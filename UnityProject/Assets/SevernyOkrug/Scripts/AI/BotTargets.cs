// Everything the bots can see, hear and shoot at: the local player and (LAN co-op) the avatars of the other players.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public interface IBotTarget
    {
        /// <summary>Root of the target hierarchy (line-of-sight hits on any child count as "seen").</summary>
        Transform Root { get; }
        GameObject Go { get; }
        Vector3 Head { get; }
        float Height { get; }
        bool Crouched { get; }
        float SpeedNow { get; }
        bool IsSprinting { get; }
        /// <summary>Flashlight on: at night the target is visible from far away.</summary>
        bool TorchOn { get; }
        bool Dead { get; }
    }

    public static class BotTargets
    {
        public static readonly List<IBotTarget> All = new List<IBotTarget>();

        public static void Add(IBotTarget t) { if (t != null && !All.Contains(t)) All.Add(t); }
        /// <summary>Null or a destroyed Unity object.</summary>
        public static bool Gone(IBotTarget t) => t == null || (t is Object o && o == null);
        public static void Remove(IBotTarget t) { All.Remove(t); }

        public static IBotTarget Of(GameObject g)
        {
            if (g == null) return null;
            for (int i = 0; i < All.Count; i++) { var t = All[i]; if (!Gone(t) && t.Go == g) return t; }
            return null;
        }

        /// <summary>Nearest living target (the current one is preferred: stickiness 0.6).</summary>
        public static IBotTarget Nearest(Vector3 from, IBotTarget current, float maxDist = float.MaxValue)
        {
            IBotTarget best = null; float bestScore = float.MaxValue;
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var t = All[i];
                if (Gone(t)) { All.RemoveAt(i); continue; }
                if (t.Dead) continue;
                float d = Vector3.Distance(from, t.Root.position);
                if (d > maxDist) continue;
                float score = t == current ? d * 0.6f : d;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }
    }
}
