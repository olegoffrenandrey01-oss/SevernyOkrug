// Original v14 art catalog; separate from the author's generated ModelSpecs.
namespace Severny
{
    public static class V14WeaponArt
    {
        public static string ModelTag(string itemId)
        {
            switch (itemId)
            {
                case "d12": return "D12";
                case "sv762": return "SV762";
                case "pp9": return "PP9";
                default: return null;
            }
        }
        public static readonly ModelSpecs.Mat[] Materials =
        {
            new ModelSpecs.Mat { key = "V14Metal", bc = "T_V14Metal_BC", ms = "T_V14Metal_MS", smooth = 0.42f, metal = true, cull = true },
            new ModelSpecs.Mat { key = "V14Steel", bc = "T_V14Steel_BC", ms = "T_V14Steel_MS", smooth = 0.55f, metal = true, cull = true },
            new ModelSpecs.Mat { key = "V14Wood", bc = "T_V14Wood_BC", ms = "T_V14Wood_MS", smooth = 0.28f, cull = true },
            new ModelSpecs.Mat { key = "V14Polymer", bc = "T_V14Polymer_BC", ms = "T_V14Polymer_MS", smooth = 0.18f, cull = true }
        };
    }
}
