using System;
using System.IO;
using UnityEngine;
using BlackMagicAPI.Modules.Spells;
using Object = UnityEngine.Object;

namespace RasenganSpell
{
    public class RasenganLogic : SpellLogic
    {
        // Reference to the currently spawned orb (set it when you spawn it)
        [SerializeField] private GameObject _activeOrb;
        public override void CastSpell(GameObject player, PageController page, Vector3 spawnPos, Vector3 dir,
            int castingLevel)
        {
            RasenganPlugin.Log?.LogInfo("[Rasengan] CastSpell invoked.");
            if (page == null)
            {
                RasenganPlugin.Log?.LogWarning("[Rasengan] CastSpell: page was null.");
                return;
            }

            // --- Hide the page via a visibility latch (supports overlapping casts) ---
            var latch = PageVisibilityLatch.GetOrAdd(page.gameObject);
            latch.Acquire(); // hides the page (increments lock)

            // Spawn the orb from the AssetBundle (preferred), else primitive fallback
            _activeOrb = TrySpawnFromBundle("rasengan", "RasenganOrbVFX", page.transform, Vector3.zero);
            
            if (!_activeOrb)
            {
                // If we couldn't spawn, release immediately so the page isn't stuck hidden
                latch.Release();
                return;
            }
            _activeOrb = _activeOrb;
            _activeOrb.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            RasenganFXController.AttachAndAutoSetup(_activeOrb);

            // Audio: loop the tail from whatever clip you already set on the orb AudioSource
            var audio = _activeOrb.GetComponent<AudioSource>();
            if (audio)
            {
                audio.playOnAwake = false;
                audio.loop = true; // we’ll schedule a tail loop below
                audio.spatialBlend = 1f;
                audio.rolloffMode = AudioRolloffMode.Linear;
                audio.minDistance = 4f;
                audio.maxDistance = 22f;
                audio.dopplerLevel = 0f;
                audio.volume *= (float) 0.75;

            }

            // Collect colliders so the orb ignores them
            var pageCols = page.GetComponentsInChildren<Collider>(includeInactive: true);
            var ownerRoot = (player != null) ? player.transform.root : page.transform.root;

            RasenganPlugin.Log?.LogDebug($"[Rasengan] Page collider count={pageCols.Length}");

            // Attach collision script
            var collision = _activeOrb.GetComponent<RasenganCollision>();
            if (!collision) collision = _activeOrb.AddComponent<RasenganCollision>();

            // Tunables
            float sphereRadius = 0.6f;
            float lifeSeconds = 10f;
            collision.baseDamage = 24f;
            collision.damagePerLevel = 6f;
            collision.knockbackLevelDistPerLevel = 0.40f;
            collision.knockbackLevelDistMax      = 3.0f;
            collision.castingLevel = castingLevel;

            // Init ignore sets + radius
            collision.Init(ownerRoot, castingLevel, pageCols, sphereRadius, lifeSeconds);

            // Place the orb just in front of the page
            _activeOrb.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            _activeOrb.transform.localRotation = Quaternion.identity;

            // Make it extend while LMB is held
            var extend = _activeOrb.AddComponent<RasenganHoldExtend>();
            extend.Init(
                anchor: page.transform,
                homePos: _activeOrb.transform.localPosition,
                homeRot: _activeOrb.transform.localRotation,
                // tweak these to taste; local -X is "left"
                extendedOffset: new Vector3(.05f, .55f, -0.3f), // left, down, forward
                extendedTiltEuler: new Vector3(-6f, 0f, -10f), // small aggressive tilt
                moveLambda: 10f, // higher = snappier
                rotateLambda: 10f
            );

            // Ensure the page becomes visible again when this orb goes away
            var releaser = _activeOrb.AddComponent<ReleaseOnDestroy>();
            releaser.Init(latch);

            // Auto-despawn fallback
            Object.Destroy(_activeOrb, lifeSeconds);

            RasenganPlugin.Log?.LogDebug(
                $"[Rasengan] Orb ready. owner='{ownerRoot?.name ?? "null"}', level={castingLevel}");
        }

        // ------- existing methods kept as-is (TrySpawnFromBundle, TailLoopFromExistingSource, CopyAudioSourceSettings, AutoStopWhenDestroyed) -------

        // ----- New helper: tracks page visibility with a lock counter -----
        class PageVisibilityLatch : MonoBehaviour
        {
            Renderer[] _renderers;
            int _locks;

            void Awake()
            {
                // Cache only the CURRENT page visuals; orb will be spawned after Acquire()
                _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            }

            public static PageVisibilityLatch GetOrAdd(GameObject pageGo)
            {
                var latch = pageGo.GetComponent<PageVisibilityLatch>();
                if (!latch) latch = pageGo.AddComponent<PageVisibilityLatch>();
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

            void SetVisible(bool visible)
            {
                if (_renderers == null || _renderers.Length == 0)
                    _renderers = GetComponentsInChildren<Renderer>(true);

                foreach (var r in _renderers)
                    if (r)
                        r.enabled = visible;
            }
        }

        // ----- New helper: releases the latch when the orb is destroyed -----
        class ReleaseOnDestroy : MonoBehaviour
        {
            PageVisibilityLatch _latch;

