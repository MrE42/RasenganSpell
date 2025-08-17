using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RasenganSpell
{
    public class RasenganMelee : MonoBehaviour
    {
        public event Action OnFinished;

        private Transform _hand;
        private GameObject _orbVfx;

        private float _damage;
        private float _knock;
        private float _radius;
        private float _reach;

        private int _maxSwings;
        private float _duration;

        private const float Windup = 0.08f;
        private const float Active = 0.06f;
        private const float Recover = 0.12f;

        private bool _canSwing = true;
        private int _swingsUsed = 0;
        private float _endTime;
        private readonly Collider[] _hits = new Collider[24];
        private readonly HashSet<Transform> _hitThisSwing = new();

        // audio
        private AudioSource _audio;
        private AudioClip _sfxWhoosh, _sfxImpact;
        private float _impactCooldown = 0.07f;
        private float _lastImpactTime = -999f;

        public void Initialize(Transform hand, GameObject orbVfx, float damage, float knockback,
            float hitRadius, float reach, int maxSwings, float duration,
            AudioSource audio = null, AudioClip whoosh = null, AudioClip impact = null)
        {
            _hand = hand != null ? hand : transform;
            _orbVfx = orbVfx;

            _damage = damage;
            _knock = knockback;
            _radius = hitRadius;
            _reach = reach;

            _maxSwings = maxSwings;
            _duration = duration;
            _endTime = Time.time + _duration;

            if (_orbVfx && _hand) _orbVfx.transform.SetParent(_hand, true);

            _audio = audio;
            _sfxWhoosh = whoosh;
            _sfxImpact = impact;
        }

        private void Update()
        {
            if (Time.time >= _endTime) { Finish(); return; }
            if (_maxSwings >= 0 && _swingsUsed >= _maxSwings) { Finish(); return; }

            if (_canSwing && Input.GetMouseButtonDown(0))
                StartCoroutine(DoSwing());
        }

        private IEnumerator DoSwing()
        {
            _canSwing = false;
            _swingsUsed++;
            _hitThisSwing.Clear();

            if (_audio && _sfxWhoosh) _audio.PlayOneShot(_sfxWhoosh, 0.9f);
            yield return new WaitForSeconds(Windup);

            float t = 0f;
            bool hitSomething = false;
            while (t < Active)
            {
                t += Time.deltaTime;
                hitSomething |= DoActiveHit();
                yield return null;
            }

            if (hitSomething && _audio && _sfxImpact && Time.time - _lastImpactTime > _impactCooldown)
            {
                _lastImpactTime = Time.time;
                _audio.PlayOneShot(_sfxImpact, 0.85f);
            }

            yield return new WaitForSeconds(Recover);
            _canSwing = true;
        }

        private bool DoActiveHit()
        {
            if (_hand == null) return false;

            Vector3 center = _hand.position + _hand.forward * _reach;
            int count = Physics.OverlapSphereNonAlloc(center, _radius, _hits, LayerMask.GetMask("Enemy", "Hittable", "Player"));

            bool any = false;
            for (int i = 0; i < count; i++)
            {
                var col = _hits[i];
                if (col == null) continue;

                Transform root = col.attachedRigidbody ? col.attachedRigidbody.transform : col.transform.root;
                if (_hitThisSwing.Contains(root)) continue;
                _hitThisSwing.Add(root);

                var health = root.GetComponentInChildren<IDamageable>();
                if (health != null)
                {
                    health.ApplyDamage(_damage, DamageType.Magic);
                    any = true;
                }

                var rb = col.attachedRigidbody;
                if (rb != null)
                {
                    Vector3 dir = (col.transform.position - center).normalized + Vector3.up * 0.25f;
                    rb.AddForce(dir.normalized * _knock, ForceMode.Impulse);
                }
            }
            return any;
        }

        private void Finish()
        {
            if (_audio) _audio.Stop();
            if (_orbVfx) Destroy(_orbVfx);
            Destroy(this);
            OnFinished?.Invoke();
        }

        private void OnDestroy()
        {
            if (_audio) _audio.Stop();
            if (_orbVfx) Destroy(_orbVfx);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_hand == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_hand.position + _hand.forward * _reach, _radius);
        }
#endif
    }

    // Placeholder so the project compiles; replace with the game's real damage API if needed.
    public interface IDamageable { void ApplyDamage(float amount, DamageType type); }
    public enum DamageType { Magic, Physical }
}
