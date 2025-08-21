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
        public override void CastSpell(GameObject player, PageController page, Vector3 spawnPos, Vector3 dir, int castingLevel)
        {
            RasenganPlugin.Log?.LogInfo("[Rasengan] CastSpell invoked.");
            if (page == null)
            {
                RasenganPlugin.Log?.LogWarning("[Rasengan] CastSpell: page was null.");
                return;
            }

            var latch = PageVisibilityLatch.GetOrAdd(page.gameObject);
            latch.Acquire();

            activeOrb = TrySpawnFromBundle("rasengan", "RasenganOrbVFX", page.transform, Vector3.zero);
            if (!activeOrb)
            {
                latch.Release();
                return;
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
                audio.volume *= 0.75f;
            }

            var pageCols  = page.GetComponentsInChildren<Collider>(includeInactive: true);
            var ownerRoot = (player != null) ? player.transform.root : page.transform.root;
            RasenganPlugin.Log?.LogDebug($"[Rasengan] Page collider count={pageCols.Length}");

            var collision = activeOrb.GetComponent<RasenganCollision>();
            if (!collision) collision = activeOrb.AddComponent<RasenganCollision>();

            float sphereRadius = 0.60f;
            float lifeSeconds  = 10f;

            collision.baseDamage                 = 24f;
            collision.damagePerLevel             = 6f;
            collision.knockbackLevelDistPerLevel = 0.40f;
            collision.knockbackLevelDistMax      = 3.00f;
            collision.castingLevel               = castingLevel;
            collision.Init(ownerRoot, castingLevel, pageCols, sphereRadius, lifeSeconds);

            activeOrb.transform.localPosition = new Vector3(0f, 0f, 0.30f);
            activeOrb.transform.localRotation = Quaternion.identity;

            var extend = activeOrb.AddComponent<RasenganHoldExtend>();
            extend.Init(
                anchor: page.transform,
                homePos: activeOrb.transform.localPosition,
                homeRot: activeOrb.transform.localRotation,
                extendedOffset: new Vector3(.05f, .55f, -0.3f), // left, down, forward
                extendedTiltEuler: new Vector3(-6f, 0f, -10f), // small aggressive tilt
                moveLambda: 10f, // higher = snappier
                rotateLambda: 10f
            );

            var releaser = activeOrb.AddComponent<ReleaseOnDestroy>();
            releaser.Init(latch);

            Object.Destroy(activeOrb, lifeSeconds);
            RasenganPlugin.Log?.LogDebug($"[Rasengan] Orb ready. owner='{ownerRoot?.name ?? "null"}', level={castingLevel}");
        }

        private void OnEnable()
        {
            RasenganSlotHooks.AnyActiveSlotChanged += OnAnyActiveSlotChanged;
        }
        
        private void OnDisable()
        {
            RasenganSlotHooks.AnyActiveSlotChanged -= OnAnyActiveSlotChanged;
        }

        // --- Replace this method body (keep signature) ---
        private void OnAnyActiveSlotChanged()
        {
            // Only care if we actually have an active orb
            if (activeOrb == null) return;
            Object.Destroy(activeOrb);
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

        // ===== Extend animation & visibility latch (unchanged) =====
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
                bool hold = Input.GetMouseButton(0);
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

        private sealed class PageVisibilityLatch : MonoBehaviour
        {
            Renderer[] _renderers;
            int _locks;

            void Awake()
            {
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            }

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

                foreach (var r in _renderers)
                    if (r) r.enabled = v;
            }
        }

        private sealed class ReleaseOnDestroy : MonoBehaviour
        {
            PageVisibilityLatch _latch;
            public void Init(PageVisibilityLatch latch) => _latch = latch;
            void OnDestroy() { if (_latch) _latch.Release(); }
            void OnDisable()
            {
                if (_latch && !gameObject.scene.isLoaded) _latch.Release();
            }
        }
    }
}
