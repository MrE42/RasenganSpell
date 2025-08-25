using System;
using System.IO;
using UnityEngine;
using BlackMagicAPI.Modules.Spells;
using Object = UnityEngine.Object;

namespace RasenganSpell
{
    /// <summary>
    /// Minimal spell logic for Rasenshuriken.
    /// - Spawns the orb (bundle -> prefab), parents to page, applies hold pose.
    /// - Sets up owner + collision.
    /// - Attaches RasenshurikenFXController and lets it drive phases & throw.
    /// - Manages page-visibility latch and cleanup.
    /// </summary>
    public class RasenshurikenLogic : SpellLogic
    {
        [SerializeField] private GameObject activeOrb;
        public static float lifeSeconds = 10f;

        public override void CastSpell(GameObject player, PageController page, Vector3 spawnPos, Vector3 dir, int castingLevel)
        {
            if (!page)
            {
                RasenganPlugin.Log?.LogWarning("[Rasenshuriken] CastSpell: page was null.");
                return;
            }

            // Hide the page while the orb is 'in hand'
            var latch = PageVisibilityLatch.GetOrAdd(page.gameObject);
            latch.Acquire();

            // Try rasenshuriken bundle first; fall back to your Rasengan prefab for easy testing
            activeOrb = TrySpawnFromBundle(
                "rasenshuriken", "Rasenshuriken",
                "rasengan",     "TheRasengan",
                page.transform, Vector3.zero);

            if (!activeOrb)
            {
                latch.Release();
                return;
            }

            // Initial transform
            activeOrb.transform.localScale    = new Vector3(0.5f, 0.5f, 0.5f);
            activeOrb.transform.localPosition = new Vector3(0f, 0f, 0.30f);
            activeOrb.transform.localRotation = Quaternion.identity;

            // Optional audio polish (safe if missing)
            var audio = activeOrb.GetComponent<AudioSource>();
            if (audio)
            {
                audio.playOnAwake = false;
                audio.loop        = true;
                audio.spatialBlend = 1f;
                audio.rolloffMode = AudioRolloffMode.Linear;
                audio.minDistance = 4f;
                audio.maxDistance = 22f;
                audio.dopplerLevel = 0f;
                audio.volume *= 0.75f;
            }

            // Ownership + page refs
            var pageCols  = page.GetComponentsInChildren<Collider>(includeInactive: true);
            var inv       = page ? page.GetComponentInParent<PlayerInventory>() : null;
            var ownerRoot = (player != null) ? player.transform.root
                           : (inv ? inv.transform.root : page.transform.root);

            // Sentinel so if the page disappears we clean up any orbs under this owner
            var hostGO   = page && page.pagerender ? page.pagerender.gameObject : page.gameObject;
            var sentinel = hostGO.GetComponent<PageRasenganSentinel>() ?? hostGO.AddComponent<PageRasenganSentinel>();
            sentinel.Init(ownerRoot);

            // Collision (starts incomplete; FX will call UpgradeToComplete when the phase finishes)
            var collision = activeOrb.GetComponent<RasenshurikenCollision>();
            if (!collision) collision = activeOrb.AddComponent<RasenshurikenCollision>();
            collision.Init(ownerRoot, castingLevel, pageCols, lifeSeconds, complete: false);

            // FX controller owns: incomplete -> effect_3+scale -> wind_0, plus owner right-click throw
            var fx = RasenshurikenFXController.AttachAndAutoSetup(activeOrb);
            fx.Init(
                ownerRoot,
                collision,
                page.transform,
                releasePageVisibility: () => { latch.Release(); }
            );

            // Smoothly keep the orb aligned to the page "hand" like Rasengan does
            var hold = activeOrb.AddComponent<ShurikenHoldExtend>();
            hold.Init(
                anchor: page.transform,
                homePos: activeOrb.transform.localPosition,
                homeRot: activeOrb.transform.localRotation,
                extendedOffset: new Vector3(.05f, .55f, -0.3f),
                extendedTiltEuler: new Vector3(-6f, 0f, -10f),
                moveLambda: 10f,
                rotateLambda: 10f
            );

            // If the orb goes away, re-show the page
            var releaser = activeOrb.AddComponent<ReleaseOnDestroy>();
            releaser.Init(latch);

            // Lifetime
            Object.Destroy(activeOrb, lifeSeconds);

            RasenganPlugin.Log?.LogDebug($"[Rasenshuriken] Spawned. owner='{ownerRoot?.name ?? "null"}', level={castingLevel}");
        }

