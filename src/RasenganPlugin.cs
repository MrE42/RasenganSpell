using System;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using BlackMagicAPI.Managers;
using UnityEngine.Rendering;
using HarmonyLib;

namespace RasenganSpell
{
    /// <summary>
    /// Entry point for the Rasengan spell mod.  This component registers the spell with
    /// BlackMagic on startup and exposes debug hotkeys for spawning a page or casting
    /// the spell manually.  It also exposes a ManualLogSource and the plugin's install
    /// directory to the rest of the mod.
    /// </summary>
    [BepInProcess("MageArena")]
    [BepInDependency("com.d1gq.black.magic.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.magearena.modsync", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(PluginGuid, "RasenganSpell", "1.0.0")]
    public class RasenganPlugin : BaseUnityPlugin
    {
        /// <summary>Unique identifier for this plugin.</summary>
        public const string PluginGuid = "com.mre42.rasengan";

        /// <summary>Logger instance used throughout the mod.</summary>
        public static ManualLogSource Log { get; private set; }

        /// <summary>Absolute path to the folder containing this plugin DLL.</summary>
        public static string PluginDir { get; private set; }

        public static string modsync = "all";

        private static bool debug = true;
        
        // 2.a) Global event everyone can listen to
        public static event Action AnyActiveSlotChanged;
        
        internal static Harmony harmony;

        public static void RaiseAnyActiveSlotChanged([CallerMemberName] string src = null)
        {
            Log?.LogInfo($"[Rasengan/Harmony] AnyActiveSlotChanged raised by: {src}");
            AnyActiveSlotChanged?.Invoke();
        }


        private void Awake()
        {
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location) ?? Directory.GetCurrentDirectory();
            Log.LogInfo($"[Rasengan] PluginDir = {PluginDir}");
            
            harmony = new Harmony(PluginGuid);
            RasenganSlotHooks.TryInstall(harmony);


            // Register: (plugin, logicType, dataType)
            try
            {
                BlackMagicManager.RegisterSpell(this, typeof(RasenganData), typeof(RasenganLogic));
                BlackMagicManager.RegisterDeathIcon(
                    this,
                    "rasengan",
                    "Rasengan_Death"
                );
                Log.LogInfo("[Rasengan] Registered Rasengan spell with BlackMagic.");
            }
            catch (Exception e)
            {
                Log.LogError($"[Rasengan] RegisterSpell failed: {e}");
            }
            
            var rp = GraphicsSettings.currentRenderPipeline;
            string pipeline =
                rp == null ? "Built-in (no SRP)" :
                    rp.GetType().FullName; // e.g. "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset" or "UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset"

            Log.LogInfo($"[Rasengan] Render Pipeline: {pipeline}");

            // Extra signal:
            Log.LogInfo($"[Rasengan] Shader.globalRenderPipeline = '{Shader.globalRenderPipeline}'");
        }

        private void Update()
        {   
            // F6: ask BlackMagic to spawn a Rasengan PAGE at the player's camera
            if (Input.GetKeyDown(KeyCode.F6) && debug)
            {
                try
                {
                    // Choose an anchor (main camera preferred)
                    var cam = Camera.main;
                    Transform anchor = cam ? cam.transform : null;

                    if (anchor == null)
                    {
                        // Fallback: any active root as a last resort
                        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                        foreach (var r in roots) { if (r && r.activeInHierarchy) { anchor = r.transform; break; } }
                    }

                    // Position the page a bit in front of the player to avoid near-clip issues
                    Vector3 pos = anchor ? anchor.position + anchor.forward * 0.7f + anchor.up * 0.1f : Vector3.zero;
                    Quaternion rot = anchor ? Quaternion.LookRotation(anchor.forward, Vector3.up) : Quaternion.identity;

                    // Spawn the Rasengan PAGE (uses RasenganLogic as the page definition)
                    BlackMagicManager.SpawnSpell<RasenganLogic>(pos, rot);
                    Log.LogInfo("[Rasengan] Debug page request (F6) spawned at camera.");
                }
                catch (Exception e)
                {
                    Log.LogWarning($"[Rasengan] F6 page spawn failed ({e.Message}).");
                    
                }
            }


        }

    }
}