using System;
using System.Collections.Generic;
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
            _ownerRoot    = ownerRoot;
            castingLevel  = Mathf.Max(1, level);
            _pageCols     = pageColliders ?? Array.Empty<Collider>();
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
            var pm = other.gameObject.GetComponent<PlayerMovement>();
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

            // 2) MONSTER/NPC: anything under the root with HitTheMonster(float|int)?
            if (DamageMonster(other))
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
                float dmg = ComputeDamage();

                //var parentCandidates = other.GetComponentsInParent<MonsterHitScript>(true);
                //var rootCandidates =
                //    (other ? other.GetComponentsInChildren<MonsterHitScript>(true) : Array.Empty<MonsterHitScript>());
                
                // Combine (parents first), then root
                //IEnumerable<MonsterHitScript> candidates = parentCandidates.Concat(rootCandidates)
                //    .Where(mb => mb != null);

                //MonsterHitScript ms = candidates.First();

                MonsterHitScript ms = other.gameObject.GetComponent<MonsterHitScript>();
                
                if (ms == null)
                {
                    return false;
                } else if (ms.gameObject.name == "wizardtrio (1)")
                {
                    return false;
                }
                
                ms.HitTheMonster(10000);
                
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
