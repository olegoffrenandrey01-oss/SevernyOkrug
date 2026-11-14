// ScriptableObject with template materials/fonts (created by menu Severny > 1). File name must match class name.
using UnityEngine;

namespace Severny
{
    [CreateAssetMenu(menuName = "Severny/Game Assets", fileName = "SevernyGameAssets")]
    public class SevernyGameAssets : ScriptableObject
    {
        public Material litTemplate;
        public Material unlitTemplate;
        public Material skyboxTemplate;
        public Font uiFont, uiFontBold;
        public Texture2D logo, menuBackground;
        [Tooltip("Set by menu Severny > 3 Place Map: the city scene Raid_Severny contains the exported map.")]
        public bool severnyMapPlaced;
    }
}
