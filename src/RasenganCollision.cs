using System.Linq;
using System.Text;
using UnityEngine;

namespace RasenganSpell
{
    /// <summary>
    /// One-and-done Rasengan collision with robust debugging and ignores.
    /// - Ensures trigger SphereCollider + kinematic Rigidbody.
    /// - Ignores the caster and the page.
    /// - On hit, tries hard to resolve the other Player (not self) and apply damage+knockback.
    /// - Emits very detailed logs to diagnose false "non-player" hits.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RasenganCollision : MonoBehaviour
    {
        [Header("Damage")]
        [Tooltip("Base damage at castingLevel = 1.")]
        public float baseDamage = 24f;

        [Tooltip("Extra damage per casting level above 1.")]
        public float damagePerLevel = 6f;

        [Tooltip("Casting level supplied by RasenganLogic.")]
        public int castingLevel = 1;

        [Header("Collision")]
        [Tooltip("Trigger radius for detecting targets.")]
        public float triggerRadius = 0.6f;

        [Tooltip("Lifetime fail-safe; orb despawns after this many seconds if it never hits.")]
        public float lifeSeconds = 6f;

        [Header("Knockback")]
        [Tooltip("Impulse force applied to the hit target.")]
        public float knockbackForce = 12f;

        [Tooltip("Upward bias added to knockback direction (helps pop targets slightly).")]
        public float knockbackUpward = 0.25f;

        // Provided by RasenganLogic
        private Transform _ownerRoot;
        private Collider[] _ownerColliders = System.Array.Empty<Collider>();
        private Collider[] _pageColliders  = System.Array.Empty<Collider>();

        private SphereCollider _myCol;
        private bool _consumed;

        // Optional tag/layer heuristics to recognize players in case PlayerMovement
        // isn't directly on the collider we hit.
        private static readonly string[] PlayerTags   = { "Player", "player" };
        private static readonly string[] PlayerLayers = { "Player", "Players" };

        /// <summary>Initialize with owner transform, casting level and extra ignores (page colliders).</summary>
        public void Init(Transform ownerRoot, int level, Collider[] pageColliders)
        {
            _ownerRoot     = ownerRoot;
            castingLevel   = Mathf.Max(1, level);
            _pageColliders = pageColliders ?? System.Array.Empty<Collider>();
        }

        private void Awake()
        {
            _myCol = GetComponent<SphereCollider>();
            _myCol.isTrigger = true;
            if (triggerRadius > 0f) _myCol.radius = triggerRadius;

            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;

            // Safety self-destruct
            if (lifeSeconds > 0f) Destroy(gameObject, lifeSeconds);
        }

        private void Start()
        {
            // Gather owner colliders to ignore (avoid self-hit)
            if (_ownerRoot != null)
                _ownerColliders = _ownerRoot.GetComponentsInChildren<Collider>(includeInactive: true);
            else
                _ownerColliders = System.Array.Empty<Collider>();

            IgnoreSet(_ownerColliders, "owner");
            IgnoreSet(_pageColliders,  "page");

            // Try to sit on a harmless layer
            int layer = LayerMask.NameToLayer("IgnoreRaycast");
            if (layer >= 0) gameObject.layer = layer;

            RasenganPlugin.Log?.LogDebug($"[RasenganCollision] Init done. ownerRoot={_ownerRoot?.name ?? "null"}, " +
                                         $"ownerCols={_ownerColliders.Length}, pageCols={_pageColliders.Length}, myLayer={gameObject.layer}");
        }

        private void IgnoreSet(Collider[] cols, string setName)
        {
            if (cols == null || cols.Length == 0) return;

            foreach (var c in cols)
            {
                if (!c || c == _myCol) continue;
                Physics.IgnoreCollision(_myCol, c, true);
            }

            RasenganPlugin.Log?.LogDebug($"[RasenganCollision] Ignoring {cols.Length} colliders from {setName} set.");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_consumed || other == null) return;

            // Dump a very detailed record for this contact
            DebugDumpContact("ENTER", other);

            Transform otherRoot = other.attachedRigidbody
                ? other.attachedRigidbody.transform.root
                : other.transform.root;

            // 1) Ignore ourselves (owner)
            if (_ownerRoot != null && otherRoot == _ownerRoot)
            {
                RasenganPlugin.Log?.LogDebug("[RasenganCollision] Ignored collision with caster (ownerRoot match).");
                Physics.IgnoreCollision(_myCol, other, true);
                return;
            }

            // 2) Ignore touching any PageController hierarchy
            var pageOnOther = other.GetComponentInParent<PageController>();
            if (pageOnOther != null)
            {
                RasenganPlugin.Log?.LogInfo($"[RasenganCollision] Touch PageController '{pageOnOther.name}' -> ignoring.");
                Physics.IgnoreCollision(_myCol, other, true);
                return;
            }

            // 3) Try to get PlayerMovement directly
            var pm = other.GetComponentInParent<PlayerMovement>();

            // 3b) If not found, use tag/layer/name heuristics to locate a plausible player root,
            // then hunt for PlayerMovement beneath it.
            if (pm == null)
            {
                bool tagLooksPlayer   = PlayerTags.Contains(other.tag) || PlayerTags.Contains(otherRoot.tag);
                bool layerLooksPlayer = PlayerLayers.Contains(LayerMask.LayerToName(other.gameObject.layer)) ||
                                        PlayerLayers.Contains(LayerMask.LayerToName(otherRoot.gameObject.layer));
                bool nameLooksPlayer  = otherRoot.name.Contains("Player");

                if (tagLooksPlayer || layerLooksPlayer || nameLooksPlayer)
                {
                    var candidateRoot = otherRoot;
                    pm = candidateRoot.GetComponentInChildren<PlayerMovement>(true);
                    if (pm != null)
                    {
                        RasenganPlugin.Log?.LogDebug("[RasenganCollision] Resolved PlayerMovement via heuristics (tag/layer/name).");
                    }
                }
            }

            // 3c) If still not found, try one more time from the collider gameObject instead of root (edge cases)
            if (pm == null)
            {
                pm = other.GetComponent<PlayerMovement>();
                if (pm != null)
                    RasenganPlugin.Log?.LogDebug("[RasenganCollision] Resolved PlayerMovement on the collider object directly.");
            }

            // 4) Player found?
            if (pm != null)
            {
                // Guard against self
                if (_ownerRoot != null && pm.transform.root == _ownerRoot)
                {
                    RasenganPlugin.Log?.LogDebug("[RasenganCollision] Found PlayerMovement but it is the caster; ignoring.");
                    Physics.IgnoreCollision(_myCol, other, true);
                    return;
                }

                if (pm.isDead)
                {
                    RasenganPlugin.Log?.LogDebug("[RasenganCollision] Found PlayerMovement but target is dead; ignoring.");
                    Physics.IgnoreCollision(_myCol, other, true);
                    return;
                }

                _consumed = true;

                float dmg = baseDamage + damagePerLevel * (castingLevel - 1);
                string pname = pm.playername;

                RasenganPlugin.Log?.LogInfo($"[RasenganCollision] *** PLAYER HIT *** target='{pname}' damage={dmg} level={castingLevel}");

                // Damage entry point (host authoritative in MP)
                pm.NonRpcDamagePlayer(dmg, null, "rasengan");

                // Knockback (best effort)
                var hitRb = other.attachedRigidbody ?? pm.GetComponent<Rigidbody>() ?? pm.transform.root.GetComponent<Rigidbody>();
                if (hitRb != null)
                {
                    Vector3 dir = (other.bounds.center - transform.position).normalized;
                    dir += Vector3.up * knockbackUpward;
                    hitRb.AddForce(dir.normalized * knockbackForce, ForceMode.Impulse);
                    RasenganPlugin.Log?.LogDebug("[RasenganCollision] Applied knockback to player.");
                }
                else
                {
                    RasenganPlugin.Log?.LogDebug("[RasenganCollision] Player root has no Rigidbody, skipping knockback.");
                }

                Destroy(gameObject);
                return;
            }

            // 5) Non-player: if it has a rigidbody, give it a shove and despawn
            var rb2 = other.attachedRigidbody ?? otherRoot.GetComponent<Rigidbody>();
            if (rb2 != null)
            {
                _consumed = true;
                Vector3 dir = (other.bounds.center - transform.position).normalized;
                dir += Vector3.up * knockbackUpward;
                rb2.AddForce(dir.normalized * knockbackForce, ForceMode.Impulse);

                RasenganPlugin.Log?.LogInfo("[RasenganCollision] Collided with non-player object, applied knockback (despawning).");
                Destroy(gameObject);
            }
            else
            {
                // No player and no rigidbody? Just ignore further collisions with this specific collider
                Physics.IgnoreCollision(_myCol, other, true);
                RasenganPlugin.Log?.LogDebug("[RasenganCollision] Non-player & no-Rigidbody; added ignore for this collider.");
            }
        }

