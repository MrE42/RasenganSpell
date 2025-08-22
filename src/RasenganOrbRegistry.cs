using System.Collections.Generic;
using UnityEngine;

namespace RasenganSpell
{
    /// <summary>
    /// Tracks all live Rasengan orbs on this client so we can
    /// destroy only the ones owned by a specific player root.
    /// </summary>
    internal static class RasenganOrbRegistry
    {
        private static readonly HashSet<RasenganCollision> _all = new HashSet<RasenganCollision>();

        public static void Register(RasenganCollision c)
        {
            if (c == null) return;
            _all.Add(c);
        }

        public static void Unregister(RasenganCollision c)
        {
            if (c == null) return;
            _all.Remove(c);
        }

        /// <summary>
        /// Destroys all rasengan orbs whose OwnerRoot == root.
        /// Returns the number destroyed (for logging).
        /// </summary>
        public static int DestroyAllUnder(Transform root, string reason)
        {
            if (root == null) return 0;

            int count = 0;
            // Copy to a temp list because we’re going to modify the set as we destroy.
            var snapshot = new List<RasenganCollision>(_all);
            foreach (var c in snapshot)
            {
                if (c == null) { _all.Remove(c); continue; }
                if (c.OwnerRoot == root)
                {
                    Object.Destroy(c.gameObject);
                    count++;
                }
            }

            RasenganPlugin.Log?.LogInfo($"[Rasengan/OrbRegistry] Destroyed {count} orb(s) under '{root.name}' due to {reason}.");
            return count;
        }
    }
}