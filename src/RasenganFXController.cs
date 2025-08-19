using System.Collections.Generic;
using UnityEngine;

namespace RasenganSpell
{
    /// <summary>
    /// Controls the visual appearance of the Rasengan orb.  It spins various child
    /// planes, flickers the emissive intensity, and exposes a simple setup helper.
    /// </summary>
    public class RasenganFXController : MonoBehaviour
    {
        public enum AxisSpace { World, Self }

        [System.Serializable]
        public class Layer
        {
            public Transform Transform;
            public Renderer  Renderer;
            public float     SpinSpeed = 600f;
            public AxisSpace AxisSpace = AxisSpace.Self;
            public Vector3   Axis      = Vector3.up;
        }

        public List<Layer> layers = new();

        [Header("Emission")]
        public Color BaseColor = new(0.15f, 0.6f, 1f, 1f);
        public float baseColorIntensity = 2.0f;
        public float emissionMin = 0.5f;
        public float emissionMax = 1.4f;
        public float flickerFreq = 12f;
        public float flickerNoise = 0.25f;

        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
        }

        private void Update()
        {
            float s  = Mathf.Sin(Time.time * flickerFreq) * 0.5f + 0.5f;
            float em = Mathf.Lerp(emissionMin, emissionMax, s + (Random.value - 0.5f) * flickerNoise);
            Color c  = BaseColor * baseColorIntensity;

            foreach (var L in layers)
            {
                if (L.Transform)
                {
                    var axis = (L.AxisSpace == AxisSpace.Self)
                        ? L.Transform.TransformDirection(L.Axis)
                        : L.Axis;
                    L.Transform.Rotate(axis.normalized, L.SpinSpeed * Time.deltaTime, Space.World);
                }

                if (L.Renderer)
                {
                    L.Renderer.GetPropertyBlock(_mpb);
                    _mpb.SetColor("_BaseColor", c);
                    _mpb.SetFloat("_Emission", em);
                    L.Renderer.SetPropertyBlock(_mpb);
                }
            }
        }

        /// <summary>
        /// Attaches a controller to the given GameObject and attempts to auto-assign the
        /// three plane children named "Plane", "Plane1", and "Plane 12".  Returns the
        /// controller component.
        /// </summary>
        public static RasenganFXController AttachAndAutoSetup(GameObject root)
        {
            if (!root) return null;
            var ctrl = root.GetComponent<RasenganFXController>();
            if (!ctrl) ctrl = root.AddComponent<RasenganFXController>();
            ctrl.layers.Clear();

            Transform t0 = null, t1 = null, t2 = null;
            Renderer  r0 = null, r1 = null, r2 = null;

            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var n = r.name.ToLowerInvariant();
                if (t0 == null && (n.Contains("plane 12") || n.Contains("plane12"))) { t0 = r.transform; r0 = r; continue; }
                if (t1 == null && (n.Contains("plane 1")  || n.Contains("plane1")))  { t1 = r.transform; r1 = r; continue; }
                if (t2 == null && (n == "plane" || n.Contains("plane")))             { t2 = r.transform; r2 = r; continue; }
            }

            if (t0 || r0) ctrl.layers.Add(new Layer { Transform = t0, Renderer = r0, SpinSpeed = 1200f, AxisSpace = AxisSpace.World, Axis = new Vector3(1, 1, 1) });
            if (t1 || r1) ctrl.layers.Add(new Layer { Transform = t1, Renderer = r1, SpinSpeed = 600f,  AxisSpace = AxisSpace.Self,  Axis = new Vector3(1, 1, -1) });
            if (t2 || r2) ctrl.layers.Add(new Layer { Transform = t2, Renderer = r2, SpinSpeed = 600f,  AxisSpace = AxisSpace.Self,  Axis = new Vector3(0, 1, 1) });

            return ctrl;
        }
    }
}