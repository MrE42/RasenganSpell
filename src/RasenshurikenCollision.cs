using System;
using UnityEngine;

namespace RasenganSpell
{
    /// <summary>
    /// Collision for Rasenshuriken. Starts “incomplete” (acts like Rasengan with base dmg),
    /// can be upgraded to “complete” by a teammate interaction. When complete, right-click
    /// throw is enabled and damage is higher. Reuses the same target logic as Rasengan.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RasenshurikenCollision : MonoBehaviour
    {
        [Header("Damage")]
        public float baseDamageIncomplete = 24f;
        public float baseDamageComplete   = 42f;
        public float damagePerLevel       = 4f;
        public int   castingLevel         = 1;

        [Header("Collision")]
        public float triggerRadius = 0.6f;
        public float lifeSeconds   = 10f;

        [Header("Throw")]
        public bool  isProjectile;
        public float throwSpeed       = 26f;
        public float projectileLife   = 4f;
        public float projectileSpin   = 720f; // purely visual spin

        [Header("Knockback (via PlayerMovement)")]
        public float knockbackLevelDistPerLevel = 0.40f;
        public float knockbackLevelDistMax      = 3.0f;

        // state
        public bool IsComplete { get; private set; }

        // ownership & ignores
        private Transform _ownerRoot;
        private Collider[] _ownerCols = Array.Empty<Collider>();
        private Collider[] _pageCols  = Array.Empty<Collider>();

        // cached
        private SphereCollider _myCol;
        private Rigidbody _rb;
        private bool _consumed;

        public Transform OwnerRoot => _ownerRoot;

        public void Init(Transform ownerRoot, int level, Collider[] pageColliders, float life, bool complete)
        {
            _ownerRoot  = ownerRoot;
            castingLevel = Mathf.Max(1, level);
            _pageCols   = pageColliders ?? Array.Empty<Collider>();
            lifeSeconds = life;
            IsComplete  = complete;
        }

        private void Awake()
        {
            _myCol = GetComponent<SphereCollider>();
            _myCol.isTrigger = true;
            _myCol.radius = triggerRadius * 2;

            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;

            if (lifeSeconds > 0f) Destroy(gameObject, lifeSeconds);
        }

        private void OnEnable()   => RasenganOrbRegistry.RegisterAsRasenganCompat(this);
        private void OnDestroy()  => RasenganOrbRegistry.UnregisterRasenganCompat(this);

        private void Start()
        {
            // ignore owner/page colliders like RasenganCollision
            _ownerCols = _ownerRoot
                ? _ownerRoot.GetComponentsInChildren<Collider>(includeInactive: true)
                : Array.Empty<Collider>();

            IgnoreSet(_ownerCols);
            IgnoreSet(_pageCols);
        }

        private void IgnoreSet(Collider[] cols)
        {
            if (cols == null || cols.Length == 0) return;
            foreach (var col in cols)
            {
                if (!col || col == _myCol) continue;
                Physics.IgnoreCollision(_myCol, col, true);
            }
        }

        private void Update()
        {
            if (isProjectile && _rb)
            {
                // add simple spin for visuals during flight
                transform.Rotate(Vector3.up, projectileSpin * Time.deltaTime, Space.World);
            }
        }

        public void UpgradeToComplete()
        {
            if (IsComplete) return;
            IsComplete = true;
            RasenganPlugin.Log?.LogInfo("[Rasenshuriken] Upgraded to COMPLETE (wind nature applied).");

            // Optional: tweak scale/FX slightly when complete
            transform.localScale = new Vector3(0.58f, 0.58f, 0.58f);
        }

        public void ThrowForward(Transform ownerRoot)
        {
            if (!IsComplete || isProjectile) return;

            // detach from page/hand
            transform.SetParent(null, true);

            // switch collider/rigidbody for flight
            isProjectile = true;
            _rb.isKinematic = false;
            _rb.useGravity = false;

            // enable trigger stays true to reuse same hit logic on trigger enters,
            // give forward velocity from owner's forward
            var fwd = ownerRoot ? ownerRoot.forward : transform.forward;
            _rb.velocity = fwd.normalized * throwSpeed;

            // shorten lifetime as projectile
            Destroy(gameObject, projectileLife);

            RasenganPlugin.Log?.LogInfo("[Rasenshuriken] Thrown!");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_consumed || !other) return;

            // OWNER: ignore
            var pm = other.GetComponentInParent<PlayerMovement>();
            if (pm != null)
            {
                if (_ownerRoot && pm.transform.root == _ownerRoot)
                {
                    Physics.IgnoreCollision(_myCol, other, true);
                    return;
                }

                if (DamagePlayer(pm))
                {
                    ApplyPlayerKnockbackViaPM(pm, castingLevel);
                    Consume("player");
                }
                else
                {
                    Physics.IgnoreCollision(_myCol, other, true);
                }
                return;
            }

            // page colliders ignore
            if (IsOurPageCollider(other))
            {
                Physics.IgnoreCollision(_myCol, other, true);
                return;
            }

            // monsters
            if (DamageMonster(other))
            {
                Consume("monster");
                return;
            }

            // non-target
            Physics.IgnoreCollision(_myCol, other, true);
        }

        private bool IsOurPageCollider(Collider c)
        {
            if (!c || _pageCols == null) return false;
            foreach (var pc in _pageCols)
            {
                if (!pc) continue;
                if (c == pc) return true;
                if (c.transform.IsChildOf(pc.transform)) return true;
            }
            return false;
        }

        private float ComputeDamage()
        {
            float baseD = IsComplete ? baseDamageComplete : baseDamageIncomplete;
            return baseD + damagePerLevel * Mathf.Max(0, castingLevel - 1);
        }

        private bool DamagePlayer(PlayerMovement pm)
        {
            try
            {
                float dmg = ComputeDamage();
                GameObject attacker = _ownerRoot ? _ownerRoot.gameObject : gameObject;
                pm.DamagePlayer(dmg, attacker, IsComplete ? "rasenshuriken" : "rasengan");
                return true;
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasenshuriken] DamagePlayer failed: {e}");
                return false;
            }
        }

        private bool DamageMonster(Collider other)
        {
            try
            {
                float dmg = ComputeDamage() * 10;

                MonsterHitScript ms = other.gameObject.GetComponent<MonsterHitScript>();
                if (ms == null) return false;
                if (ms.gameObject.name == "wizardtrio (1)") return false;

                ms.HitTheMonster(dmg);
                return true;
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasenshuriken] DamageMonster failed: {e}");
                return false;
            }
        }

        private void ApplyPlayerKnockbackViaPM(PlayerMovement pm, int level)
        {
            try
            {
                Vector3 dirPlayerToOrb = (transform.position - pm.transform.position).normalized;
                float extraDist = Mathf.Clamp(3 * level * knockbackLevelDistPerLevel, 0f, 3 * knockbackLevelDistMax);
                Vector3 hit = dirPlayerToOrb * extraDist;

                GameObject proxy = new GameObject();
                proxy.transform.position = (transform.position + (3 * hit));
                pm.ApplyKnockback(proxy);
                Destroy(proxy);

                pm.velocity.y = 10 + (extraDist / 4);
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasenshuriken] ApplyKnockback failed: {e}");
            }
        }

        private void Consume(string reason)
        {
            if (_consumed) return;
            _consumed = true;
            if (_myCol) _myCol.enabled = false;
            Destroy(gameObject);
        }
    }
}
