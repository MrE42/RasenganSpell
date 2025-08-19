using System.IO;
using UnityEngine;
using BlackMagicAPI.Modules.Spells; // SpellLogic, PageController

namespace RasenganSpell
{
    /// <summary>
    /// Rasengan spell: hide the page mesh, spawn an orb at/just in front of the page,
    /// attach one-shot collision that scales damage by casting level and applies knockback.
    /// </summary>
    public class RasenganLogic : SpellLogic
    {
        public override void CastSpell(GameObject player, PageController page, Vector3 spawnPos, Vector3 dir, int castingLevel)
        {
            RasenganPlugin.Log?.LogInfo("[Rasengan] CastSpell invoked.");

            if (page == null)
            {
                RasenganPlugin.Log?.LogWarning("[Rasengan] CastSpell: page was null.");
                return;
            }

            // Hide the page visuals
            if (page.gameObject.TryGetComponent<Renderer>(out var pageRenderer)) pageRenderer.enabled = false;
            foreach (var childR in page.GetComponentsInChildren<Renderer>(true)) childR.enabled = false;

            // Spawn the orb from the AssetBundle (preferred), else primitive fallback
            GameObject orbVfx = TrySpawnFromBundle("rasengan", "RasenganOrbVFX", page.transform, Vector3.zero);
            if (!orbVfx)
            {
                RasenganPlugin.Log?.LogWarning("[Rasengan] Bundle failed or prefab missing, using primitive sphere as fallback.");
                orbVfx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orbVfx.transform.SetParent(page.transform, false);
                orbVfx.transform.localScale = Vector3.one * 0.2f;

                var primitiveCol = orbVfx.GetComponent<Collider>();
                if (primitiveCol) Object.Destroy(primitiveCol);
            }

            RasenganFXController.AttachAndAutoSetup(orbVfx);

            // Collect colliders so the orb ignores them
            var pageCols  = page.GetComponentsInChildren<Collider>(includeInactive: true);
            var ownerRoot = (player != null) ? player.transform.root : page.transform.root;
            var ownerCols = ownerRoot ? ownerRoot.GetComponentsInChildren<Collider>(includeInactive: true) : new Collider[0];

            RasenganPlugin.Log?.LogDebug($"[Rasengan] Page collider count={pageCols.Length}");

            // Attach collision script
            var collision = orbVfx.GetComponent<RasenganCollision>();
            if (!collision) collision = orbVfx.AddComponent<RasenganCollision>();

            // Tunables
            float sphereRadius     = 0.6f;
            float lifeSeconds      = 6f;
            collision.baseDamage   = 24f;
            collision.damagePerLevel = 6f;
            collision.knockbackForce  = 12f;
            collision.knockbackUpward = 0.25f;

            // IMPORTANT: keep using castingLevel
            collision.castingLevel = castingLevel;

            // Init ignore sets + radius
            collision.Init(ownerRoot, castingLevel, pageCols, sphereRadius);

            // Place the orb just in front of the page
            orbVfx.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            orbVfx.transform.localRotation = Quaternion.identity;

            // Self-despawn after a short time in case it doesn't hit anything
            Object.Destroy(orbVfx, lifeSeconds);

            RasenganPlugin.Log?.LogDebug($"[Rasengan] Orb ready. owner='{ownerRoot?.name ?? "null"}', level={castingLevel}");
        }

        private static GameObject TrySpawnFromBundle(string bundleBaseName, string prefabName, Transform parent, Vector3 localPos)
        {
            try
            {
                var pluginRoot = RasenganPlugin.PluginDir;

                var pAssets = Path.Combine(pluginRoot, "Assets", $"{bundleBaseName}.bundle");
                var pSameDir1 = Path.Combine(pluginRoot, $"{bundleBaseName}.bundle");
                var pSameDir2 = Path.Combine(pluginRoot, $"{bundleBaseName}");
                var pNested   = Path.Combine(pluginRoot, "RasenganSpell", $"{bundleBaseName}.bundle");

                string path = null;
                if (File.Exists(pAssets))        path = pAssets;
                else if (File.Exists(pSameDir1)) path = pSameDir1;
                else if (File.Exists(pSameDir2)) path = pSameDir2;
                else if (File.Exists(pNested))   path = pNested;

                if (path == null)
                {
                    RasenganPlugin.Log?.LogWarning($"[Rasengan] Bundle not found. Tried: {pAssets}, {pSameDir1}, {pSameDir2}, {pNested}");
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
    }
}