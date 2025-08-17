using System.IO;
using UnityEngine;

namespace RasenganSpell
{
    public static class AssetLoader
    {
        static void Log(string msg) => RasenganPlugin.Log?.LogInfo(msg);

        static string ProbePath(params string[] parts)
        {
            var p = Path.Combine(parts);
            return File.Exists(p) ? p : null;
        }

        public static Sprite LoadSprite(string fileName)
        {
            // Try <PluginDir>\RasenganSpell\Sprites\ and <PluginDir>\Sprites\
            var root = RasenganPlugin.PluginDir;
            var p = ProbePath(root, "RasenganSpell", "Sprites", fileName)
                 ?? ProbePath(root, "Sprites", fileName);
            if (p == null) return null;

            var bytes = File.ReadAllBytes(p);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex is null) return null;
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        public static GameObject TrySpawnFromBundle(string bundleBaseName, string prefabName, Transform parent, Vector3 localPos)
        {
            try
            {
                var root = RasenganPlugin.PluginDir;
                var path = ProbePath(root, "RasenganSpell", $"{bundleBaseName}.bundle")
                       ?? ProbePath(root, $"{bundleBaseName}.bundle");
                if (path == null) { RasenganPlugin.Log?.LogWarning($"[Rasengan] Bundle not found: {bundleBaseName}.bundle"); return null; }

                var bundle = AssetBundle.LoadFromFile(path);
                if (!bundle) { RasenganPlugin.Log?.LogWarning($"[Rasengan] Failed to load AssetBundle: {path}"); return null; }

                var prefab = bundle.LoadAsset<GameObject>(prefabName);
                if (!prefab) { RasenganPlugin.Log?.LogWarning($"[Rasengan] Prefab '{prefabName}' not found in bundle."); bundle.Unload(false); return null; }

                var go = Object.Instantiate(prefab);
                if (parent) { go.transform.SetParent(parent, false); go.transform.localPosition = localPos; }
                bundle.Unload(false);
                return go;
            }
            catch (System.Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasengan] Bundle spawn failed: {e.Message}");
                return null;
            }
        }

        // A very forgiving hand finder.
        public static Transform FindBestHand(Transform root)
        {
            if (!root) return null;
            string[] names = { "RightHand", "r_hand", "hand_r", "Hand_R", "Right Hand", "PlayerHand", "WeaponAnchor" };
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.Replace(" ", "").ToLowerInvariant();
                foreach (var want in names)
                {
                    var w = want.Replace(" ", "").ToLowerInvariant();
                    if (n.Contains(w)) return t;
                }
            }
            return root; // fallback = root itself
        }
    }
}
