// LAN message ids + serialization helpers for Unity types and items.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public static class Msg
    {
        public const byte Hello = 1, Welcome = 2, Reject = 3, Lobby = 4, RaidStart = 5, Loaded = 6, World = 7,
                          PState = 8, Shot = 9, BotStates = 10, BotShot = 11, HitBot = 12, BotDie = 13, HitPlayer = 14,
                          Container = 15, ItemDrop = 16, ItemTake = 17, PlayerDied = 18, PlayerLeft = 19, RaidEnd = 20,
                          Time = 21, Bye = 22, Toast = 23, Ready = 24;
    }

    public static class NetIO
    {
        public static NetWriter V3(this NetWriter w, Vector3 v) => w.F32(v.x).F32(v.y).F32(v.z);
        public static Vector3 V3(this NetReader r) => new Vector3(r.F32(), r.F32(), r.F32());

        public static NetWriter Item(this NetWriter w, ItemInstance it)
        {
            w.Str(it.id).I32(it.count).U8(it.x).U8(it.y).Bool(it.rot).I32(it.ammo).Str(it.ammoId).U8(it.chamber).F32(it.dur).Bool(it.fullAutoMode);
            return w;
        }

        public static ItemInstance Item(this NetReader r)
        {
            return new ItemInstance
            {
                id = r.Str(), count = r.I32(), x = r.U8(), y = r.U8(), rot = r.Bool(), ammo = r.I32(), ammoId = r.Str(),
                chamber = r.U8(), dur = r.F32(), fullAutoMode = r.Bool()
            };
        }

        public static NetWriter Items(this NetWriter w, IList<ItemInstance> items)
        {
            int n = 0;
            if (items != null) foreach (var it in items) if (!ItemInstance.IsEmpty(it)) n++;
            w.I16(n);
            if (items != null) foreach (var it in items) if (!ItemInstance.IsEmpty(it)) w.Item(it);
            return w;
        }

        public static List<ItemInstance> Items(this NetReader r)
        {
            int n = r.I16();
            var l = new List<ItemInstance>(Mathf.Max(0, n));
            for (int i = 0; i < n; i++) { var it = r.Item(); if (!ItemInstance.IsEmpty(it)) l.Add(it); }
            return l;
        }

        /// <summary>Content signature of a grid (detects local changes of an open container).</summary>
        public static string Signature(InventoryGrid g)
        {
            if (g == null) return "";
            var sb = new System.Text.StringBuilder(64);
            foreach (var it in g.items)
                if (!ItemInstance.IsEmpty(it)) sb.Append(it.id).Append(',').Append(it.count).Append(',').Append(it.x).Append(',').Append(it.y).Append(it.rot ? 'r' : '-')
                                                 .Append(it.ammo).Append(it.ammoId).Append(it.chamber).Append(Mathf.RoundToInt(it.dur * 10f)).Append(';');
            return sb.ToString();
        }

        /// <summary>Everything a killed player carried except the secure pouch (dropped as a lootable corpse).</summary>
        public static List<ItemInstance> DroppedOnDeath(Loadout L)
        {
            var l = new List<ItemInstance>();
            foreach (EquipSlot s in System.Enum.GetValues(typeof(EquipSlot))) { var it = L.Get(s); if (it != null) l.Add(it.Clone()); }
            foreach (var g in new[] { L.rig, L.pockets, L.backpack }) foreach (var it in g.items) if (!ItemInstance.IsEmpty(it)) l.Add(it.Clone());
            return l;
        }
    }
}
