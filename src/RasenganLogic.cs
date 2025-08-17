using UnityEngine;
using BlackMagicAPI.Modules.Spells; // SpellLogic, PageController

namespace RasenganSpell
{
    /// <summary>
    /// Implements the spell logic so BlackMagicManager.SpawnSpell<RasenganLogic>() works.
    /// </summary>
    public class RasenganLogic : SpellLogic
    {
        /// <summary>
        /// REQUIRED by SpellLogic in your API build.
        /// This is called by the page use AND by SpawnSpell<T>().
        /// </summary>
        public override void CastSpell(GameObject player, PageController page, Vector3 spawnPos, Vector3 dir, int hand)
        {
            RasenganPlugin.Log?.LogInfo("[Rasengan] CastSpell fired.");

            // Choose an owner GameObject and a transform to parent to (hand if available).
            var ownerGo = player ?? (Camera.main ? Camera.main.gameObject : null);
            if (!ownerGo)
            {
                RasenganPlugin.Log?.LogWarning("[Rasengan] CastSpell: owner was null.");
                return;
            }

            var parent   = AssetLoader.FindBestHand(ownerGo.transform);
            var localPos = new Vector3(0.15f, 0.05f, 0.18f);

            // 1) Try to spawn prefab from bundle first.
            GameObject orbVfx = AssetLoader.TrySpawnFromBundle("rasengan", "RasenganOrbVFX", parent, localPos);
            bool usedFallback = false;

            // 2) Fallback: bright emissive sphere if the bundle/prefab isn't present.
            if (orbVfx == null)
            {
                RasenganPlugin.Log?.LogWarning("[Rasengan] VFX prefab not found in bundle; using fallback sphere.");
                orbVfx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = orbVfx.GetComponent<Collider>(); if (col) Destroy(col);
                orbVfx.name = "RasenganOrbVFX_Fallback";

                if (parent) { orbVfx.transform.SetParent(parent, false); orbVfx.transform.localPosition = localPos; }
                else        { orbVfx.transform.position = spawnPos; }

                orbVfx.transform.localScale = Vector3.one * 0.08f;

                var mr = orbVfx.GetComponent<MeshRenderer>();
                if (mr)
                {
                    var mat = new Material(Shader.Find("HDRP/Lit")) { enableInstancing = true };
                    var baseColor = new Color(0.15f, 0.6f, 1f, 1f);
                    mat.SetColor("_BaseColor",     baseColor * 5f);
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissiveColor", baseColor * 6f);
                    mr.sharedMaterial = mat;
                }

                usedFallback = true;
            }

            // 3) Add/auto-setup the FX controller on the spawned object.
            var ctrl = RasenganFXController.AttachAndAutoSetup(orbVfx);
            if (usedFallback && ctrl)
            {
                ctrl.BaseColor          = new Color(0.15f, 0.6f, 1f, 1f);
                ctrl.baseColorIntensity = 3.5f;
                ctrl.emissionMin        = 1.5f;
                ctrl.emissionMax        = 2.5f;
                ctrl.flickerFreq        = 10f;
                ctrl.flickerNoise       = 0.3f;
            }

            // 4) Temporary melee so it "does something" when active.
            var handTf = AssetLoader.FindBestHand(ownerGo.transform);
            var melee = ownerGo.AddComponent<RasenganMelee>();
            melee.Initialize(
                hand: handTf,
                orbVfx: orbVfx,
                damage: 30f,
                knockback: 11f,
                hitRadius: 1.1f,
                reach: 1.7f,
                maxSwings: 6,
                duration: 6f
            );

            melee.OnFinished += () => { if (orbVfx) Destroy(orbVfx); };
        }

        // IMPORTANT: Do NOT keep an OnPageItemUse override in this API build;
        // it doesn’t exist here and will cause “no suitable method for override”.
    }
}
