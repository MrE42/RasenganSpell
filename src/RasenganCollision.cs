using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using BlackMagicAPI.Modules.Spells; // PageController

namespace RasenganSpell
{
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RasenganCollision : MonoBehaviour
    {
        [Header("Damage")]
        public float baseDamage = 24f;
        public float damagePerLevel = 6f;
        public int   castingLevel = 1;

        [Header("Collision")]
        public float triggerRadius = 0.6f;
        public float lifeSeconds   = 6f;

        [Header("Knockback")]
        public float knockbackForce  = 12f;
        public float knockbackUpward = 0.25f;

        // Set by RasenganLogic.Init(...)
        private Transform _ownerRoot;
        private Collider[] _ownerCols = Array.Empty<Collider>();
        private Collider[] _pageCols  = Array.Empty<Collider>();

        private SphereCollider _myCol;
        private bool _consumed;

        /// <summary>Called by RasenganLogic right after instantiation.</summary>
        public void Init(Transform ownerRoot, int level, Collider[] pageColliders, float radius)
        {
            _ownerRoot   = ownerRoot;
            castingLevel = Mathf.Max(1, level);
            _pageCols    = pageColliders ?? Array.Empty<Collider>();
            triggerRadius = radius > 0f ? radius : triggerRadius;
        }

        private void Awake()
        {
            _myCol = GetComponent<SphereCollider>();
            _myCol.isTrigger = true;
            _myCol.radius = triggerRadius;

            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            if (lifeSeconds > 0f) Destroy(gameObject, lifeSeconds);
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

            // Skip owner & our own page hierarchies completely
            var root = GetRoot(other);
            if (_ownerRoot && root == _ownerRoot)
            {
                Physics.IgnoreCollision(_myCol, other, true);
                return;
            }
            if (IsOrHasPage(other))
            {
                Physics.IgnoreCollision(_myCol, other, true);
                return;
            }

            // 1) PLAYER: PlayerMovement present?
            var pm = other.GetComponentInParent<PlayerMovement>();
            if (pm != null)
            {
                // Do not damage our own caster
                if (_ownerRoot && pm.transform.root == _ownerRoot)
                {
                    Physics.IgnoreCollision(_myCol, other, true);
                    return;
                }

                if (DamagePlayer(pm))
                {
                    ApplyKnockback(other);
                    Consume("player");
                }
                else
                {
                    RasenganPlugin.Log?.LogWarning("[RasenganCollision] PlayerMovement found but no usable damage method. Ignoring this collider.");
                    Physics.IgnoreCollision(_myCol, other, true);
                }
                return;
            }

            // 2) MONSTER/NPC: MonsterHitScript somewhere under the same root?
            if (DamageMonster(other, root))
            {
                ApplyKnockback(other);
                Consume("monster");
                return;
            }

            // 3) Non-target; keep moving and ignore further collisions with this collider
            RasenganPlugin.Log?.LogInfo($"[RasenganCollision] Non-target contact with '{other.name}'. Ignoring and continuing.");
            Physics.IgnoreCollision(_myCol, other, true);
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
                var attackerGo = _ownerRoot ? _ownerRoot.gameObject : gameObject;
                RasenganPlugin.Log?.LogInfo($"[RasenganCollision] Player hit '{pm.playername}' dmg={dmg}");

                var t = pm.GetType();

                // Prefer NonRpcDamagePlayer if available, else DamagePlayer. Try common signatures.
                var nonRpc = t.GetMethod("NonRpcDamagePlayer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (nonRpc != null && TryInvokeDamageLike(nonRpc, pm, dmg, attackerGo))
                    return true;

                var local = t.GetMethod("DamagePlayer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (local != null && TryInvokeDamageLike(local, pm, dmg, attackerGo))
                    return true;

                return false;
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[RasenganCollision] DamagePlayer failed: {e}");
                return false;
            }
        }

        /// <summary>
        /// Calls MonsterHitScript.HitTheMonster(float) on any such component under the same root.
        /// First tries parents of the collider; if not found, searches the entire root hierarchy.
        /// </summary>
        private bool DamageMonster(Collider other, Transform root)
        {
            try
            {
                float dmg = ComputeDamage();

                // --- Try on the collider's parent chain (fast path)
                var comp = other.GetComponentsInParent<MonoBehaviour>(true)
                                .FirstOrDefault(mb => mb && mb.GetType().Name == "MonsterHitScript");

                // --- If not found there, search anywhere under the same root
                if (comp == null && root)
                {
                    comp = root.GetComponentsInChildren<MonoBehaviour>(true)
                               .FirstOrDefault(mb => mb && mb.GetType().Name == "MonsterHitScript");
                }

                if (comp == null)
                {
                    // For troubleshooting, show a small list of components on the root once per contact
                    var compNames = root ? root.GetComponentsInChildren<MonoBehaviour>(true)
                                                .Where(x => x).Select(x => x.GetType().Name).Distinct().Take(10)
                                                .ToArray() : Array.Empty<string>();
                    RasenganPlugin.Log?.LogInfo($"[RasenganCollision] No MonsterHitScript on '{root?.name ?? other.name}'. Components: {string.Join(", ", compNames)}");
                    return false;
                }

                // Look specifically for HitTheMonster(float) or (int)
                var t = comp.GetType();
                var floatMethod = t.GetMethod("HitTheMonster",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(float) },
                    modifiers: null);

                if (floatMethod != null)
                {
                    floatMethod.Invoke(comp, new object[] { dmg });
                    RasenganPlugin.Log?.LogInfo($"[RasenganCollision] Monster hit '{comp.gameObject.name}' via MonsterHitScript.HitTheMonster(float) dmg={dmg}");
                    return true;
                }

                var intMethod = t.GetMethod("HitTheMonster",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(int) },
                    modifiers: null);

                if (intMethod != null)
                {
                    intMethod.Invoke(comp, new object[] { Mathf.RoundToInt(dmg) });
                    RasenganPlugin.Log?.LogInfo($"[RasenganCollision] Monster hit '{comp.gameObject.name}' via MonsterHitScript.HitTheMonster(int) dmg={dmg}");
                    return true;
                }

                RasenganPlugin.Log?.LogWarning("[RasenganCollision] MonsterHitScript found but no HitTheMonster(float) or (int) method.");
                return false;
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[RasenganCollision] DamageMonster failed: {e}");
                return false;
            }
        }

