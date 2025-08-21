using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RasenganSpell
{
    internal static class RasenganSlotHooks
    {
        static bool _installed;

        // What RasenganLogic subscribes to.
        public static event Action AnyActiveSlotChanged;

        public static void TryInstall(Harmony harmony)
        {
            if (_installed || harmony == null) return;
            _installed = true;

            try
            {
                var piType = AccessTools.TypeByName("PlayerInventory");
                if (piType == null)
                {
                    RasenganPlugin.Log?.LogWarning("[Rasengan/Harmony] PlayerInventory type not found.");
                    return;
                }

                // Find PlayerInventory.activateHotbar (case-insensitive). Prefer the parameterless overload if any.
                var candidates = piType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => string.Equals(m.Name, "activateHotbar", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var target = candidates.FirstOrDefault(m => m.GetParameters().Length == 0) ?? candidates.FirstOrDefault();
                if (target == null)
                {
                    RasenganPlugin.Log?.LogWarning("[Rasengan/Harmony] Method 'activateHotbar' not found on PlayerInventory.");
                    return;
                }

                var post = new HarmonyMethod(typeof(RasenganSlotHooks).GetMethod(nameof(ActivateHotbar_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic));

                harmony.Patch(target, postfix: post);
                RasenganPlugin.Log?.LogInfo("[Rasengan/Harmony] Hooked PlayerInventory.activateHotbar");
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasengan/Harmony] Failed to install activateHotbar hook: {e}");
            }
        }

        // Single signal point used by RasenganLogic.
        static void ActivateHotbar_Postfix()
        {
            RasenganPlugin.Log?.LogInfo("[Rasengan/Harmony] AnyActiveSlotChanged raised by: PlayerInventory.activateHotbar");
            AnyActiveSlotChanged?.Invoke();
        }
    }
}
