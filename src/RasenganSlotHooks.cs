using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RasenganSpell
{
    internal static class RasenganSlotHooks
    {
        static bool _installed;

        // Keep your local signal for page-local logic.
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

                // 1) Hotbar toggle (local player convenience)
                var hotbar = piType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => string.Equals(m.Name, "activateHotbar", StringComparison.OrdinalIgnoreCase));
                if (hotbar != null)
                {
                    harmony.Patch(hotbar,
                        postfix: new HarmonyMethod(typeof(RasenganSlotHooks)
                            .GetMethod(nameof(ActivateHotbar_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    RasenganPlugin.Log?.LogInfo("[Rasengan/Harmony] Hooked PlayerInventory.activateHotbar");
                }

                // 2) Server accepts a new object in hand
                var setServer = AccessTools.Method(piType, "SetObjectInHandServer");
                if (setServer != null)
                {
                    harmony.Patch(setServer,
                        postfix: new HarmonyMethod(typeof(RasenganSlotHooks)
                            .GetMethod(nameof(SetObjectInHandServer_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    RasenganPlugin.Log?.LogInfo("[Rasengan/Harmony] Hooked PlayerInventory.SetObjectInHandServer");
                }

                // 3) Server replicated the change to observers (this is called on *every* client)
                var setObserver = AccessTools.Method(piType, "SetObjectInHandObserver");
                if (setObserver != null)
                {
                    harmony.Patch(setObserver,
                        postfix: new HarmonyMethod(typeof(RasenganSlotHooks)
                            .GetMethod(nameof(SetObjectInHandObserver_Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    RasenganPlugin.Log?.LogInfo("[Rasengan/Harmony] Hooked PlayerInventory.SetObjectInHandObserver");
                }
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasengan/Harmony] Failed to install hooks: {e}");
            }
        }

        // === Postfixes ===

        // (A) Local convenience: keep your current page logic reacting to local slot changes.
        static void ActivateHotbar_Postfix()
        {
            RasenganPlugin.Log?.LogInfo("[Rasengan/Harmony] AnyActiveSlotChanged raised by: PlayerInventory.activateHotbar");
            AnyActiveSlotChanged?.Invoke();
        }

        // (B) Runs on the SERVER when a player actually equips/switches.
        static void SetObjectInHandServer_Postfix(object __instance, GameObject obj)
        {
            TryCleanupFor(__instance, reason: "equip-server");
        }

        // (C) Runs on EVERY CLIENT observing that player (including host) when the server replicates the equip.
        static void SetObjectInHandObserver_Postfix(object __instance, GameObject obj)
        {
            TryCleanupFor(__instance, reason: "equip-observer");
        }

        // Shared clean-up: find the player root owning this inventory and nuke rasengan orbs under it.
        static void TryCleanupFor(object __instance, string reason)
        {
            try
            {
                var c = __instance as Component;
                if (c == null) return;
                var root = c.transform.root;
                if (root == null) return;

                RasenganPlugin.Log?.LogInfo($"[Rasengan/NetCleanup] {reason}: destroying rasengan(s) under '{root.name}'.");
                RasenganOrbRegistry.DestroyAllUnder(root, reason);
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasengan/NetCleanup] {reason} failed: {e}");
            }
        }
    }
}