        /// <summary>
        /// Tries a few common PlayerMovement damage signatures:
        /// (float), (float, GameObject), (float, GameObject, string)
        /// </summary>
        private bool TryInvokeDamageLike(MethodInfo mi, object target, float dmg, GameObject attacker)
        {
            try
            {
                var ps = mi.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(float))
                {
                    mi.Invoke(target, new object[] { dmg });
                    return true;
                }
                if (ps.Length == 2 && ps[0].ParameterType == typeof(float) && ps[1].ParameterType == typeof(GameObject))
                {
                    mi.Invoke(target, new object[] { dmg, attacker });
                    return true;
                }
                if (ps.Length == 3 &&
                    ps[0].ParameterType == typeof(float) &&
                    ps[1].ParameterType == typeof(GameObject) &&
                    ps[2].ParameterType == typeof(string))
                {
                    mi.Invoke(target, new object[] { dmg, attacker, "rasengan" });
                    return true;
                }
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[RasenganCollision] Invoke {mi.DeclaringType.Name}.{mi.Name} failed: {e.Message}");
            }
            return false;
        }

        private void ApplyKnockback(Collider other)
        {
            try
            {
                var rb = other.attachedRigidbody
                         ? other.attachedRigidbody
                         : other.GetComponentInParent<Rigidbody>();
                if (!rb) return;

                var dir = (other.bounds.center - transform.position).normalized;
                dir.y += knockbackUpward;
                rb.AddForce(dir * knockbackForce, ForceMode.Impulse);
            }
            catch { /* non-fatal */ }
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
