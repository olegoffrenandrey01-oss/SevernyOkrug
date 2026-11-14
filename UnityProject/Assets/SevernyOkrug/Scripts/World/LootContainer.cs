// Searchable container (crate, bag, cabinet, safe, corpse). Items are revealed one by one while searching.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public class LootContainer : Interactable
    {
        public string title = "Ящик";
        public ItemDB.LootTable table = ItemDB.LootTable.Crate;
        public InventoryGrid grid;
        public int minItems = 1, maxItems = 4;
        public bool isCorpse;
        public float searchTimePerItem = 0.55f;
        public int seed;
        /// <summary>Contents were set by code (tutorial / LAN snapshot): no random fill, items are still searched.</summary>
        public bool prefilled;
        /// <summary>The local player has opened it at least once.</summary>
        public bool opened;
        /// <summary>LAN: network id (0 = not networked).</summary>
        public int nid;
        public bool Generated => _generated;

        bool _generated;
        readonly HashSet<ItemInstance> _hidden = new HashSet<ItemInstance>();
        float _searchTimer, _nextSearchSnd;

        public override string Prompt => isCorpse ? "Обыскать: " + title : "Открыть: " + title;

        public override void Interact(PlayerController p)
        {
            EnsureGenerated();
            opened = true;
            SFX.PlayAt(isCorpse ? SFX.Rustle : SFX.LootOpen, transform.position, 0.75f, 2f, 25f);
            NoiseSystem.Emit(transform.position, 7f, p.gameObject);
            UIManager.I?.OpenInventory(this);
        }

        public void EnsureGenerated()
        {
            if (_generated) return;
            _generated = true;
            if (grid == null) grid = new InventoryGrid(title, 5, 4);
            if (isCorpse || prefilled) { foreach (var it in grid.items) _hidden.Add(it); return; }
            var rng = new System.Random(seed == 0 ? Random.Range(1, int.MaxValue) : seed);
            Fill(rng);
            foreach (var it in grid.items) _hidden.Add(it);
        }

        // ------------------------------------------------------------------ v08 loot generation
        int _value;

        /// <summary>Minimum total price of a container (scaled by raid luck) — no more "two rounds in a huge crate".</summary>
        static int ValueFloor(ItemDB.LootTable t)
        {
            switch (t)
            {
                case ItemDB.LootTable.Crate: return 22000;
                case ItemDB.LootTable.Cabinet: return 25000;
                case ItemDB.LootTable.Safe: return 70000;
                case ItemDB.LootTable.Military: return 70000;
                case ItemDB.LootTable.AmmoBox: return 9000;
                case ItemDB.LootTable.WeaponCrate: return 0;
                default: return 12000;
            }
        }

        void Fill(System.Random rng)
        {
            float luck = ItemDB.Luck;
            _value = 0;
            if (table == ItemDB.LootTable.WeaponCrate)
            {
                var w = ItemDB.PickFrom(table, rng);
                AddItem(w, rng);
                int mags = rng.Next(1, 3) + (luck > 1.5f ? 1 : 0);
                for (int i = 0; i < mags; i++) AddItem(ItemDB.Get(w.magId), rng);
                AddItem(ItemDB.Get(rng.NextDouble() < 0.18 * luck ? (w.caliber == "9x18" ? "a918bp" : "a545bp") : (w.caliber == "9x18" ? "a918" : "a545")), rng);
                if (rng.NextDouble() < 0.5) AddItem(ItemDB.Get(w.caliber == "9x18" ? "a918" : "a545"), rng);
                return;
            }
            int n = rng.Next(minItems, maxItems + 1);
            if (luck > 1.3f && rng.NextDouble() < luck - 1f) n++;
            for (int i = 0; i < n; i++) AddItem(ItemDB.PickFrom(table, rng), rng);
            int floor = Mathf.RoundToInt(ValueFloor(table) * luck);
            for (int guard = 0; _value < floor && guard < 5; guard++) AddItem(ItemDB.PickFrom(table, rng), rng);
        }

        /// <summary>Adds one item with sensible contents (full-ish ammo stacks, loaded magazines/weapons).</summary>
        bool AddItem(ItemDef d, System.Random rng)
        {
            if (d == null) return false;
            var it = ItemInstance.Create(d.id);
            string ammoFor(ItemDef x) => x.caliber == "9x18" ? (rng.NextDouble() < 0.12 * ItemDB.Luck ? "a918bp" : "a918") : (rng.NextDouble() < 0.12 * ItemDB.Luck ? "a545bp" : "a545");
            switch (d.type)
            {
                case ItemType.Ammo:
                    it.count = isCorpse ? rng.Next(15, 45) : rng.NextDouble() < 0.45 ? d.maxStack : rng.Next(d.maxStack / 2, d.maxStack + 1);
                    break;
                case ItemType.Magazine:
                    it.ammoId = ammoFor(d);
                    it.ammo = rng.NextDouble() < 0.4 ? d.capacity : rng.Next(d.capacity / 2, d.capacity + 1);
                    break;
                case ItemType.Weapon:
                    {
                        it.ammoId = ammoFor(d);
                        var mag = ItemDB.Get(d.magId);
                        int cap = mag != null ? mag.capacity : 10;
                        it.ammo = rng.Next(cap / 3, cap + 1); it.chamber = rng.NextDouble() < 0.5 ? 1 : 0;
                        break;
                    }
                case ItemType.Armor:
                case ItemType.Helmet:
                    it.dur = d.durability * (float)(0.5 + rng.NextDouble() * 0.5);
                    break;
            }
            if (!grid.AutoPlace(it)) return false;
            _value += it.Price;
            // a weapon usually comes with a spare magazine
            if (d.type == ItemType.Weapon && table != ItemDB.LootTable.WeaponCrate && rng.NextDouble() < 0.6) AddItem(ItemDB.Get(d.magId), rng);
            return true;
        }

        /// <summary>LAN: contents changed on another machine. Items this player has not searched yet stay hidden.</summary>
        public void ApplyRemote(List<ItemInstance> items)
        {
            if (grid == null) grid = new InventoryGrid(title, 5, 4);
            bool wasGenerated = _generated;
            _generated = true;
            var hiddenKeys = new HashSet<string>();
            foreach (var it in _hidden) hiddenKeys.Add(it.id + "|" + it.x + "|" + it.y);
            _hidden.Clear();
            grid.items.Clear();
            foreach (var it in items)
            {
                if (ItemInstance.IsEmpty(it)) continue;
                if (!grid.TryPlaceAt(it, it.x, it.y, it.rot) && !grid.AutoPlace(it)) continue;
                if (!wasGenerated || !opened || hiddenKeys.Contains(it.id + "|" + it.x + "|" + it.y)) _hidden.Add(it);
            }
            grid.Notify();
        }

        public bool IsHidden(ItemInstance it) => _hidden.Contains(it);
        public bool SearchDone => _hidden.Count == 0;

        /// <summary>Called by the inventory UI every frame while this container is open.</summary>
        public void TickSearch(float dt)
        {
            if (_hidden.Count == 0) return;
            if (SFX.Search != null && Time.unscaledTime >= _nextSearchSnd)
            {
                _nextSearchSnd = Time.unscaledTime + SFX.Search.length - 0.05f;
                SFX.PlayAt(SFX.Search, transform.position + Vector3.up * 0.4f, 0.55f, 2f, 20f, 0.04f);
            }
            _searchTimer += dt;
            if (_searchTimer < searchTimePerItem) return;
            _searchTimer = 0f;
            ItemInstance first = null;
            foreach (var it in grid.items) if (_hidden.Contains(it)) { first = it; break; }
            if (first == null) { _hidden.Clear(); return; }
            _hidden.Remove(first);
            grid.Notify();
        }

        /// <summary>Procedural container visuals with a solid collider.</summary>
        public static LootContainer Create(ItemDB.LootTable t, Vector3 pos, Quaternion rot, Transform parent, int seed)
        {
            var g = new GameObject("Loot_" + t);
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos, rot);
            var lc = g.AddComponent<LootContainer>();
            lc.table = t; lc.seed = seed;
            Vector3 size;
            switch (t)
            {
                case ItemDB.LootTable.Bag:
                    lc.title = "Сумка"; size = new Vector3(0.7f, 0.35f, 0.4f); lc.grid = new InventoryGrid("Сумка", 4, 4); lc.minItems = 2; lc.maxItems = 4;
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, GameAssets.Lit(new Color(0.18f, 0.2f, 0.15f), 0.1f), false);
                    Prim.Box(g.transform, new Vector3(0, size.y + 0.03f, 0), new Vector3(0.5f, 0.04f, 0.06f), GameAssets.Lit(new Color(0.1f, 0.1f, 0.1f)), false);
                    break;
                case ItemDB.LootTable.Cabinet:
                    lc.title = "Шкаф"; size = new Vector3(0.6f, 1.4f, 0.5f); lc.grid = new InventoryGrid("Шкаф", 5, 5); lc.minItems = 2; lc.maxItems = 5;
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, GameAssets.Lit(new Color(0.35f, 0.37f, 0.36f), 0.3f, 0.4f), false);
                    for (int i = 0; i < 3; i++) Prim.Box(g.transform, new Vector3(0, 0.3f + i * 0.42f, size.z * 0.5f + 0.01f), new Vector3(0.5f, 0.02f, 0.02f), GameAssets.Lit(new Color(0.15f, 0.15f, 0.15f)), false);
                    break;
                case ItemDB.LootTable.Toolbox:
                    lc.title = "Ящик с инструментами"; size = new Vector3(0.5f, 0.26f, 0.24f); lc.grid = new InventoryGrid("Инструменты", 4, 3); lc.minItems = 2; lc.maxItems = 4;
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, GameAssets.Lit(new Color(0.45f, 0.08f, 0.06f), 0.35f, 0.3f), false);
                    break;
                case ItemDB.LootTable.Safe:
                    lc.title = "Сейф"; size = new Vector3(0.55f, 0.6f, 0.5f); lc.grid = new InventoryGrid("Сейф", 3, 3); lc.minItems = 2; lc.maxItems = 3; lc.searchTimePerItem = 1f;
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, GameAssets.Lit(new Color(0.12f, 0.13f, 0.12f), 0.4f, 0.6f), false);
                    Prim.Cyl(g.transform, new Vector3(0, 0.32f, size.z * 0.5f + 0.02f), 0.12f, 0.03f, GameAssets.Lit(new Color(0.5f, 0.5f, 0.5f), 0.5f, 0.8f), false, Quaternion.Euler(90, 0, 0));
                    break;
                case ItemDB.LootTable.Medbox:
                    lc.title = "Медицинская сумка"; size = new Vector3(0.45f, 0.3f, 0.3f); lc.grid = new InventoryGrid("Медсумка", 4, 3); lc.minItems = 2; lc.maxItems = 4;
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, GameAssets.Lit(new Color(0.62f, 0.6f, 0.55f), 0.2f), false);
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, size.z * 0.5f + 0.005f), new Vector3(0.18f, 0.05f, 0.01f), GameAssets.Lit(new Color(0.6f, 0.05f, 0.05f)), false);
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, size.z * 0.5f + 0.005f), new Vector3(0.05f, 0.18f, 0.01f), GameAssets.Lit(new Color(0.6f, 0.05f, 0.05f)), false);
                    break;
                case ItemDB.LootTable.WeaponCrate:
                    {
                        lc.title = "Оружейный ящик"; size = new Vector3(1.25f, 0.36f, 0.46f); lc.grid = new InventoryGrid("Оружейный ящик", 7, 3); lc.searchTimePerItem = 0.7f;
                        var olive = GameAssets.Lit(new Color(0.24f, 0.27f, 0.17f), 0.18f);
                        Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, olive, false);
                        Prim.Box(g.transform, new Vector3(0, size.y + 0.01f, 0), new Vector3(1.29f, 0.02f, 0.5f), GameAssets.Lit(new Color(0.2f, 0.23f, 0.14f), 0.2f), false);
                        var dark = GameAssets.Lit(new Color(0.08f, 0.08f, 0.07f), 0.3f, 0.6f);
                        for (int i = -1; i <= 1; i += 2) Prim.Box(g.transform, new Vector3(i * 0.42f, size.y * 0.55f, size.z * 0.5f + 0.012f), new Vector3(0.1f, 0.06f, 0.02f), dark, false);
                        Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, size.z * 0.5f + 0.004f), new Vector3(0.36f, 0.08f, 0.006f), GameAssets.Lit(new Color(0.75f, 0.72f, 0.6f), 0.1f), false);
                        break;
                    }
                case ItemDB.LootTable.AmmoBox:
                    lc.title = "Цинк с патронами"; size = new Vector3(0.42f, 0.24f, 0.3f); lc.grid = new InventoryGrid("Цинк", 3, 2); lc.minItems = 2; lc.maxItems = 4; lc.searchTimePerItem = 0.45f;
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, GameAssets.Lit(new Color(0.2f, 0.24f, 0.17f), 0.35f, 0.55f), false);
                    Prim.Box(g.transform, new Vector3(0, size.y + 0.012f, 0), new Vector3(0.2f, 0.025f, 0.04f), GameAssets.Lit(new Color(0.1f, 0.1f, 0.1f), 0.3f, 0.5f), false);
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.55f, size.z * 0.5f + 0.004f), new Vector3(0.22f, 0.05f, 0.006f), GameAssets.Lit(new Color(0.8f, 0.76f, 0.55f), 0.1f), false);
                    break;
                case ItemDB.LootTable.Military:
                    {
                        lc.title = "Военный ящик"; size = new Vector3(0.95f, 0.5f, 0.6f); lc.grid = new InventoryGrid("Военный ящик", 6, 5); lc.minItems = 3; lc.maxItems = 5; lc.searchTimePerItem = 0.8f;
                        Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, GameAssets.Lit(new Color(0.22f, 0.25f, 0.16f), 0.15f), false);
                        Prim.Box(g.transform, new Vector3(0, size.y * 0.72f, size.z * 0.5f + 0.004f), new Vector3(0.95f, 0.06f, 0.006f), GameAssets.Lit(new Color(0.6f, 0.08f, 0.06f), 0.1f), false);
                        Prim.Box(g.transform, new Vector3(0, size.y + 0.015f, 0), new Vector3(0.99f, 0.03f, 0.64f), GameAssets.Lit(new Color(0.19f, 0.22f, 0.14f), 0.15f), false);
                        break;
                    }
                default:
                    lc.title = "Деревянный ящик"; size = new Vector3(1.0f, 0.55f, 0.6f); lc.grid = new InventoryGrid("Ящик", 6, 4); lc.minItems = 3; lc.maxItems = 6;
                    Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, GameAssets.Lit(new Color(0.33f, 0.26f, 0.15f), 0.1f), false);
                    Prim.Box(g.transform, new Vector3(0, size.y + 0.015f, 0), new Vector3(1.04f, 0.03f, 0.64f), GameAssets.Lit(new Color(0.27f, 0.21f, 0.12f), 0.1f), false);
                    break;
            }
            var col = g.AddComponent<BoxCollider>();
            col.center = new Vector3(0, size.y * 0.5f, 0); col.size = size;
            return lc;
        }
    }
}
