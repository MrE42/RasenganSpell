using System.IO;
using UnityEngine;
using BlackMagicAPI.Modules.Spells; // SpellLogic, PageController

namespace RasenganSpell
{
    /// <summary>
    /// Rasengan spell behaviour: spawns/animates the orb, positions it in front of the player,
    /// drives the left hand with IK to hold it when available, and enables melee swings.
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

            // Hide the page
            if (page.gameObject.TryGetComponent<Renderer>(out var r)) r.enabled = false;
            foreach (var childR in page.GetComponentsInChildren<Renderer>()) childR.enabled = false;

            // Spawn orb from bundle
            GameObject orbVfx = TrySpawnFromBundle("rasengan", "RasenganOrbVFX", page.transform, Vector3.zero);
            if (!orbVfx)
            {
                RasenganPlugin.Log?.LogWarning("[Rasengan] Bundle failed, fallback sphere.");
                orbVfx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orbVfx.transform.SetParent(page.transform, false);
                orbVfx.transform.localScale = Vector3.one * 0.2f;
            }

            // FX controller
            RasenganFXController.AttachAndAutoSetup(orbVfx);

            // Damage scaling
            int lvl = Mathf.Max(1, castingLevel);
            float dmg = 24f + 6f * (lvl - 1);
            float kb  = 10f + 1.5f * (lvl - 1);
            float dur = 5f  + 0.5f * (lvl - 1);

            // Melee swings anchored to page
            var melee = page.gameObject.AddComponent<RasenganMelee>();
            melee.Initialize(
                hand: page.transform,
                orbVfx: orbVfx,
                damage: dmg,
                knockback: kb,
                hitRadius: 1.1f,
                reach: 1.7f,
                maxSwings: 6,
                duration: dur
            );

            melee.OnFinished += () =>
            {
                if (orbVfx) Object.Destroy(orbVfx);
                // Re-enable page if you want:
                // foreach (var childR in page.GetComponentsInChildren<Renderer>()) childR.enabled = true;
            };
        }

        private static GameObject TrySpawnFromBundle(string bundleBaseName, string prefabName, Transform parent, Vector3 localPos)
{
    try
    {
        // PluginDir should be .../BepInEx/plugins/RasenganSpell
        var pluginRoot = RasenganPlugin.PluginDir;

        // Preferred location: Assets/rasengan.bundle (new layout)
        var pAssets   = Path.Combine(pluginRoot, "Assets", $"{bundleBaseName}.bundle");

        // Legacy fallbacks (if someone has an older install)
        var pSameDir1 = Path.Combine(pluginRoot, $"{bundleBaseName}.bundle");
        var pSameDir2 = Path.Combine(pluginRoot, $"{bundleBaseName}");              // without .bundle
        var pNested   = Path.Combine(pluginRoot, "RasenganSpell", $"{bundleBaseName}.bundle");

        string path = null;
        if (File.Exists(pAssets))      path = pAssets;
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
