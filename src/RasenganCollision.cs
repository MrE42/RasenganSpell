using System;
using UnityEngine;

namespace RasenganSpell
{
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RasenganCollision : MonoBehaviour
    {
        [Header("Damage")]
        public float baseDamage = 24f;
        public float damagePerLevel = 2f;
        public int   castingLevel = 1;

        [Header("Collision")]
        public float triggerRadius = 0.6f;
        public float lifeSeconds   = 10f;

        [Header("Player Knockback (via PlayerMovement)")]
        public float knockbackLevelDistPerLevel = 0.40f; // extra meters per level
        public float knockbackLevelDistMax      = 3.0f;  // cap so it doesn't get silly

        // Set by RasenganLogic.Init(...)
        private Transform _ownerRoot;
        private Collider[] _ownerCols = Array.Empty<Collider>();
        private Collider[] _pageCols  = Array.Empty<Collider>();

        private SphereCollider _myCol;
        private bool _consumed;
        
        public Transform OwnerRoot => _ownerRoot;

        // (optional convenience)
        public bool IsOwnedBy(Transform t) => _ownerRoot == t;
        
        /// <summary>Called by RasenganLogic right after instantiation.</summary>
        public void Init(Transform ownerRoot, int level, Collider[] pageColliders, float radius, float life)
        {
            _ownerRoot    = ownerRoot;
            castingLevel  = Mathf.Max(1, level);
            _pageCols     = pageColliders ?? Array.Empty<Collider>();
            triggerRadius = radius > 0f ? radius : triggerRadius;
            lifeSeconds = life;

        }

        private void Awake()
        {
            _myCol = GetComponent<SphereCollider>();
            _myCol.isTrigger = true;
            _myCol.radius = triggerRadius * 2;

            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            if (lifeSeconds > 0f) Destroy(gameObject, lifeSeconds);
        }
        
        private void OnEnable()
        {
            RasenganOrbRegistry.Register(this);
        }

        private void OnDestroy()
        {
            RasenganOrbRegistry.Unregister(this);
        }

        private void Start()
        {
            // Collect owner colliders to ignore self-hits
            _ownerCols = _ownerRoot
                ? _ownerRoot.GetComponentsInChildren<Collider>(includeInactive: true)
                : Array.Empty<Collider>();

            IgnoreSet(_ownerCols, "owner");
            IgnoreSet(_pageCols,  "page");

            RasenganPlugin.Log?.LogInfo(
                $"[RasenganCollision] Ready. ownerRoot={_ownerRoot?.name ?? "null"}, " +
                $"ownerCols={_ownerCols.Length}, pageCols={_pageCols.Length}, layer={gameObject.layer}");
        }

        private void IgnoreSet(Collider[] cols, string label)
        {
            if (cols == null || cols.Length == 0) return;
            int c = 0;
            foreach (var col in cols)
            {
                if (!col || col == _myCol) continue;
                Physics.IgnoreCollision(_myCol, col, true);
                c++;
            }
            RasenganPlugin.Log?.LogInfo($"[RasenganCollision] Ignoring {c}/{cols.Length} colliders from {label} set.");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_consumed || !other) return;

            // 1) PLAYER FIRST: look up the hierarchy so we catch hand/weapon/etc. colliders
            var pm = other.GetComponentInParent<PlayerMovement>();
            if (pm != null)
            {
                // Ignore our own caster
                if (_ownerRoot && pm.transform.root == _ownerRoot)
                {
                    Physics.IgnoreCollision(_myCol, other, true);
                    RasenganPlugin.Log?.LogInfo("[RasenganCollision] Ignoring owner (player).");
                    return;
                }

                if (DamagePlayer(pm))
                {
                    ApplyPlayerKnockbackViaPM(pm, castingLevel);
                    Consume("player");
                }
                else
                {
                    RasenganPlugin.Log?.LogWarning("[RasenganCollision] PlayerMovement found but no usable damage method.");
                    Physics.IgnoreCollision(_myCol, other, true);
                }
                return;
            }

            // 2) Ignore ONLY our own page colliders (safety; Start() already set IgnoreCollision)
            if (IsOurPageCollider(other))
            {
                Physics.IgnoreCollision(_myCol, other, true);
                RasenganPlugin.Log?.LogInfo("[RasenganCollision] Ignoring our page collider.");
                return;
            }

            // 3) MONSTERS/NPCs
            if (DamageMonster(other))
            {
                Consume("monster");
                return;
            }

            // 4) Non-target; ignore going forward
            RasenganPlugin.Log?.LogInfo($"[RasenganCollision] Non-target contact with '{other.name}'. Ignoring and continuing.");
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


        private static Transform GetRoot(Collider c)
        {
            if (!c) return null;
            return c.attachedRigidbody ? c.attachedRigidbody.transform.root : c.transform.root;
        }

        private float ComputeDamage() =>
            baseDamage + damagePerLevel * Mathf.Max(0, castingLevel - 1);

        private bool DamagePlayer(PlayerMovement pm)
        {
            try
            {
                float dmg = ComputeDamage();
                GameObject attacker = _ownerRoot ? _ownerRoot.gameObject : gameObject;
                RasenganPlugin.Log?.LogInfo($"[RasenganCollision] Player hit '{pm.playername}' dmg={dmg}");
                pm.DamagePlayer(dmg, attacker, "rasengan");
                return true;
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[RasenganCollision] DamagePlayer failed: {e}");
                return false;
            }
        }

        /// <summary>
        /// Finds any component under the collider’s parents or the same root that exposes
        /// HitTheMonster(float) or HitTheMonster(int) and invokes it.
        /// Prefers a component literally named "MonsterHitScript" when multiple candidates exist.
        /// </summary>
        private bool DamageMonster(Collider other)
        {
            try
            {
                float dmg = ComputeDamage() * 10;

                MonsterHitScript ms = other.gameObject.GetComponent<MonsterHitScript>();
                
                if (ms == null)
                {
                    return false;
                } else if (ms.gameObject.name == "wizardtrio (1)")
                {
                    return false;
                }
                
                ms.HitTheMonster(dmg);
                
                RasenganPlugin.Log?.LogInfo(
                        $"[RasenganCollision] Monster killed '{ms.gameObject.name}' via {ms.GetType().Name}.HitTheMonster");

                return true;
                
                    
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[RasenganCollision] DamageMonster failed: {e}");
                return false;
            }
        }

        private void ApplyPlayerKnockbackViaPM(PlayerMovement pm, int level)
        {
            try
            {
                // Direction from player -> rasengan
                Vector3 dirPlayerToRasengan = (transform.position - pm.transform.position).normalized;

                // Level-based extra distance to push the hit point beyond the Rasengan
                float extraDist = Mathf.Clamp(3 * level * knockbackLevelDistPerLevel, 0f, 3 * knockbackLevelDistMax);

                // Put the proxy a bit PAST the Rasengan along that direction
                Vector3 hit = dirPlayerToRasengan * extraDist;

                GameObject proxy = new GameObject();

                proxy.transform.position = (transform.position + hit);
                
                pm.ApplyKnockback(proxy);

                pm.velocity.y = 4 + (extraDist / 4);
                
                // Me: You know you can also change x and z here right?
                // Also Me: Nah, we use the ApplyKnockback function

            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[RasenganCollision] ApplyPlayerKnockbackViaPM failed: {e}");
            }
        }

        private void Consume(string reason)
        {
            if (_consumed) return;
            _consumed = true;
            if (_myCol) _myCol.enabled = false;
            RasenganPlugin.Log?.LogInfo($"[RasenganCollision] Consumed ({reason}).");
            Destroy(gameObject);
        }

        private static bool IsOrHasPage(Collider c) =>
            c && c.GetComponentInParent<PageController>() != null;
    }
}
