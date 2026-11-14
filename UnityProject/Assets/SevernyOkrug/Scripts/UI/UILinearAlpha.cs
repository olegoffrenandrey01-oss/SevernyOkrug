// v11.1: Linear colour space fix for the code-built uGUI. The URP project renders in Linear space, where uGUI blends translucent
// graphics in linear light: dark scrims / gradients / HUD backings came out grey and weak, faint light tints and the film grain
// far too strong (grey specks). This mesh effect re-maps every vertex alpha with UITheme.LinAlpha so the blend looks as designed.
using UnityEngine;
using UnityEngine.UI;

namespace Severny
{
    /// <summary>v11.1, Linear colour space: re-maps the vertex alpha of a Graphic with UITheme.LinAlpha so translucent UI looks
    /// as designed (black scrims stay black, faint tints stay faint). Added to every Image / Text by UIFactory.Img / Label.</summary>
    [DisallowMultipleComponent]
    public sealed class UILinearAlpha : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || !UITheme.Linear) return;
            var g = graphic;
            if (g == null || (!(g is Text) && g.color.a >= 0.999f)) return;   // opaque image: nothing to do
            int n = vh.currentVertCount;
            var v = new UIVertex();
            Color32 last = new Color32(0, 0, 0, 0); byte lastA = 0; bool has = false;
            for (int i = 0; i < n; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var c = v.color;
                if (c.a == 0 || c.a == 255) continue;
                if (!has || c.r != last.r || c.g != last.g || c.b != last.b || c.a != last.a)
                {
                    last = c; has = true;
                    lastA = (byte)Mathf.Clamp(Mathf.RoundToInt(UITheme.LinAlpha(c, c.a / 255f) * 255f), 1, 255);
                }
                v.color.a = lastA;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
