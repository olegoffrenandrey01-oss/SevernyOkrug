// Grid inventory model (pure C#, JsonUtility-serializable).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    [Serializable]
    public class ItemInstance
    {
        public string id = "";
        public int count = 1;
        public int x, y;
        public bool rot;
        public int ammo;        // magazine: rounds; weapon: rounds in inserted magazine
        public string ammoId = ""; // which ammo type is loaded (mag / weapon)
        public int chamber;     // weapon: round in chamber (0/1)
        public float dur = -1f; // armor durability, med resource (-1 = full / default)
        public bool fullAutoMode;

        public ItemDef Def => ItemDB.Get(id);
        public static bool IsEmpty(ItemInstance i) => i == null || string.IsNullOrEmpty(i.id) || ItemDB.Get(i.id) == null;

        public static ItemInstance Create(string id, int count = 1)
        {
            var d = ItemDB.Get(id);
            var it = new ItemInstance { id = id, count = Mathf.Max(1, count) };
            if (d == null) return it;
            if (d.type == ItemType.Armor || d.type == ItemType.Helmet) it.dur = d.durability;
            if (d.type == ItemType.Medical) it.dur = d.med == MedKind.Medkit ? d.hpPool : d.uses;
            if (d.type == ItemType.Weapon) it.fullAutoMode = d.fullAuto;
            return it;
        }

        public ItemInstance Clone() => (ItemInstance)MemberwiseClone();

        public int W => Def == null ? 1 : (rot ? Def.h : Def.w);
        public int H => Def == null ? 1 : (rot ? Def.w : Def.h);

        public int Price
        {
            get
            {
                var d = Def; if (d == null) return 0;
                float p = d.price * count;
                if ((d.type == ItemType.Armor || d.type == ItemType.Helmet) && d.durability > 0 && dur >= 0) p *= Mathf.Lerp(0.3f, 1f, dur / d.durability);
                if (d.type == ItemType.Medical && dur >= 0)
                {
                    float full = d.med == MedKind.Medkit ? d.hpPool : Mathf.Max(1, d.uses);
                    p *= Mathf.Clamp01(dur / Mathf.Max(1f, full));
                }
                if (d.type == ItemType.Magazine && ammo > 0) { var a = ItemDB.Get(ammoId); if (a != null) p += a.price * ammo; }
                return Mathf.RoundToInt(p);
            }
        }

        public float Weight
        {
            get
            {
                var d = Def; if (d == null) return 0f;
                float w = d.weight * count;
                if (ammo > 0) { var a = ItemDB.Get(ammoId); if (a != null) w += a.weight * ammo; }
                return w;
            }
        }

        public string Badge
        {
            get
            {
                var d = Def; if (d == null) return "";
                switch (d.type)
                {
                    case ItemType.Ammo: return count.ToString();
                    case ItemType.Magazine: return ammo + "/" + d.capacity;
                    case ItemType.Weapon: return "";
                    case ItemType.Armor:
                    case ItemType.Helmet: return Mathf.CeilToInt(Mathf.Max(0, dur)) + "/" + Mathf.CeilToInt(d.durability);
                    case ItemType.Medical: return d.med == MedKind.Medkit ? Mathf.CeilToInt(Mathf.Max(0, dur)) + "/" + Mathf.CeilToInt(d.hpPool) : (d.uses > 1 ? Mathf.CeilToInt(Mathf.Max(0, dur)) + "/" + d.uses : "");
                    default: return count > 1 ? count.ToString() : "";
                }
            }
        }
    }

    [Serializable]
    public class InventoryGrid
    {
        public string name = "";
        public int w = 1, h = 1;
        public List<ItemInstance> items = new List<ItemInstance>();

        [NonSerialized] public Action Changed;

        public InventoryGrid() { }
        public InventoryGrid(string name, int w, int h) { this.name = name; this.w = w; this.h = h; }

        public void Notify() { Changed?.Invoke(); }

        public bool InBounds(int x, int y, int iw, int ih) => x >= 0 && y >= 0 && x + iw <= w && y + ih <= h;

        public bool Fits(int x, int y, int iw, int ih, ItemInstance ignore = null)
        {
            if (!InBounds(x, y, iw, ih)) return false;
            for (int i = 0; i < items.Count; i++)
            {
                var o = items[i];
                if (o == ignore) continue;
                if (x < o.x + o.W && x + iw > o.x && y < o.y + o.H && y + ih > o.y) return false;
            }
            return true;
        }

        public ItemInstance ItemAt(int cx, int cy)
        {
            foreach (var o in items)
                if (cx >= o.x && cx < o.x + o.W && cy >= o.y && cy < o.y + o.H) return o;
            return null;
        }

        public bool TryPlaceAt(ItemInstance it, int x, int y, bool rot)
        {
            bool old = it.rot; it.rot = rot;
            if (Fits(x, y, it.W, it.H, it))
            {
                it.x = x; it.y = y;
                if (!items.Contains(it)) items.Add(it);
                Notify();
                return true;
            }
            it.rot = old;
            return false;
        }

        public bool FindFree(ItemInstance it, out int fx, out int fy, out bool frot)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool r = pass == 1;
                var d = it.Def; if (d == null) break;
                if (r && d.w == d.h) break;
                int iw = r ? d.h : d.w, ih = r ? d.w : d.h;
                for (int yy = 0; yy <= h - ih; yy++)
                    for (int xx = 0; xx <= w - iw; xx++)
                        if (Fits(xx, yy, iw, ih, it)) { fx = xx; fy = yy; frot = r; return true; }
            }
            fx = fy = 0; frot = false; return false;
        }

        /// <summary>Stacks into existing stacks first, then finds free space. Returns false if (partially) not placed.</summary>
        public bool AutoPlace(ItemInstance it)
        {
            var d = it.Def; if (d == null) return false;
            if (d.Stackable)
            {
                foreach (var o in items)
                {
                    if (o == it || o.id != it.id || o.count >= d.maxStack) continue;
                    int mv = Mathf.Min(d.maxStack - o.count, it.count);
                    o.count += mv; it.count -= mv;
                    if (it.count <= 0) { Notify(); return true; }
                }
            }
            if (FindFree(it, out int fx, out int fy, out bool fr))
            {
                it.rot = fr; it.x = fx; it.y = fy;
                if (!items.Contains(it)) items.Add(it);
                Notify();
                return true;
            }
            Notify();
            return false;
        }

        /// <summary>All-or-nothing placement for purchases. AutoPlace intentionally permits partial stacking elsewhere.</summary>
        public bool TryAutoPlaceAll(ItemInstance it)
        {
            if (it == null || it.Def == null || items.Contains(it)) return false;
            int remaining = it.count;
            if (it.Def.Stackable)
                foreach (var o in items)
                    if (o.id == it.id) remaining -= Mathf.Max(0, it.Def.maxStack - o.count);
            if (remaining > 0 && !FindFree(it, out _, out _, out _)) return false;
            return AutoPlace(it);
        }

        public bool Remove(ItemInstance it) { bool r = items.Remove(it); if (r) Notify(); return r; }

        public int Count(string id) { int n = 0; foreach (var o in items) if (o.id == id) n += o.count; return n; }

        public int AmmoCount(string caliber)
        {
            int n = 0;
            foreach (var o in items) { var d = o.Def; if (d != null && d.type == ItemType.Ammo && d.caliber == caliber) n += o.count; }
            return n;
        }

        /// <summary>Takes up to n rounds of the given caliber. Same ammoId first; if allowSwitch, may pick another type (only before anything was taken).</summary>
        public int TakeAmmo(string caliber, int n, ref string ammoId, bool allowSwitch)
        {
            int taken = 0;
            for (int pass = 0; pass < 2 && taken < n; pass++)
            {
                if (pass == 1 && !allowSwitch && !string.IsNullOrEmpty(ammoId)) break;
                for (int i = items.Count - 1; i >= 0 && taken < n; i--)
                {
                    var o = items[i]; var d = o.Def;
                    if (d == null || d.type != ItemType.Ammo || d.caliber != caliber) continue;
                    bool same = !string.IsNullOrEmpty(ammoId) && o.id == ammoId;
                    if (pass == 0 && !same) continue;
                    if (pass == 1 && !same) { if (taken > 0) continue; ammoId = o.id; }
                    int mv = Mathf.Min(o.count, n - taken);
                    o.count -= mv; taken += mv;
                    if (o.count <= 0) items.RemoveAt(i);
                }
            }
            if (taken > 0) Notify();
            return taken;
        }

        public ItemInstance BestMag(string magId, ItemInstance exclude = null)
        {
            ItemInstance best = null;
            foreach (var o in items)
                if (o != exclude && o.id == magId && (best == null || o.ammo > best.ammo)) best = o;
            return best;
        }

        public int Value() { int v = 0; foreach (var o in items) v += o.Price; return v; }
        public float Weight() { float v = 0; foreach (var o in items) v += o.Weight; return v; }
        public void Clear() { items.Clear(); Notify(); }
    }

    public enum EquipSlot { Primary, Holster, Armor, Helmet }

    /// <summary>Player equipment + carried grids. Lost on death (except the secure pouch).</summary>
    [Serializable]
    public class Loadout
    {
        public ItemInstance primary = new ItemInstance();
        public ItemInstance holster = new ItemInstance();
        public ItemInstance armor = new ItemInstance();
        public ItemInstance helmet = new ItemInstance();
        public InventoryGrid pockets = new InventoryGrid("Карманы", 4, 1);
        public InventoryGrid rig = new InventoryGrid("Разгрузка", 4, 2);
        public InventoryGrid backpack = new InventoryGrid("Рюкзак", 5, 5);
        public InventoryGrid secure = new InventoryGrid("Подсумок (не теряется)", 2, 2);

        public ItemInstance Get(EquipSlot s)
        {
            switch (s)
            {
                case EquipSlot.Primary: return ItemInstance.IsEmpty(primary) ? null : primary;
                case EquipSlot.Holster: return ItemInstance.IsEmpty(holster) ? null : holster;
                case EquipSlot.Armor: return ItemInstance.IsEmpty(armor) ? null : armor;
                default: return ItemInstance.IsEmpty(helmet) ? null : helmet;
            }
        }

        public void Set(EquipSlot s, ItemInstance it)
        {
            var v = it ?? new ItemInstance();
            switch (s)
            {
                case EquipSlot.Primary: primary = v; break;
                case EquipSlot.Holster: holster = v; break;
                case EquipSlot.Armor: armor = v; break;
                default: helmet = v; break;
            }
        }

        public static bool Accepts(EquipSlot s, ItemDef d)
        {
            if (d == null) return false;
            switch (s)
            {
                case EquipSlot.Primary: return d.type == ItemType.Weapon && !d.isPistol;
                case EquipSlot.Holster: return d.type == ItemType.Weapon && d.isPistol;
                case EquipSlot.Armor: return d.type == ItemType.Armor;
                default: return d.type == ItemType.Helmet;
            }
        }

        /// <summary>Grids searched for reload / meds (EFT-like: rig and pockets first).</summary>
        public IEnumerable<InventoryGrid> QuickGrids() { yield return rig; yield return pockets; }
        public IEnumerable<InventoryGrid> AllGrids() { yield return rig; yield return pockets; yield return backpack; yield return secure; }

        public bool AutoPlace(ItemInstance it)
        {
            foreach (var g in new[] { rig, pockets, backpack })
                if (g.AutoPlace(it)) return true;
            return false;
        }

        public float TotalWeight()
        {
            float w = 0f;
            foreach (EquipSlot s in Enum.GetValues(typeof(EquipSlot))) { var i = Get(s); if (i != null) w += i.Weight; }
            foreach (var g in AllGrids()) w += g.Weight();
            return w;
        }

        public int TotalValue()
        {
            int v = 0;
            foreach (EquipSlot s in Enum.GetValues(typeof(EquipSlot))) { var i = Get(s); if (i != null) v += i.Price; }
            foreach (var g in AllGrids()) v += g.Value();
            return v;
        }

        public void FixupAfterLoad()
        {
            if (pockets == null) pockets = new InventoryGrid("Карманы", 4, 1);
            if (rig == null) rig = new InventoryGrid("Разгрузка", 4, 2);
            if (backpack == null) backpack = new InventoryGrid("Рюкзак", 5, 5);
            if (secure == null) secure = new InventoryGrid("Подсумок (не теряется)", 2, 2);
            foreach (var g in AllGrids()) g.items.RemoveAll(i => ItemInstance.IsEmpty(i));
        }

        /// <summary>Death: everything except the secure pouch is lost.</summary>
        public void LoseAllExceptSecure()
        {
            primary = new ItemInstance(); holster = new ItemInstance(); armor = new ItemInstance(); helmet = new ItemInstance();
            pockets.Clear(); rig.Clear(); backpack.Clear();
        }
    }
}
