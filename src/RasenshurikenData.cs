#nullable enable
using UnityEngine;
using BlackMagicAPI.Modules.Spells;

namespace RasenganSpell
{
    public class RasenshurikenData : SpellData
    {
        public override string   Name      => "Rasenshuriken";
        public override float    Cooldown  => 65f;
        public override Color    GlowColor => new Color(0.85f, 0.95f, 1f, 1f);
        public override string[] SubNames  => new[] { "rasenshuriken", "rasen", "rasen shuriken", "shuriken", "wind style", "wind", "style", "um", "hmm", "yeah", "why", "python", "bug", "what" };
    }
}