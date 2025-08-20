#nullable enable
using UnityEngine;
using BlackMagicAPI.Modules.Spells;
using System.IO;

namespace RasenganSpell
{
    public class RasenganData : SpellData
    {
        public override string Name => "Rasengan";
        public override float  Cooldown  => 26f;
        public override Color  GlowColor => new Color(0.2f, 0.6f, 1f, 1f);
        public override string[] SubNames => new[] { "rasengan", "ramen", "rasen", "gan", "gone" };

        // Log which textures BlackMagic will try to load; actual loading is done by the base class.
        public override Texture2D? GetMainTexture()
        {
            var dir  = RasenganPlugin.PluginDir ?? "";
            var path = Path.Combine(dir, "Sprites", Name.Replace(" ", "") + "_Main.png");
            RasenganPlugin.Log?.LogInfo($"[Rasengan] Page Main: {path} exists={File.Exists(path)}");
            return base.GetMainTexture();
        }

        public override Texture2D? GetEmissionTexture()
        {
            var dir  = RasenganPlugin.PluginDir ?? "";
            var path = Path.Combine(dir, "Sprites", Name.Replace(" ", "") + "_Emission.png");
            RasenganPlugin.Log?.LogInfo($"[Rasengan] Page Emission: {path} exists={File.Exists(path)}");
            return base.GetEmissionTexture();
        }
    }
}