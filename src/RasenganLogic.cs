using System;
using System.IO;
using UnityEngine;
using BlackMagicAPI.Modules.Spells;
using Object = UnityEngine.Object;

namespace RasenganSpell
{
    public class RasenganLogic : SpellLogic
    {
        [SerializeField] private GameObject activeOrb;
        public static float lifeSeconds = 10.0f;

        public override bool CastSpell(PlayerMovement player, PageController page, Vector3 spawnPos, Vector3 dir, int castingLevel)
        {
            RasenganPlugin.Log?.LogInfo("[Rasengan] CastSpell invoked.");
            if (page == null)
            {
                RasenganPlugin.Log?.LogWarning("[Rasengan] CastSpell: page was null.");
                return false;
            }

            var latch = PageVisibilityLatch.GetOrAdd(page.gameObject);
            latch.Acquire();

            activeOrb = TrySpawnFromBundle("rasengan", "TheRasengan", page.transform, Vector3.zero);
            if (!activeOrb)
            {
                latch.Release();
                return false;
            }

            activeOrb.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            RasenganFXController.AttachAndAutoSetup(activeOrb);

            var audio = activeOrb.GetComponent<AudioSource>();
            if (audio)
            {
                audio.playOnAwake = false;
                audio.loop = true;
                audio.spatialBlend = 1f;
                audio.rolloffMode = AudioRolloffMode.Linear;
                audio.minDistance = 4f;
                audio.maxDistance = 22f;
                audio.dopplerLevel = 0f;

                // Use configurable volume multiplier (clamped 0..1)
                audio.volume = Mathf.Clamp01(audio.volume * RasenganPlugin.AudioVolumeMultiplier);
            }

            var pageCols  = page.GetComponentsInChildren<Collider>(includeInactive: true);
            var inv = page ? page.GetComponentInParent<PlayerInventory>() : null;
            var ownerRoot = (player != null)
                ? player.transform.root
                : (inv ? inv.transform.root : page.transform.root);

            var hostGO = page && page.pagerender ? page.pagerender.gameObject : page.gameObject;
            var sentinel = hostGO.GetComponent<PageRasenganSentinel>() ?? hostGO.AddComponent<PageRasenganSentinel>();
            sentinel.Init(ownerRoot);

            RasenganPlugin.Log?.LogDebug($"[Rasengan] Page collider count={pageCols.Length}");

            var collision = activeOrb.GetComponent<RasenganCollision>();
            if (!collision) collision = activeOrb.AddComponent<RasenganCollision>();
            collision.Init(ownerRoot, castingLevel, pageCols, lifeSeconds);

            activeOrb.transform.localPosition = new Vector3(0f, 0f, 0.30f);
            activeOrb.transform.localRotation = Quaternion.identity;

            var extend = activeOrb.AddComponent<RasenganHoldExtend>();
            extend.Init(
                anchor: page.transform,
                homePos: activeOrb.transform.localPosition,
                homeRot: activeOrb.transform.localRotation,
                extendedOffset: new Vector3(.05f, .55f, -0.3f),
                extendedTiltEuler: new Vector3(-6f, 0f, -10f),
                moveLambda: 10f,
                rotateLambda: 10f
            );

            var releaser = activeOrb.AddComponent<ReleaseOnDestroy>();
            releaser.Init(latch);

            Object.Destroy(activeOrb, lifeSeconds);
            RasenganPlugin.Log?.LogDebug($"[Rasengan] Orb ready. owner='{ownerRoot?.name ?? "null"}', level={castingLevel}");
            return true;
        }

