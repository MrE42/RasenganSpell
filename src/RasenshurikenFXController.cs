using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RasenganSpell
{
    /// <summary>
    /// Spins visual layers and owns the Rasenshuriken phases:
    /// Incomplete (rasengan_11) -> Upgrading (enable effect_3 + scale up) -> Complete (enable wind_0).
    /// Handles coop "wind infusion" and owner right-click throw once complete.
    /// Includes debug hotkeys to emulate the second player.
    /// </summary>
    public class RasenshurikenFXController : MonoBehaviour
    {
        // ===== Debug hotkeys =====
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private bool enableDebugHotkeys = true;
#else
        [SerializeField] private bool enableDebugHotkeys = false; // flip true if you want this in release builds
#endif
        [SerializeField] private KeyCode debugInfuseKey   = KeyCode.F6;   // emulate teammate infusion (runs UpgradeRoutine)
        [SerializeField] private KeyCode debugCompleteKey = KeyCode.None; // optional: e.g., F7 forces instant completion
        [SerializeField] private bool    logDebugHotkeys  = true;

        // ===== Spinner & Emission =====
        public enum AxisSpace { World, Self }

        [Serializable]
        public class Layer
        {
            // Targets
            public Transform     Transform;
            public MeshRenderer  Renderer; // optional (emission disabled if null)

            // Spin
            public float     SpinSpeed = 600f;
            public AxisSpace AxisSpace = AxisSpace.Self;
            public Vector3   Axis      = Vector3.up;

            // Emission (applied via MaterialPropertyBlock to avoid touching shared materials)
            public Color BaseColor   = new Color(0.20f, 0.60f, 1f, 1f); // bluish
            public float EmissionMin = 0.35f;
            public float EmissionMax = 1.20f;
            public float FlickerFreq = 2.5f; // Hz
            public float FlickerNoise = 0.5f; // 0..1 perlin mix

            // runtime
            [NonSerialized] public MaterialPropertyBlock _mpb;
            [NonSerialized] public float _seed;
        }

        public List<Layer> layers = new();

        void Awake() => InitLayerRuntime();

        private void InitLayerRuntime()
        {
            foreach (var L in layers)
            {
                if (L == null) continue;
                L._seed = UnityEngine.Random.value * 1000f;
                if (L.Renderer && L._mpb == null) L._mpb = new MaterialPropertyBlock();
            }
        }

        void Update()
        {
            // Spin layers
            foreach (var L in layers)
            {
                if (!L?.Transform) continue;
                var axisW = (L.AxisSpace == AxisSpace.Self) ? L.Transform.TransformDirection(L.Axis) : L.Axis;
                L.Transform.Rotate(axisW.normalized, L.SpinSpeed * Time.deltaTime, Space.World);
            }

            // Emission flicker
            var t = Time.time;
            foreach (var L in layers)
            {
                if (L?.Renderer == null) continue;

                // base sine + perlin -> 0..1
                float s = 0.5f + 0.5f * Mathf.Sin((t + L._seed) * (Mathf.PI * 2f) * L.FlickerFreq);
                float p = Mathf.PerlinNoise(t * 0.75f + L._seed, 0.123f);
                float u = Mathf.Clamp01(Mathf.Lerp(s, p, Mathf.Clamp01(L.FlickerNoise)));

                float intensity = Mathf.Lerp(L.EmissionMin, L.EmissionMax, u);
                var emiss = L.BaseColor * Mathf.LinearToGammaSpace(intensity);

                if (L._mpb == null) L._mpb = new MaterialPropertyBlock();
                L.Renderer.GetPropertyBlock(L._mpb);
                L._mpb.SetColor("_EmissionColor", emiss);
                L.Renderer.SetPropertyBlock(L._mpb);

                // Try to enable emission keyword so it's actually visible on Standard/Lit shaders
                try
                {
                    var mat = L.Renderer.sharedMaterial;
                    if (mat && !mat.IsKeywordEnabled("_EMISSION")) mat.EnableKeyword("_EMISSION");
                }
                catch { /* ignore if material doesn't support keyword */ }
            }

            TickDebugHotkeys();   // debug override
            TickCoopUpgrade();    // teammate infusion (hold E nearby)
            TickOwnerThrow();     // owner right-click when complete
        }

        // ===== Auto-setup that matches your screenshots =====
        public static RasenshurikenFXController AttachAndAutoSetup(GameObject root)
        {
            if (!root) return null;
            var ctrl = root.GetComponent<RasenshurikenFXController>();
            if (!ctrl) ctrl = root.AddComponent<RasenshurikenFXController>();
            ctrl.layers.Clear();

            Transform FindT(string nameOrContains)
            {
                Transform exact = null, contains = null;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == nameOrContains) { exact = t; break; }
                    if (!contains && t.name.IndexOf(nameOrContains, StringComparison.OrdinalIgnoreCase) >= 0)
                        contains = t;
                }
                return exact ? exact : contains;
            }

            MeshRenderer FindR(Transform t) => t ? (t.GetComponent<MeshRenderer>() ?? t.GetComponentInChildren<MeshRenderer>(true)) : null;

            var obj23   = FindT("Object_23");
            var obj25   = FindT("Object_25");
            var obj27   = FindT("Object_27");
            var obj20   = FindT("Object_20");
            var obj17   = FindT("Object_17");
            var effect3 = FindT("effect_3");
            var wind0   = FindT("wind_0");

            // Elements 0..6 (from your inspector screenshots)
            if (obj23) ctrl.layers.Add(new Layer { Transform = obj23, Renderer = FindR(obj23), SpinSpeed = 600f, AxisSpace = AxisSpace.Self, Axis = new Vector3(0,1,0) });
            if (obj25) ctrl.layers.Add(new Layer { Transform = obj25, Renderer = FindR(obj25), SpinSpeed = 600f, AxisSpace = AxisSpace.Self, Axis = new Vector3(0,1,0) });
            if (obj27) ctrl.layers.Add(new Layer { Transform = obj27, Renderer = FindR(obj27), SpinSpeed = 600f, AxisSpace = AxisSpace.Self, Axis = new Vector3(0,0,1) });
            if (obj20) ctrl.layers.Add(new Layer { Transform = obj20, Renderer = FindR(obj20), SpinSpeed = 600f, AxisSpace = AxisSpace.Self, Axis = new Vector3(0,1,0) });
            if (obj17) ctrl.layers.Add(new Layer { Transform = obj17, Renderer = FindR(obj17), SpinSpeed =  60f, AxisSpace = AxisSpace.Self, Axis = new Vector3(1,0,0) });

            // Phase objects (no emission per your note)
            if (effect3) ctrl.layers.Add(new Layer { Transform = effect3, Renderer = null, SpinSpeed = -120f, AxisSpace = AxisSpace.Self, Axis = new Vector3(0,1,0) });
            if (wind0)   ctrl.layers.Add(new Layer { Transform = wind0,   Renderer = null, SpinSpeed = -600f, AxisSpace = AxisSpace.Self, Axis = new Vector3(0,1,0) });

            ctrl.InitLayerRuntime();
            return ctrl;
        }

        // ===== Phase system (incomplete -> upgrade -> complete) + coop + throw =====
        [Header("Phase binding (substring match)")]
        [SerializeField] string rasengan11Key = "rasengan_11";
        [SerializeField] string effect3Key    = "effect_3";
        [SerializeField] string wind0Key      = "wind_0";

        Transform _rasengan11, _effect3, _wind0;

        [Header("Upgrade settings")]
        [SerializeField] float  holdToInfuseSeconds = 0.5f;
        [SerializeField] float  coopRadius          = 1.2f;
        [SerializeField] float  upgradeDuration     = 2.5f;
        [SerializeField] float  scaleMultiplier     = 1.25f;

        float _holdTimer;
        bool  _upgrading;
        public bool IsComplete { get; private set; }

        // external
        Transform _ownerRoot;                  // caster (only they can throw)
        RasenshurikenCollision _collision;     // damage/throw logic
        Transform _pageAnchor;                 // hand/page parent (so we can detach on throw)
        Action _releasePageVisibility;         // callback to re-show the page when thrown

        /// <summary>Wire phases, ownership, and callbacks.</summary>
        public void Init(Transform ownerRoot,
                         RasenshurikenCollision collision,
                         Transform pageAnchor,
                         Action releasePageVisibility)
        {
            _ownerRoot = ownerRoot;
            _collision = collision;
            _pageAnchor = pageAnchor;
            _releasePageVisibility = releasePageVisibility;

            var root = transform;
            _rasengan11 = FindBySubstring(root, rasengan11Key);
            _effect3    = FindBySubstring(root, effect3Key);
            _wind0      = FindBySubstring(root, wind0Key);

            if (!_rasengan11) Debug.LogWarning("[RasenshurikenFX] Could not find 'rasengan_11'.");
            if (!_effect3)    Debug.LogWarning("[RasenshurikenFX] Could not find 'effect_3'.");
            if (!_wind0)      Debug.LogWarning("[RasenshurikenFX] Could not find 'wind_0'.");

            // start incomplete
            SetActive(_rasengan11, true);
            SetActive(_effect3,   false);
            SetActive(_wind0,     false);

            IsComplete = false;
            _upgrading = false;
            _holdTimer = 0f;
        }

        // --- Coop upgrade (nearby non-owner holds E) ---
        void TickCoopUpgrade()
        {
            if (IsComplete || _upgrading || _collision == null) return;

            var nearby = FindNonOwnerPlayerNearby(transform.position, coopRadius, _ownerRoot);
            if (!nearby) { _holdTimer = 0f; return; }

            if (Input.GetKey(KeyCode.E))
            {
                _holdTimer += Time.deltaTime;
                if (_holdTimer >= holdToInfuseSeconds)
                {
                    _holdTimer = 0f;
                    StartCoroutine(UpgradeRoutine());
                }
            }
            else _holdTimer = 0f;
        }

        IEnumerator UpgradeRoutine()
        {
            _upgrading = true;

            // 1) enable effect_3
            SetActive(_effect3, true);

            // 2) scale whole object up smoothly
            var t0 = Time.time;
            var start = transform.localScale;
            var end   = start * Mathf.Max(1.01f, scaleMultiplier);

            while (Time.time - t0 < upgradeDuration)
            {
                float u = Mathf.Clamp01((Time.time - t0) / upgradeDuration);
                // smoothstep
                u = u * u * (3f - 2f * u);
                transform.localScale = Vector3.Lerp(start, end, u);
                yield return null;
            }
            transform.localScale = end;

            // 3) enable wind_0
            SetActive(_wind0, true);

            // 4) mark complete + notify collision (bumps damage / unlocks throw)
            _upgrading = false;
            IsComplete = true;
            _collision?.UpgradeToComplete();
        }

        // --- Owner right-click throw (allowed only when complete) ---
        void TickOwnerThrow()
        {
            if (!IsComplete || _collision == null || _ownerRoot == null) return;
            if (Input.GetMouseButtonDown(1))
            {
                _collision.ThrowForward(_ownerRoot); // projectile launch

                _releasePageVisibility?.Invoke();    // page visible again
                if (_pageAnchor && transform.parent == _pageAnchor)
                    transform.SetParent(null, true);  // detach from hand
            }
        }

        // ===== Debug hotkeys =====
        private void TickDebugHotkeys()
        {
            if (!enableDebugHotkeys) return;

            // F6: simulate teammate infusion (run full upgrade)
            if (Input.GetKeyDown(debugInfuseKey))
            {
                if (!IsComplete && !_upgrading)
                {
                    if (logDebugHotkeys) Debug.Log("[RasenshurikenFX] DEBUG: Simulating teammate infusion (upgrade start).");
                    StartCoroutine(UpgradeRoutine());
                }
            }

            // Optional: instant-complete (skips scale animation)
            if (debugCompleteKey != KeyCode.None && Input.GetKeyDown(debugCompleteKey))
            {
                if (!IsComplete)
                {
                    if (logDebugHotkeys) Debug.Log("[RasenshurikenFX] DEBUG: Forcing COMPLETE state.");
                    SetActive(_effect3, true);
                    transform.localScale *= Mathf.Max(1.01f, scaleMultiplier);
                    SetActive(_wind0, true);
                    _upgrading  = false;
                    IsComplete  = true;
                    _collision?.UpgradeToComplete();
                }
            }
        }

        // ===== helpers =====
        static Transform FindBySubstring(Transform root, string key)
        {
            if (!root || string.IsNullOrEmpty(key)) return null;
            var k = key.ToLowerInvariant();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains(k)) return t;
            return null;
        }

        static void SetActive(Transform t, bool v) { if (t) t.gameObject.SetActive(v); }

        static PlayerMovement FindNonOwnerPlayerNearby(Vector3 pos, float radius, Transform ownerRoot)
        {
            var cols = Physics.OverlapSphere(pos, radius);
            foreach (var c in cols)
            {
                var pm = c.GetComponentInParent<PlayerMovement>();
                if (!pm) continue;
                if (ownerRoot && pm.transform.root == ownerRoot) continue; // skip owner
                return pm;
            }
            return null;
        }
    }
}
