// Editable defaults shared by runtime, the trader and loot. Existing item IDs and base prices are preserved.
using System;
using UnityEngine;
namespace Severny
{
    [Serializable] public class ItemBalanceOverride
    {
        public string itemId;
        [Min(1)] public int price = 1000;
        [Range(1, 5)] public int rarity = 1;
    }
    [CreateAssetMenu(menuName = "Severny/v14 Balance and graphics")]
    public class V14Settings : ScriptableObject
    {
        [Header("Графика всей игры")]
        public bool surfaceDetail = true;
        public bool clearImage = true;
        [Range(0f, 1f)] public float wearOpacity = 0.22f;
        [Range(0, 4000)] public int maxSurfacePatches = 1600;
        [Range(0f, 0.4f)] public float grain = 0.025f;
        [Range(0f, 1f)] public float bloomMultiplier = 0.65f;
        [Header("Скупщик: прежние коэффициенты по умолчанию")]
        [Range(1f, 3f)] public float buyMarkup = 1.15f;
        [Range(0.05f, 0.9f)] public float sellFraction = 0.5f;
        public string[] offers = { "av74", "p9m", "d12", "sv762", "pp9", "mag_av74", "mag_p9m", "mag_d12", "mag_sv762", "mag_pp9", "a545", "a545bp", "a918", "a12", "a762", "a919", "bandage", "medkit", "bigkit", "surgkit", "painkill", "vest3", "vest4", "helm2", "water", "stew", "tools", "tape", "crackers", "juice" };
        [Header("Переопределения цен и редкости (пусто = исходный баланс)")]
        public ItemBalanceOverride[] items = Array.Empty<ItemBalanceOverride>();
        [Header("Полезные предметы")]
        [Range(1f, 30f)] public float armorRepair = 12f;
        [Range(1f, 5f)] public float toolSearchSpeed = 1.4f;
        static V14Settings _current;
        public static V14Settings Current
        {
            get
            {
                if (_current == null) _current = Resources.Load<V14Settings>("V14Settings");
                if (_current == null) { _current = CreateInstance<V14Settings>(); _current.hideFlags = HideFlags.DontSave; }
                return _current;
            }
        }
    }
}
