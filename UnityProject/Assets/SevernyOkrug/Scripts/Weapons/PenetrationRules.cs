using System;
namespace Severny
{
    /// <summary>Game balance calculation. Physics supplies measured length and angle separately.</summary>
    public static class PenetrationRules
    {
        public static float Cost(float measured, float thickness, float resistance, float incidence, bool hollow)
        {
            if (float.IsNaN(measured) || float.IsNaN(thickness) || float.IsNaN(resistance) || float.IsNaN(incidence) || measured <= 0f)
                return float.PositiveInfinity;
            float wall = Math.Max(0.001f, thickness) / Math.Max(0.05f, Math.Abs(incidence));
            float path = hollow ? Math.Min(measured, wall * 2f) : Math.Max(measured, wall);
            return path * Math.Max(1f, resistance);
        }
    }
}
