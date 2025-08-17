using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using BlackMagicAPI.Managers;

namespace RasenganSpell
{
    [BepInPlugin(PluginGuid, "Rasengan Spell", "1.0.0")]
    public class RasenganPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.yourname.rasengan";

        // Exposed so other classes can log & locate files.
        public static ManualLogSource Log { get; private set; }
        public static string PluginDir { get; private set; }

        private RasenganLogic _debugLogic;

        private void Awake()
        {
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location) ?? Directory.GetCurrentDirectory();
            Log.LogInfo($"[Rasengan] PluginDir = {PluginDir}");

            // Register spell (non-generic overload in BlackMagicAPI 2.4.0)
            try
            {
                BlackMagicManager.RegisterSpell(this, typeof(RasenganLogic), typeof(RasenganData));
                Log.LogInfo("[Rasengan] Registered spell with BlackMagic.");
            }
            catch (Exception e)
            {
                Log.LogError($"[Rasengan] RegisterSpell failed: {e.Message}");
            }
        }

        private void Update()
        {
            // F6: ask BlackMagic to spawn a page (host only). If that fails, do the manual cast.
            if (Input.GetKeyDown(KeyCode.F6))
            {
                try
                {
                    BlackMagicManager.SpawnSpell<RasenganLogic>();
                    Log.LogInfo("[Rasengan] Debug page request (F6) sent to BlackMagic.");
                }
                catch (Exception e)
                {
                    Log.LogWarning($"[Rasengan] F6 page spawn failed ({e.Message}); doing manual cast instead.");
                    DoManualCast();
                }
            }

            // F7: pure manual cast (no page involved)
            if (Input.GetKeyDown(KeyCode.F7))
                DoManualCast();
        }

        private void DoManualCast()
        {
            try
            {
                Log.LogInfo("[Rasengan] Manual cast (F7).");

                // Owner = camera if possible (works both in menu & dungeon scenes)
                GameObject ownerGo = Camera.main ? Camera.main.gameObject : null;
                if (ownerGo == null)
                {
                    var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                    foreach (var r in roots) { if (r && r.activeInHierarchy) { ownerGo = r; break; } }
                }
                if (!ownerGo) { Log.LogWarning("[Rasengan] No owner found for manual cast."); return; }

                if (_debugLogic == null)
                {
                    var host = new GameObject("RasenganDebugLogicHost");
                    DontDestroyOnLoad(host);
                    _debugLogic = host.AddComponent<RasenganLogic>();
                }

                var fwd = ownerGo.transform.forward;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;

                var spawnPos = ownerGo.transform.position + fwd * 0.45f + Vector3.up * 0.05f;
                _debugLogic.CastSpell(ownerGo, page: null, spawnPos, fwd, hand: 1);
            }
            catch (Exception e)
            {
                Log.LogError($"[Rasengan] Manual cast failed: {e}");
            }
        }
    }
}
