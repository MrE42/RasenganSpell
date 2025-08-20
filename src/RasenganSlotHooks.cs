using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RasenganSpell
{
    internal static class RasenganSlotHooks
    {
        static bool _installed;

        static readonly string[] CandidateMethodNames =
        {
            "SetActiveSlot", "SwitchSlot", "SwitchActiveSlot", "EquipSlot", "SelectSlot",
            "SelectHotbarSlot", "TrySwitchSlot", "TryEquip", "Equip",
            "OnSelectedItemChanged", "SetCurrentItem"
        };

        public static void TryInstall(Harmony harmony)
        {
            if (_installed || harmony == null) return;
            _installed = true;

            int patched = 0;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null)
                {
                    RasenganPlugin.Log?.LogWarning("[Rasengan/Harmony] Assembly-CSharp not found.");
                    return;
                }

                var post = new HarmonyMethod(typeof(RasenganSlotHooks)
                    .GetMethod(nameof(PostfixNotify), BindingFlags.Static | BindingFlags.NonPublic));

                foreach (var t in asm.GetTypes())
                {
                    // 2a) Hotbar-like methods
                    foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                   BindingFlags.NonPublic))
                    {
                        if (m.ReturnType != typeof(void)) continue;
                        if (!CandidateMethodNames.Contains(m.Name)) continue;

                        harmony.Patch(m, postfix: post);
                        RasenganPlugin.Log?.LogInfo($"[Rasengan/Harmony] Hooked {t.FullName}.{m.Name}()");
                        patched++;
                    }

                    // 2b) Also listen to OnDisable on anything (pages, items, slots, etc.)
                    var onDisable = t.GetMethod("OnDisable",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (onDisable != null)
                    {
                        harmony.Patch(onDisable, postfix: post);
                        RasenganPlugin.Log?.LogInfo($"[Rasengan/Harmony] Hooked {t.FullName}.OnDisable()");
                        patched++;
                    }
                }

                RasenganPlugin.Log?.LogInfo($"[Rasengan/Harmony] Installed {patched} hook(s).");
            }
            catch (Exception e)
            {
                RasenganPlugin.Log?.LogWarning($"[Rasengan/Harmony] Failed installing hooks: {e}");
            }
        }

        // Harmony passes __originalMethod so we can log exactly what fired.
        static void PostfixNotify(MethodBase __originalMethod)
        {
            RasenganPlugin.Log?.LogInfo(
                $"[Rasengan/Harmony] Postfix from {__originalMethod?.DeclaringType?.FullName}.{__originalMethod?.Name}");
            RasenganPlugin.RaiseAnyActiveSlotChanged("HarmonyPostfix");
        }
    }
}
