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
            public float     SpinSpeed = 600f;
            public AxisSpace AxisSpace = AxisSpace.Self;
            public Vector3   Axis      = Vector3.up;
        }

        public List<Layer> layers = new();
        

        private void Update()
        {
            foreach (var L in layers)
            {
                if (L.Transform)
                {
                    var axis = (L.AxisSpace == AxisSpace.Self)
                        ? L.Transform.TransformDirection(L.Axis)
                        : L.Axis;
                    L.Transform.Rotate(axis.normalized, L.SpinSpeed * Time.deltaTime, Space.World);
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

            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var n = r.name.ToLowerInvariant();
                if (t0 == null && (n.Contains("plane 12") || n.Contains("plane12"))) { t0 = r.transform; continue; }
                if (t1 == null && (n.Contains("plane 1")  || n.Contains("plane1")))  { t1 = r.transform; continue; }
                if (t2 == null && (n == "plane" || n.Contains("plane")))             { t2 = r.transform; continue; }
            }

            if (t0) ctrl.layers.Add(new Layer { Transform = t0, SpinSpeed = 1200f, AxisSpace = AxisSpace.World, Axis = new Vector3(1, 1, 1) });
            if (t1) ctrl.layers.Add(new Layer { Transform = t1, SpinSpeed = 600f,  AxisSpace = AxisSpace.Self,  Axis = new Vector3(1, 1, -1) });
            if (t2) ctrl.layers.Add(new Layer { Transform = t2, SpinSpeed = 600f,  AxisSpace = AxisSpace.Self,  Axis = new Vector3(0, 1, 1) });

            return ctrl;
        }
    }
}