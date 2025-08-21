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
    }
}