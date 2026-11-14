// Item definitions. All names/stats are original to this prototype (no assets/data from other games).
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public enum ItemType { Weapon, Magazine, Ammo, Medical, Armor, Helmet, Valuable, Barter, Food }
    public enum MedKind { None, Bandage, Medkit, Surgery, Painkiller }

    public class ItemDef
    {
        public string id, name, shortName, desc;
        public ItemType type;
        public int w = 1, h = 1, maxStack = 1, price = 1000;
        public float weight = 0.2f;
        public Color color = new Color(0.25f, 0.25f, 0.24f);
        public int rarity = 1; // 1 common .. 5 very rare (loot tables)
        // weapon
        public string caliber, magId;
        public float rpm, recoilV, recoilH, spreadDeg, reloadTime, ergo;
        public bool fullAuto, isPistol;
        // magazine
        public int capacity;
        // ammo
        public float damage, penetration, speed, drag;
        public int pellets = 1;
        public float pelletSpread;
        // medical
        public MedKind med;
        public float hpPool, healPerUse, useTime;
        public bool stopsBleed;
        public int uses;
        // armor
        public int armorClass;
        public float durability;
        // food
        public float energy;

        public bool Stackable => maxStack > 1;
    }

    public static class ItemDB
    {
        static Dictionary<string, ItemDef> _items;
        static List<ItemDef> _list;

        public static IReadOnlyList<ItemDef> All { get { Init(); return _list; } }

        public static ItemDef Get(string id)
        {
            Init();
            if (string.IsNullOrEmpty(id)) return null;
            _items.TryGetValue(id, out var d);
            return d;
        }

        static readonly Color CWeapon = new Color(0.20f, 0.21f, 0.20f);
        static readonly Color CMag = new Color(0.22f, 0.24f, 0.20f);
        static readonly Color CAmmo = new Color(0.30f, 0.28f, 0.16f);
        static readonly Color CMed = new Color(0.36f, 0.16f, 0.15f);
        static readonly Color CArmor = new Color(0.20f, 0.25f, 0.22f);
        static readonly Color CVal = new Color(0.38f, 0.32f, 0.14f);
        static readonly Color CBarter = new Color(0.24f, 0.24f, 0.27f);
        static readonly Color CFood = new Color(0.22f, 0.27f, 0.30f);

        static void Add(ItemDef d) { _items[d.id] = d; _list.Add(d); }

        static void Init()
        {
            if (_items != null) return;
            _items = new Dictionary<string, ItemDef>();
            _list = new List<ItemDef>();
            // --- weapons ---
            Add(new ItemDef { id = "av74", name = "Автомат АВ-74", shortName = "АВ-74", type = ItemType.Weapon, w = 4, h = 2, price = 38000, weight = 3.3f, color = CWeapon, rarity = 3,
                caliber = "5.45x39", magId = "mag_av74", rpm = 640, fullAuto = true, recoilV = 1.15f, recoilH = 0.45f, spreadDeg = 0.06f, reloadTime = 2.6f, ergo = 45,
                desc = "Автомат под 5.45×39. Надёжный, отдача заметная. Режимы: одиночный / автоматический (B)." });
            Add(new ItemDef { id = "p9m", name = "Пистолет П-9М", shortName = "П-9М", type = ItemType.Weapon, w = 2, h = 1, price = 9000, weight = 0.8f, color = CWeapon, rarity = 2,
                caliber = "9x18", magId = "mag_p9m", rpm = 420, fullAuto = false, isPistol = true, recoilV = 1.6f, recoilH = 0.6f, spreadDeg = 0.12f, reloadTime = 1.9f, ergo = 80,
                desc = "Компактный пистолет под 9×18. Слабый, но всегда под рукой." });
            // --- magazines ---
            Add(new ItemDef { id = "mag_av74", name = "Магазин АВ-74, 30 патр.", shortName = "Маг.30", type = ItemType.Magazine, w = 1, h = 2, price = 1800, weight = 0.25f, color = CMag, caliber = "5.45x39", capacity = 30, rarity = 2,
                desc = "Чтобы снарядить — перетащите патроны 5.45×39 на магазин." });
            Add(new ItemDef { id = "mag_p9m", name = "Магазин П-9М, 8 патр.", shortName = "Маг.8", type = ItemType.Magazine, w = 1, h = 1, price = 700, weight = 0.1f, color = CMag, caliber = "9x18", capacity = 8, rarity = 1,
                desc = "Чтобы снарядить — перетащите патроны 9×18 на магазин." });
            // --- ammo ---
            Add(new ItemDef { id = "a545", name = "Патрон 5.45×39 ПС", shortName = "5.45", type = ItemType.Ammo, maxStack = 60, price = 120, weight = 0.011f, color = CAmmo, caliber = "5.45x39",
                damage = 46, penetration = 27, speed = 880, drag = 0.0011f, rarity = 1, desc = "Стандартный патрон со стальным сердечником." });
            Add(new ItemDef { id = "a545bp", name = "Патрон 5.45×39 БП", shortName = "БП", type = ItemType.Ammo, maxStack = 60, price = 420, weight = 0.011f, color = new Color(0.36f, 0.22f, 0.12f), caliber = "5.45x39",
                damage = 41, penetration = 42, speed = 890, drag = 0.0010f, rarity = 4, desc = "Бронебойный. Редкий." });
            Add(new ItemDef { id = "a918", name = "Патрон 9×18 ПСТ", shortName = "9×18", type = ItemType.Ammo, maxStack = 50, price = 60, weight = 0.01f, color = CAmmo, caliber = "9x18",
                damage = 48, penetration = 12, speed = 300, drag = 0.0030f, rarity = 1, desc = "Пистолетный патрон." });
            // --- medical ---
            Add(new ItemDef { id = "bandage", name = "Бинт стерильный", shortName = "Бинт", type = ItemType.Medical, med = MedKind.Bandage, stopsBleed = true, uses = 1, useTime = 2.5f, price = 2500, weight = 0.05f, color = CMed, rarity = 1,
                desc = "Останавливает кровотечение. ПКМ — использовать." });
            Add(new ItemDef { id = "medkit", name = "Аптечка АП-1", shortName = "АП-1", type = ItemType.Medical, med = MedKind.Medkit, hpPool = 100, healPerUse = 50, useTime = 3f, price = 7000, weight = 0.15f, color = CMed, rarity = 2,
                desc = "Восстанавливает до 100 ед. здоровья (по 50 за раз). Кровотечение не останавливает." });
            Add(new ItemDef { id = "bigkit", name = "Аптечка походная", shortName = "Поход.", type = ItemType.Medical, med = MedKind.Medkit, hpPool = 300, healPerUse = 70, stopsBleed = true, useTime = 4f, w = 2, h = 1, price = 19000, weight = 0.6f, color = CMed, rarity = 3,
                desc = "300 ед. здоровья, останавливает кровотечение." });
            Add(new ItemDef { id = "surgkit", name = "Хирургический набор", shortName = "Хир.", type = ItemType.Medical, med = MedKind.Surgery, uses = 2, useTime = 9f, w = 2, h = 1, price = 26000, weight = 0.8f, color = CMed, rarity = 4,
                desc = "Восстанавливает уничтоженную часть тела (до 1 ед.). 2 применения." });
            Add(new ItemDef { id = "painkill", name = "Обезболивающее «Анестол»", shortName = "Обезб.", type = ItemType.Medical, med = MedKind.Painkiller, uses = 4, useTime = 2f, price = 6000, weight = 0.05f, color = CMed, rarity = 2,
                desc = "На 120 с снимает хромоту и боль от переломанных конечностей." });
            // --- armor ---
            Add(new ItemDef { id = "vest3", name = "Бронежилет БЖ-3", shortName = "БЖ-3", type = ItemType.Armor, w = 3, h = 3, armorClass = 3, durability = 45, price = 42000, weight = 8f, color = CArmor, rarity = 3,
                desc = "Класс защиты 3. Защищает грудь и живот." });
            Add(new ItemDef { id = "vest4", name = "Бронежилет БЖ-4М", shortName = "БЖ-4", type = ItemType.Armor, w = 3, h = 3, armorClass = 4, durability = 55, price = 98000, weight = 10.5f, color = CArmor, rarity = 4,
                desc = "Класс защиты 4. Тяжёлый." });
            Add(new ItemDef { id = "helm2", name = "Каска КС-1", shortName = "КС-1", type = ItemType.Helmet, w = 2, h = 2, armorClass = 2, durability = 25, price = 18000, weight = 1.3f, color = CArmor, rarity = 2,
                desc = "Класс защиты 2. Защищает голову." });
            // --- valuables / barter ---
            Add(new ItemDef { id = "watch", name = "Часы наручные «Полюс»", shortName = "Часы", type = ItemType.Valuable, price = 14000, weight = 0.1f, color = CVal, rarity = 3, desc = "Старые механические часы." });
            Add(new ItemDef { id = "chain", name = "Цепочка (позолота)", shortName = "Цепь", type = ItemType.Valuable, price = 26000, weight = 0.1f, color = CVal, rarity = 4, desc = "Ценная вещь. Продать Скупщику." });
            Add(new ItemDef { id = "gpu", name = "Видеоплата ВП-3", shortName = "ВП-3", type = ItemType.Valuable, w = 2, h = 1, price = 140000, weight = 0.9f, color = CVal, rarity = 5, desc = "Очень ценная плата. Встречается редко." });
            Add(new ItemDef { id = "hdd", name = "Жёсткий диск", shortName = "HDD", type = ItemType.Barter, price = 9000, weight = 0.5f, color = CBarter, rarity = 2, desc = "Может хранить что-то важное." });
            Add(new ItemDef { id = "radio", name = "Рация носимая", shortName = "Рация", type = ItemType.Barter, w = 1, h = 2, price = 17000, weight = 0.7f, color = CBarter, rarity = 3, desc = "Рабочая, но без батареи." });
            Add(new ItemDef { id = "docs", name = "Папка с документами", shortName = "Док.", type = ItemType.Valuable, price = 21000, weight = 0.3f, color = CVal, rarity = 3, desc = "Документы из администрации округа." });
            Add(new ItemDef { id = "bolts", name = "Болты", shortName = "Болты", type = ItemType.Barter, price = 3200, weight = 0.4f, color = CBarter, rarity = 1, desc = "Пригодятся в хозяйстве." });
            Add(new ItemDef { id = "wires", name = "Моток проводов", shortName = "Провод", type = ItemType.Barter, price = 4100, weight = 0.4f, color = CBarter, rarity = 1, desc = "Медные провода." });
            Add(new ItemDef { id = "tape", name = "Изолента", shortName = "Изол.", type = ItemType.Barter, price = 2600, weight = 0.1f, color = CBarter, rarity = 1, desc = "Синяя изолента." });
            Add(new ItemDef { id = "tools", name = "Набор инструментов", shortName = "Инстр.", type = ItemType.Barter, w = 2, h = 1, price = 11000, weight = 1.8f, color = CBarter, rarity = 2, desc = "Ключи, отвёртки, плоскогубцы." });
            Add(new ItemDef { id = "canister", name = "Канистра с топливом", shortName = "Канистра", type = ItemType.Barter, w = 2, h = 2, price = 24000, weight = 10f, color = new Color(0.32f, 0.18f, 0.12f), rarity = 3, desc = "ОГНЕОПАСНО. Тяжёлая." });
            Add(new ItemDef { id = "battery", name = "Аккумулятор", shortName = "Аккум.", type = ItemType.Barter, w = 1, h = 2, price = 8000, weight = 1.2f, color = CBarter, rarity = 2, desc = "Аккумулятор для рации или фонаря." });
            Add(new ItemDef { id = "cig", name = "Сигареты «Полёт»", shortName = "Сигар.", type = ItemType.Barter, price = 2100, weight = 0.05f, color = CBarter, rarity = 1, desc = "Пачка сигарет. Местная валюта." });
            Add(new ItemDef { id = "moonshine", name = "Бутыль самогона", shortName = "Самог.", type = ItemType.Barter, w = 1, h = 2, price = 16000, weight = 1f, color = CBarter, rarity = 3, desc = "Крепкий." });
            Add(new ItemDef { id = "stew", name = "Консервы мясные", shortName = "Конс.", type = ItemType.Food, price = 3000, weight = 0.4f, color = CFood, energy = 40, rarity = 1, desc = "Тушёное мясо. Можно продать." });
            Add(new ItemDef { id = "water", name = "Вода питьевая 0.6 л", shortName = "Вода", type = ItemType.Food, w = 1, h = 2, price = 2000, weight = 0.6f, color = CFood, energy = 10, rarity = 1, desc = "Бутылка воды." });

            // --- v08: better loot ---
            Add(new ItemDef { id = "a918bp", name = "Патрон 9×18 ПБМ", shortName = "ПБМ", type = ItemType.Ammo, maxStack = 50, price = 260, weight = 0.01f, color = new Color(0.36f, 0.22f, 0.12f), caliber = "9x18",
                damage = 40, penetration = 24, speed = 520, drag = 0.0024f, rarity = 3, desc = "Бронебойный пистолетный патрон." });
            Add(new ItemDef { id = "vest5", name = "Бронежилет БЖ-5 «Штурм»", shortName = "БЖ-5", type = ItemType.Armor, w = 3, h = 3, armorClass = 5, durability = 70, price = 185000, weight = 12.5f, color = CArmor, rarity = 5,
                desc = "Класс защиты 5. Очень тяжёлый, держит даже бронебойные." });
            Add(new ItemDef { id = "helm4", name = "Шлем ТШ-4", shortName = "ТШ-4", type = ItemType.Helmet, w = 2, h = 2, armorClass = 4, durability = 40, price = 62000, weight = 1.8f, color = CArmor, rarity = 4,
                desc = "Класс защиты 4. Армейский титановый шлем." });
            Add(new ItemDef { id = "irp", name = "ИРП (сухпаёк)", shortName = "ИРП", type = ItemType.Food, w = 1, h = 2, price = 6500, weight = 0.9f, color = CFood, energy = 60, rarity = 2, desc = "Армейский рацион на сутки." });
            Add(new ItemDef { id = "thermos", name = "Термос с чаем", shortName = "Термос", type = ItemType.Food, w = 1, h = 2, price = 5200, weight = 0.8f, color = CFood, energy = 20, rarity = 2, desc = "Горячий сладкий чай. Зимой — на вес золота." });
            Add(new ItemDef { id = "goldring", name = "Золотое кольцо", shortName = "Кольцо", type = ItemType.Valuable, price = 38000, weight = 0.02f, color = CVal, rarity = 4, desc = "Обручальное. Скупщик возьмёт с радостью." });
            Add(new ItemDef { id = "flash", name = "Флешка с данными", shortName = "Флешка", type = ItemType.Valuable, price = 30000, weight = 0.02f, color = CVal, rarity = 4, desc = "Подписана «бухгалтерия». Кому-то очень нужна." });
            Add(new ItemDef { id = "cash", name = "Пачка купюр", shortName = "Деньги", type = ItemType.Valuable, price = 50000, weight = 0.1f, color = new Color(0.25f, 0.34f, 0.2f), rarity = 4, desc = "Перетянуто резинкой. Продаётся по номиналу." });
            Add(new ItemDef { id = "d12", name = "Дробовик Д-12", shortName = "Д-12", type = ItemType.Weapon, w = 4, h = 2, price = 31000, weight = 3.7f, color = CWeapon, rarity = 3,
                caliber = "12x70", magId = "mag_d12", rpm = 150, recoilV = 3.2f, recoilH = 0.9f, spreadDeg = 0.2f, reloadTime = 3.1f, ergo = 35,
                desc = "Полуавтомат, 6 патронов. Картечь эффективна вблизи, слаба против брони. Временные модель, звук и анимация." });
            Add(new ItemDef { id = "sv762", name = "Винтовка СВ-762", shortName = "СВ-762", type = ItemType.Weapon, w = 5, h = 2, price = 78000, weight = 4.4f, color = CWeapon, rarity = 4,
                caliber = "7.62x51", magId = "mag_sv762", rpm = 220, recoilV = 2.4f, recoilH = 0.65f, spreadDeg = 0.025f, reloadTime = 3f, ergo = 30,
                desc = "Самозарядная винтовка. Точная, сильная отдача, дорогие редкие патроны. Временные модель, звук и анимация." });
            Add(new ItemDef { id = "pp9", name = "Пистолет-пулемёт ПП-9", shortName = "ПП-9", type = ItemType.Weapon, w = 3, h = 2, price = 26000, weight = 2.5f, color = CWeapon, rarity = 3,
                caliber = "9x19", magId = "mag_pp9", rpm = 850, fullAuto = true, recoilV = 0.65f, recoilH = 0.35f, spreadDeg = 0.1f, reloadTime = 2.1f, ergo = 65,
                desc = "Ближний бой. Небольшая отдача, высокий расход боеприпасов. Временные модель, звук и анимация." });
            Add(new ItemDef { id = "mag_d12", name = "Магазин Д-12", shortName = "Д12/6", type = ItemType.Magazine, w = 1, h = 2, price = 1600, caliber = "12x70", capacity = 6, rarity = 2, color = CMag });
            Add(new ItemDef { id = "mag_sv762", name = "Магазин СВ-762", shortName = "СВ/10", type = ItemType.Magazine, w = 1, h = 2, price = 2400, caliber = "7.62x51", capacity = 10, rarity = 3, color = CMag });
            Add(new ItemDef { id = "mag_pp9", name = "Магазин ПП-9", shortName = "ПП/25", type = ItemType.Magazine, w = 1, h = 2, price = 1400, caliber = "9x19", capacity = 25, rarity = 2, color = CMag });
            Add(new ItemDef { id = "a12", name = "12/70 картечь", shortName = "12/70", type = ItemType.Ammo, maxStack = 20, price = 180, weight = 0.04f, color = CAmmo, rarity = 2,
                caliber = "12x70", damage = 13, penetration = 5, speed = 380, drag = 0.006f, pellets = 8, pelletSpread = 2.8f, desc = "8 дробин по 13 урона до потерь на скорости и броне." });
            Add(new ItemDef { id = "a762", name = "7.62×51 оболочечный", shortName = "7.62", type = ItemType.Ammo, maxStack = 40, price = 380, weight = 0.024f, color = CAmmo, rarity = 3,
                caliber = "7.62x51", damage = 72, penetration = 35, speed = 820, drag = 0.0012f });
            Add(new ItemDef { id = "a919", name = "9×19 оболочечный", shortName = "9×19", type = ItemType.Ammo, maxStack = 50, price = 85, weight = 0.012f, color = CAmmo, rarity = 1,
                caliber = "9x19", damage = 38, penetration = 16, speed = 390, drag = 0.003f });
            Add(new ItemDef { id = "crackers", name = "Сухари", shortName = "Сухари", type = ItemType.Food, price = 1800, energy = 25, weight = 0.15f, color = CFood, rarity = 1 });
            Add(new ItemDef { id = "juice", name = "Сок", shortName = "Сок", type = ItemType.Food, price = 2400, energy = 30, weight = 0.3f, color = CFood, rarity = 1 });
            Add(new ItemDef { id = "coffee", name = "Банка кофе", shortName = "Кофе", type = ItemType.Barter, price = 8500, weight = 0.2f, color = CFood, rarity = 3 });
            Add(new ItemDef { id = "fuse", name = "Предохранители", shortName = "Предохр.", type = ItemType.Barter, price = 4600, weight = 0.1f, color = CBarter, rarity = 2 });
            Add(new ItemDef { id = "filter", name = "Фильтр промышленный", shortName = "Фильтр", type = ItemType.Barter, price = 12500, weight = 0.6f, color = CBarter, rarity = 3 });
            Add(new ItemDef { id = "bearing", name = "Подшипник", shortName = "Подшип.", type = ItemType.Barter, price = 5800, weight = 0.4f, color = CBarter, rarity = 2 });
            foreach (var d in _list)
                if (d.type == ItemType.Food) d.desc = "В рейде восстанавливает " + d.energy + " выносливости. Голод не используется. ПКМ — употребить.";
            _items["tools"].desc = "Ускоряет обыск хозяйственных контейнеров. ПКМ: ремонт надетой брони, расходуется одна изолента; набор остаётся.";
            foreach (var o in V14Settings.Current.items)
                if (o != null && !string.IsNullOrEmpty(o.itemId) && _items.TryGetValue(o.itemId, out var d))
                { d.price = Mathf.Max(1, o.price); d.rarity = Mathf.Clamp(o.rarity, 1, 5); }
        }

        public static string DefaultAmmo(string caliber, bool armorPiercing = false)
        {
            switch (caliber)
            {
                case "9x18": return armorPiercing ? "a918bp" : "a918";
                case "5.45x39": return armorPiercing ? "a545bp" : "a545";
                case "12x70": return "a12";
                case "7.62x51": return "a762";
                case "9x19": return "a919";
                default: return null;
            }
        }

        // --- loot tables ---
        public enum LootTable { Crate, Bag, Cabinet, Toolbox, Safe, Medbox, Body, WeaponCrate, AmmoBox, Military, Pantry }

        /// <summary>Loot quality of the current raid: 1 = old tables, higher = rare items more often (night raids ~1.7).</summary>
        public static float Luck = 1.25f;

        public static string[] TableItems(LootTable t)
        {
            switch (t)
            {
                case LootTable.Pantry: return new[] { "stew", "water", "irp", "thermos", "crackers", "juice", "coffee" };
                case LootTable.Crate: return new[] { "a545", "a545", "a545", "a918", "a918", "a918bp", "a545bp", "mag_av74", "mag_av74", "mag_p9m", "bolts", "wires", "tape", "tools", "canister", "battery", "stew", "water", "irp", "cig", "av74", "p9m", "d12", "pp9", "sv762", "mag_d12", "mag_pp9", "mag_sv762", "a12", "a919", "a762", "vest3", "helm2" };
                case LootTable.Bag: return new[] { "cig", "stew", "water", "crackers", "juice", "irp", "thermos", "bandage", "bandage", "medkit", "painkill", "watch", "chain", "goldring", "a918", "mag_p9m", "tape", "moonshine", "docs", "flash", "cash" };
                case LootTable.Cabinet: return new[] { "docs", "docs", "hdd", "flash", "radio", "battery", "wires", "tape", "cig", "watch", "chain", "goldring", "gpu", "cash" };
                case LootTable.Toolbox: return new[] { "bolts", "wires", "tape", "tools", "battery", "bolts", "wires", "hdd", "canister", "fuse", "filter", "bearing" };
                case LootTable.Safe: return new[] { "chain", "goldring", "watch", "docs", "gpu", "cash", "cash", "flash", "a545bp" };
                case LootTable.Medbox: return new[] { "bandage", "bandage", "medkit", "medkit", "bigkit", "painkill", "surgkit" };
                case LootTable.WeaponCrate: return new[] { "av74", "av74", "p9m", "d12", "pp9", "sv762" };
                case LootTable.AmmoBox: return new[] { "a545", "a545", "a545", "a545bp", "a918", "a918", "a918bp", "a12", "a919", "a762" };
                case LootTable.Military: return new[] { "vest3", "vest4", "vest5", "helm2", "helm4", "mag_av74", "mag_av74", "a545bp", "a545", "bigkit", "surgkit", "radio", "irp", "av74" };
                default: return new[] { "a545", "a918", "bandage", "medkit", "painkill", "cig", "stew", "water", "watch", "chain", "goldring", "cash", "docs" };
            }
        }

        /// <summary>Rarity-weighted pick from a table.</summary>
        public static ItemDef PickFrom(LootTable t, System.Random rng)
        {
            var ids = TableItems(t);
            float total = 0f;
            foreach (var id in ids) total += Weight(Get(id));
            float r = (float)rng.NextDouble() * total;
            foreach (var id in ids)
            {
                r -= Weight(Get(id));
                if (r <= 0f) return Get(id);
            }
            return Get(ids[ids.Length - 1]);
        }

        static float Weight(ItemDef d) => d == null ? 0f : 1f / Mathf.Pow(d.rarity, 2f / Mathf.Max(0.5f, Luck));
    }
}