        // --------- Asset loading (bundle + prefab) ---------
        private static GameObject TrySpawnFromBundle(
            string bundleBaseName, string prefabNamePrimary,
            string fallbackBundleBase, string fallbackPrefab,
            Transform parent, Vector3 localPos)
        {
            GameObject Try(string baseName, string prefab)
            {
                try
                {
                    var pluginRoot = RasenganPlugin.PluginDir;
                    var pAssets = Path.Combine(pluginRoot, "Assets", $"{baseName}.bundle");
                    var pSame1  = Path.Combine(pluginRoot, $"{baseName}.bundle");
                    var pSame2  = Path.Combine(pluginRoot, $"{baseName}");
                    var pNested = Path.Combine(pluginRoot, "RasenganSpell", $"{baseName}.bundle");

                    string path = null;
                    if (File.Exists(pAssets)) path = pAssets;
                    else if (File.Exists(pSame1)) path = pSame1;
                    else if (File.Exists(pSame2)) path = pSame2;
                    else if (File.Exists(pNested)) path = pNested;

                    if (path == null) return null;

                    var bundle = AssetBundle.LoadFromFile(path);
                    if (!bundle) return null;

                    var prefabObj = bundle.LoadAsset<GameObject>(prefab);
                    if (!prefabObj) { bundle.Unload(false); return null; }

                    var go = UnityEngine.Object.Instantiate(prefabObj);
                    if (parent)
                    {
                        go.transform.SetParent(parent, false);
                        go.transform.localPosition = localPos;
                    }
                    bundle.Unload(false);
                    return go;
                }
                catch (Exception e)
                {
                    RasenganPlugin.Log?.LogWarning($"[Rasenshuriken] Bundle spawn failed: {e.Message}");
                    return null;
                }
            }

            return Try(bundleBaseName, prefabNamePrimary) ?? Try(fallbackBundleBase, fallbackPrefab);
        }

        // --------- Helpers (scoped to this logic) ---------

        /// <summary>Simple visibility latch for the page while the orb is active.</summary>
        private sealed class PageVisibilityLatch : MonoBehaviour
        {
            Renderer[] _renderers;
            int _locks;

            void Awake() { _renderers = GetComponentsInChildren<Renderer>(includeInactive: true); }

            public static PageVisibilityLatch GetOrAdd(GameObject go)
            {
                var latch = go.GetComponent<PageVisibilityLatch>();
                if (!latch) latch = go.AddComponent<PageVisibilityLatch>();
                return latch;
            }

            public void Acquire()
            {
                if (_locks == 0) SetVisible(false);
                _locks++;
            }

            public void Release()
            {
                _locks = Mathf.Max(0, _locks - 1);
                if (_locks == 0) SetVisible(true);
            }

            void SetVisible(bool v)
            {
                if (_renderers == null || _renderers.Length == 0)
                    _renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var r in _renderers) if (r) r.enabled = v;
            }
        }

        /// <summary>Release the page latch if the orb is destroyed (timeout or hit).</summary>
        private sealed class ReleaseOnDestroy : MonoBehaviour
        {
            PageVisibilityLatch _latch;
            public void Init(PageVisibilityLatch latch) => _latch = latch;
            void OnDestroy()  { if (_latch) _latch.Release(); }
            void OnDisable()  { if (_latch && !gameObject.scene.isLoaded) _latch.Release(); }
        }

        /// <summary>Ensures all orbs under this owner are cleaned up if the page object is disabled/destroyed.</summary>
        private sealed class PageRasenganSentinel : MonoBehaviour
        {
            Transform _ownerRoot;
            public void Init(Transform ownerRoot) => _ownerRoot = ownerRoot;
            void OnDisable() { Cleanup("page-disabled"); }
            void OnDestroy() { Cleanup("page-destroyed"); }
            void Cleanup(string reason)
            {
                var root = _ownerRoot ? _ownerRoot : transform.root;
                if (root) RasenganOrbRegistry.DestroyAllUnder(root, reason);
            }
        }

        /// <summary>
        /// Keeps the orb snug to the page/hand with a gentle offset and tilt.
        /// Smoothing uses critically-damped style (lambda in s^-1).
        /// </summary>
        private sealed class ShurikenHoldExtend : MonoBehaviour
        {
            Transform _anchor;
            Vector3 _homePos, _extendedOffset;
            Quaternion _homeRot, _extendedTilt;
            float _moveLambda, _rotateLambda;

            public void Init(Transform anchor, Vector3 homePos, Quaternion homeRot,
                             Vector3 extendedOffset, Vector3 extendedTiltEuler,
                             float moveLambda, float rotateLambda)
            {
                _anchor = anchor;
                _homePos = homePos;
                _homeRot = homeRot;
                _extendedOffset = extendedOffset;
                _extendedTilt = Quaternion.Euler(extendedTiltEuler);
                _moveLambda = Mathf.Max(0.01f, moveLambda);
                _rotateLambda = Mathf.Max(0.01f, rotateLambda);
            }

            void LateUpdate()
            {
                if (!_anchor) return;

                // Target local pose (slightly extended/tilted for style)
                var targetPos = _homePos + _extendedOffset;
                var targetRot = _homeRot * _extendedTilt;

                // Smooth towards target in local space
                transform.localPosition = SmoothDampLike(transform.localPosition, targetPos, _moveLambda);
                transform.localRotation = SmoothDampRotationLike(transform.localRotation, targetRot, _rotateLambda);
            }

            static Vector3 SmoothDampLike(Vector3 current, Vector3 target, float lambda)
            {
                // exp(-lambda * dt) smoothing; frame-rate independent
                float t = 1f - Mathf.Exp(-lambda * Time.deltaTime);
                return Vector3.LerpUnclamped(current, target, t);
            }

            static Quaternion SmoothDampRotationLike(Quaternion current, Quaternion target, float lambda)
            {
                float t = 1f - Mathf.Exp(-lambda * Time.deltaTime);
                return Quaternion.SlerpUnclamped(current, target, t);
            }
        }
    }
}
