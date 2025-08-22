using System;
using HarmonyLib;
using UnityEngine; // for Component

namespace RasenganSpell
{
    internal static class RasenganSlotHooks
    {
        static bool _installed;

        // Now includes WHICH player changed.
        public static event Action<Transform> AnyActiveSlotChanged;

        public static void TryInstall(Harmony harmony)
        {
            if (_installed) return;

            try
            {
                // Patch PlayerInventory.activateHotbar (declared method)
                var playerInvType = AccessTools.TypeByName("PlayerInventory");
                if (playerInvType == null)
                {
                    RasenganPlugin.Log?.LogWarning("[Rasengan/Harmony] Could not find type 'PlayerInventory'.");
                    return;
                }

                var target = AccessTools.Method(playerInvType, "activateHotbar");
                if (target == null)
                {
                    RasenganPlugin.Log?.LogWarning("[Rasengan/Harmony] Could not find PlayerInventory.activateHotbar");
                    return;
                }

                var postfix = new HarmonyMethod(typeof(RasenganSlotHooks), nameof(ActivateHotbar_Postfix));
                harmony.Patch(target, postfix: postfix);

                _installed = true;
                RasenganPlugin.Log?.LogInfo("[Rasengan/Harmony] Hooked PlayerInventory.activateHotbar");
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasengan/Harmony] Failed to install activateHotbar hook: {e}");
            }
        }

        // Strongly typed __instance so Harmony gives us the real component.
        static void ActivateHotbar_Postfix(object __instance)
        {
            try
            {
                var comp = __instance as Component;
                var root = comp ? comp.transform.root : null;
                if (root == null) return;

                RasenganPlugin.Log?.LogInfo("[Rasengan/Harmony] ActiveSlotChanged: " + root.name);
                AnyActiveSlotChanged?.Invoke(root);
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning("[Rasengan/Harmony] ActivateHotbar_Postfix error: " + e);
            }
        }
    }
}
