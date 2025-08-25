using System.Collections.Generic;
using UnityEngine;

namespace RasenganSpell
{
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

        public static int DestroyAllUnder(Transform root, string reason)
        {
            if (root == null) return 0;

            int count = 0;
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

        // ---- Compatibility helpers so Rasenshuriken can reuse this registry without refactors ----
        public static void RegisterAsRasenganCompat(RasenshurikenCollision rsc)
        {
            // no-op here; Rasenshuriken manages its own lifetime.
            // Intentionally empty to keep identical call sites between Rasengan and Rasenshuriken code.
        }

        public static void UnregisterRasenganCompat(RasenshurikenCollision rsc)
        {
            // no-op by default
        }
    }
}