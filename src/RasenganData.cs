using UnityEngine;
using BlackMagicAPI.Modules.Spells;

namespace RasenganSpell
{
    /// <summary>
    /// MUST inherit SpellData or BlackMagic will reject the registration.
    /// </summary>
    public class RasenganData : SpellData
    {
        [Header("Page Visuals (optional)")]
        public string PageTitle = "Rasengan";
        public string VoiceKeyword = "rasengan";
        public string PageSpriteName = "Rasengan_Main.png";

        [Header("VFX (optional)")]
        public string BundleName = "rasengan";           // file: rasengan.bundle
        public string OrbPrefabName = "RasenganOrbVFX";  // prefab name inside the bundle

        // --- SpellData abstract members (required by BlackMagicAPI 2.4.0) ---
        public override string Name      => PageTitle;
        public override float  Cooldown  => 3f;
        public override Color  GlowColor => new Color(0.2f, 0.6f, 1f, 1f);
    }
}