        // ===== AssetBundle spawn helper (unchanged) =====
        private static GameObject TrySpawnFromBundle(string bundleBaseName, string prefabName, Transform parent, Vector3 localPos)
        {
            try
            {
                var pluginRoot = RasenganPlugin.PluginDir;

                var pAssets = Path.Combine(pluginRoot, "Assets", $"{bundleBaseName}.bundle");
                var pSame1  = Path.Combine(pluginRoot, $"{bundleBaseName}.bundle");
                var pSame2  = Path.Combine(pluginRoot, $"{bundleBaseName}");
                var pNested = Path.Combine(pluginRoot, "RasenganSpell", $"{bundleBaseName}.bundle");

                string path = null;
                if (File.Exists(pAssets)) path = pAssets;
                else if (File.Exists(pSame1)) path = pSame1;
                else if (File.Exists(pSame2)) path = pSame2;
                else if (File.Exists(pNested)) path = pNested;

                if (path == null)
                {
                    RasenganPlugin.Log?.LogWarning($"[Rasengan] Bundle not found. Tried: {pAssets}, {pSame1}, {pSame2}, {pNested}");
                    return null;
                }

                var bundle = AssetBundle.LoadFromFile(path);
                if (!bundle)
                {
                    RasenganPlugin.Log?.LogWarning($"[Rasengan] Failed to load AssetBundle: {path}");
                    return null;
                }

                var prefab = bundle.LoadAsset<GameObject>(prefabName);
                if (!prefab)
                {
                    RasenganPlugin.Log?.LogWarning($"[Rasengan] Prefab '{prefabName}' not found in bundle: {path}");
                    bundle.Unload(false);
                    return null;
                }

                var go = UnityEngine.Object.Instantiate(prefab);
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
                RasenganPlugin.Log?.LogWarning($"[Rasengan] Bundle spawn failed: {e.Message}");
                return null;
            }
        }

        // ===== Extend animation & visibility latch (updated input) =====
        public sealed class RasenganHoldExtend : MonoBehaviour
        {
            Transform _anchor;
            Vector3 _homePos;
            Quaternion _homeRot;
            Vector3 _extendedOffset;
            Quaternion _extendedRot;
            float _moveLambda = 10f;
            float _rotLambda = 10f;
            Vector3 _targetPos;
            Quaternion _targetRot;

            public void Init(
                Transform anchor,
                Vector3 homePos,
                Quaternion homeRot,
                Vector3 extendedOffset,
                Vector3 extendedTiltEuler,
                float moveLambda = 10f,
                float rotateLambda = 10f)
            {
                _anchor = anchor != null ? anchor : transform.parent;
                _homePos = homePos;
                _homeRot = homeRot;
                _extendedOffset = extendedOffset;
                _extendedRot = homeRot * Quaternion.Euler(extendedTiltEuler);
                _moveLambda = Mathf.Max(1f, moveLambda);
                _rotLambda = Mathf.Max(1f, rotateLambda);
                _targetPos = _homePos;
                _targetRot = _homeRot;
                if (_anchor && transform.parent != _anchor)
                    transform.SetParent(_anchor, false);
            }

            void Update()
            {
                // Use configurable binding (mouse or keyboard) instead of hardcoded Mouse0
                bool hold = RasenganPlugin.HoldActive();

                _targetPos = hold ? _homePos + _extendedOffset : _homePos;
                _targetRot = hold ? _extendedRot : _homeRot;

                float posT = 1f - Mathf.Exp(-_moveLambda * Time.deltaTime);
                float rotT = 1f - Mathf.Exp(-_rotLambda * Time.deltaTime);

                transform.localPosition = Vector3.Lerp(transform.localPosition, _targetPos, posT);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, _targetRot, rotT);

                if (_anchor && transform.parent != _anchor)
                    transform.SetParent(_anchor, false);
            }
        }

        // (rest unchanged: PageVisibilityLatch, ReleaseOnDestroy, PageRasenganSentinel)
        // ...
        private sealed class PageVisibilityLatch : MonoBehaviour
        {
            Renderer[] _renderers;
            int _locks;
            void Awake() { _renderers = GetComponentsInChildren<Renderer>(includeInactive: true); }
            public static PageVisibilityLatch GetOrAdd(GameObject go) { var l = go.GetComponent<PageVisibilityLatch>(); if (!l) l = go.AddComponent<PageVisibilityLatch>(); return l; }
            public void Acquire() { if (_locks == 0) SetVisible(false); _locks++; }
            public void Release() { _locks = Mathf.Max(0, _locks - 1); if (_locks == 0) SetVisible(true); }
            void SetVisible(bool v) { if (_renderers == null || _renderers.Length == 0) _renderers = GetComponentsInChildren<Renderer>(true); foreach (var r in _renderers) if (r) r.enabled = v; }
        }

        private sealed class ReleaseOnDestroy : MonoBehaviour
        {
            PageVisibilityLatch _latch;
            public void Init(PageVisibilityLatch latch) => _latch = latch;
            void OnDestroy() { if (_latch) _latch.Release(); }
            void OnDisable() { if (_latch && !gameObject.scene.isLoaded) _latch.Release(); }
        }

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
    }
}
