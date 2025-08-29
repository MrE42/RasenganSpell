using System;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using UnityEngine;
using BlackMagicAPI.Managers;
using UnityEngine.Rendering;

namespace RasenganSpell
{
    /// <summary>
    /// Entry point for the Rasengan spell mod. Registers the spell and exposes config.
    /// </summary>
    [BepInProcess("MageArena")]
    [BepInDependency("com.d1gq.black.magic.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.magearena.modsync", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(PluginGuid, "RasenganSpell", "1.0.2")]
    public class RasenganPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mre42.rasengan";

        public static ManualLogSource Log { get; private set; }
        public static string PluginDir { get; private set; }

        public static string modsync = "all";
        private static bool debug = false;

        // ==== Config entries ====
        private static ConfigEntry<string> _moveHoldBinding;   // e.g., "Mouse0", "Mouse1", "E", "LeftShift"
        private static ConfigEntry<float>  _volume;            // 0.0 - 1.0 multiplier

        // Parsed binding cache
        private static int     _mouseButtonIndex = -1;         // 0..2 for Mouse0/1/2; -1 if not mouse
        private static KeyCode _holdKeyCode     = KeyCode.None;

        public static float AudioVolumeMultiplier => Mathf.Clamp01(_volume?.Value ?? 0.75f);

        private void Awake()
        {
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location) ?? Directory.GetCurrentDirectory();
            Log.LogInfo($"[Rasengan] PluginDir = {PluginDir}");

            // ---- Bind config (creates BepInEx/config/com.mre42.rasengan.cfg) ----
            _moveHoldBinding = Config.Bind(
                "Controls",
                "MoveHold",
                "Mouse0",
                "Key/button to hold for Rasengan 'extend/move' behavior. Examples: Mouse0, Mouse1, E, LeftShift"
            );

            _volume = Config.Bind(
                "Audio",
                "Volume",
                0.75f,
                "Rasengan audio volume multiplier (0.0 - 1.0)."
            );

            // Parse current settings and watch for changes
            ParseMoveHold(_moveHoldBinding.Value);
            _moveHoldBinding.SettingChanged += (_, __) => ParseMoveHold(_moveHoldBinding.Value);
            _volume.SettingChanged += (_, __) => { /* nothing to parse; clamp on read */ };

            // Register spell
            try
            {
                BlackMagicManager.RegisterSpell(this, typeof(RasenganData), typeof(RasenganLogic));
                BlackMagicManager.RegisterDeathIcon(this, "rasengan", "Rasengan_Death");
                Log.LogInfo("[Rasengan] Registered Rasengan spell with BlackMagic.");
            }
            catch (Exception e)
            {
                Log.LogError($"[Rasengan] RegisterSpell failed: {e}");
            }

            // Render pipeline info (debug)
            var rp = GraphicsSettings.currentRenderPipeline;
            string pipeline = rp == null ? "Built-in (no SRP)" : rp.GetType().FullName;
            Log.LogInfo($"[Rasengan] Render Pipeline: {pipeline}");
            Log.LogInfo($"[Rasengan] Shader.globalRenderPipeline = '{Shader.globalRenderPipeline}'");
        }

        private void Update()
        {
            // F6: debug spawn a Rasengan PAGE at the camera
            if (Input.GetKeyDown(KeyCode.F6) && debug)
            {
                try
                {
                    var cam = Camera.main;
                    Transform anchor = cam ? cam.transform : null;
                    if (anchor == null)
                    {
                        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                        foreach (var r in roots) { if (r && r.activeInHierarchy) { anchor = r.transform; break; } }
                    }

                    Vector3 pos = anchor ? anchor.position + anchor.forward * 0.7f + anchor.up * 0.1f : Vector3.zero;
                    Quaternion rot = anchor ? Quaternion.LookRotation(anchor.forward, Vector3.up) : Quaternion.identity;

                    BlackMagicManager.SpawnSpell<RasenganLogic>(pos, rot);
                    Log.LogInfo("[Rasengan] Debug page request (F6) spawned at camera.");
                }
                catch (Exception e)
                {
                    Log.LogWarning($"[Rasengan] F6 page spawn failed ({e.Message}).");
                }
            }
        }

        // ==== Public helpers used by RasenganLogic ====

        /// <summary>Returns true if the configured "MoveHold" binding is currently held down.</summary>
        public static bool HoldActive()
        {
            // Mouse binding?
            if (_mouseButtonIndex >= 0)
                return Input.GetMouseButton(_mouseButtonIndex);

            // Keyboard binding?
            if (_holdKeyCode != KeyCode.None)
                return Input.GetKey(_holdKeyCode);

            // Fallback: behave like legacy Mouse0
            return Input.GetMouseButton(0);
        }

        // ==== Internal parsing ====
        private static void ParseMoveHold(string raw)
        {
            _mouseButtonIndex = -1;
            _holdKeyCode = KeyCode.None;

            if (string.IsNullOrWhiteSpace(raw))
            {
                Log?.LogWarning("[Rasengan] Controls.MoveHold was empty; defaulting to Mouse0.");
                _mouseButtonIndex = 0;
                return;
            }

            raw = raw.Trim();

            // MouseN pattern?
            if (raw.StartsWith("Mouse", StringComparison.InvariantCultureIgnoreCase))
            {
                var num = raw.Substring("Mouse".Length);
                if (int.TryParse(num, out int idx) && idx >= 0 && idx <= 2)
                {
                    _mouseButtonIndex = idx;
                    Log?.LogInfo($"[Rasengan] MoveHold bound to Mouse{idx}.");
                    return;
                }

                Log?.LogWarning($"[Rasengan] Invalid mouse button '{raw}'. Falling back to Mouse0.");
                _mouseButtonIndex = 0;
                return;
            }

            // Try KeyCode
            if (Enum.TryParse<KeyCode>(raw, ignoreCase: true, out var kc))
            {
                _holdKeyCode = kc;
                Log?.LogInfo($"[Rasengan] MoveHold bound to KeyCode.{kc}.");
                return;
            }

            Log?.LogWarning($"[Rasengan] Could not parse MoveHold='{raw}'. Falling back to Mouse0.");
            _mouseButtonIndex = 0;
        }
    }
}