        /// <summary>
        /// Very verbose contact dump to help identify what we're actually touching.
        /// </summary>
        private void DebugDumpContact(string phase, Collider col)
        {
            if (RasenganPlugin.Log == null) return;

            var root = col.attachedRigidbody ? col.attachedRigidbody.transform.root : col.transform.root;
            var sb = new StringBuilder();

            sb.AppendLine($"[RasenganCollision] {phase} with collider='{col.name}' " +
                          $"(layer={col.gameObject.layer} '{LayerMask.LayerToName(col.gameObject.layer)}', tag='{col.tag}'), " +
                          $"root='{root.name}' (layer={root.gameObject.layer} '{LayerMask.LayerToName(root.gameObject.layer)}', tag='{root.tag}')");

            // Print immediate parent chain up to 6 levels
            sb.AppendLine("  Hierarchy (up to 6):");
            var t = col.transform;
            int depth = 0;
            while (t != null && depth < 6)
            {
                sb.AppendLine($"    [{depth}] {t.name} (layer {t.gameObject.layer}:{LayerMask.LayerToName(t.gameObject.layer)}, tag '{t.tag}')");
                t = t.parent;
                depth++;
            }

            // Show key components on root (capped)
            var comps = root.GetComponentsInChildren<Component>(true)
                            .Take(16)
                            .Select(c => c != null ? c.GetType().Name : "null").ToArray();
            sb.Append("  Root has components:");
            foreach (var c in comps) sb.Append($" {c},");
            sb.AppendLine();

            // Explicit probes
            var foundPage   = col.GetComponentInParent<PageController>();
            var foundPlayer = col.GetComponentInParent<PlayerMovement>();
            sb.AppendLine($"  Probes: PageController={(foundPage ? "YES" : "no")}, PlayerMovement={(foundPlayer ? "YES" : "no")}");
            if (foundPage)
                sb.AppendLine($"    - PageController name: {foundPage.name}");
            if (foundPlayer)
                sb.AppendLine($"    - PlayerMovement playername: {foundPlayer.playername}");

            // Owner context
            sb.AppendLine($"  OwnerRoot: {(_ownerRoot ? _ownerRoot.name : "null")}");

            RasenganPlugin.Log.LogDebug(sb.ToString());
        }
    }
}