            public void Init(PageVisibilityLatch latch)
            {
                _latch = latch;
            }

            void OnDestroy()
            {
                if (_latch) _latch.Release();
            }

            void OnDisable()
            {
                if (_latch && !gameObject.scene.isLoaded) _latch.Release();
            } // safety for scene unloads
        }


        private static GameObject TrySpawnFromBundle(string bundleBaseName, string prefabName, Transform parent,
            Vector3 localPos)
        {
            try
            {
                var pluginRoot = RasenganPlugin.PluginDir;

                var pAssets = Path.Combine(pluginRoot, "Assets", $"{bundleBaseName}.bundle");
                var pSameDir1 = Path.Combine(pluginRoot, $"{bundleBaseName}.bundle");
                var pSameDir2 = Path.Combine(pluginRoot, $"{bundleBaseName}");
                var pNested = Path.Combine(pluginRoot, "RasenganSpell", $"{bundleBaseName}.bundle");

                string path = null;
                if (File.Exists(pAssets)) path = pAssets;
                else if (File.Exists(pSameDir1)) path = pSameDir1;
                else if (File.Exists(pSameDir2)) path = pSameDir2;
                else if (File.Exists(pNested)) path = pNested;

                if (path == null)
                {
                    RasenganPlugin.Log?.LogWarning(
                        $"[Rasengan] Bundle not found. Tried: {pAssets}, {pSameDir1}, {pSameDir2}, {pNested}");
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

                var go = Object.Instantiate(prefab);
                if (parent)
                {
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = localPos;
                }

                bundle.Unload(false);
                return go;
            }
            catch (System.Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasengan] Bundle spawn failed: {e.Message}");
                return null;
            }
        }

        public sealed class RasenganHoldExtend : MonoBehaviour
        {
            Transform _anchor;
            Vector3 _homePos;
            Quaternion _homeRot;

            Vector3 _extendedOffset;
            Quaternion _extendedRot;

            float _moveLambda = 10f; // exponential damping constants
            float _rotLambda = 10f;

            // cache locals so we don’t allocate
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
            }

            void Update()
            {
                // Only drive locally. If there’s ever a dedicated server with no Input,
                // this simply does nothing (stays at home pose).
                bool hold = Input.GetMouseButton(0);

                _targetPos = hold ? _homePos + _extendedOffset : _homePos;
                _targetRot = hold ? _extendedRot : _homeRot;

                // Exponential smoothing that’s framerate-independent:
                float posT = 1f - Mathf.Exp(-_moveLambda * Time.deltaTime);
                float rotT = 1f - Mathf.Exp(-_rotLambda * Time.deltaTime);

                // Work in local space so it follows the page/hand
                transform.localPosition = Vector3.Lerp(transform.localPosition, _targetPos, posT);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, _targetRot, rotT);

                // Optional: keep parent/anchor if someone reparented us at runtime
                if (_anchor && transform.parent != _anchor)
                    transform.SetParent(_anchor, worldPositionStays: false);
            }
        }
        // at class scope
        private static RasenganLogic s_currentHeldPage;   // whichever Rasengan page is currently enabled/held
        private float _nextPoll;                           // for safety polling

        private void OnEnable()
        {
            s_currentHeldPage = this;
            RasenganPlugin.AnyActiveSlotChanged += OnAnyActiveSlotChanged;
            RasenganPlugin.Log?.LogInfo("[Rasengan] Logic.OnEnable -> marked as current page.");
        }

        private void OnDisable()
        {
            if (s_currentHeldPage == this) s_currentHeldPage = null;
            RasenganPlugin.AnyActiveSlotChanged -= OnAnyActiveSlotChanged;
            RasenganPlugin.Log?.LogInfo("[Rasengan] Logic.OnDisable -> unsubscribed.");
            // If the page is going away, definitely nuke the orb
            KillIfOrbActive("OnDisable");
        }

        private void Update()
        {
            // small watchdog: every 0.25s, if this page isn't the one being held anymore, kill orb
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 0.25f;
                TryKillActiveIfNotHeld("poll");
            }
        }

        private void OnAnyActiveSlotChanged()
        {
            RasenganPlugin.Log?.LogInfo($"[Rasengan] SlotChanged received. thisHeld={(s_currentHeldPage==this)} orb={( _activeOrb != null )} activeInHierarchy={gameObject.activeInHierarchy}");
            TryKillActiveIfNotHeld("event");
        }

        private void TryKillActiveIfNotHeld(string reason)
        {
            if (_activeOrb == null) return;
            // Our page is not the one currently enabled/held -> kill the active orb
            if (s_currentHeldPage != this || !gameObject.activeInHierarchy || !enabled)
            {
                KillIfOrbActive($"not-held:{reason}");
            }
        }

        private void KillIfOrbActive(string why)
        {
            if (_activeOrb == null) return;
            RasenganPlugin.Log?.LogInfo($"[Rasengan] Killing active orb [{why}]");

            Destroy(_activeOrb);
            _activeOrb = null;

            // whatever you already do to unhide the page again:
            // e.g., ShowPageTextures(true); ResetFX(); etc.
        }



    }
}