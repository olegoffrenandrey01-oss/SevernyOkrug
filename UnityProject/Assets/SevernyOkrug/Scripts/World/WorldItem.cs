// Loose item lying in the world (pick up with F).
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public class WorldItem : Interactable
    {
        public ItemInstance item;
        /// <summary>LAN: network id (0 = not networked yet).</summary>
        public int nid;
        public static readonly List<WorldItem> All = new List<WorldItem>();
        /// <summary>LAN hooks (set by NetSession): an item was dropped / picked up on this machine.</summary>
        public static System.Action<WorldItem> NetDropped, NetTaken;
        public static bool SuppressNet;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public override string Prompt => item != null && item.Def != null ? "Подобрать: " + item.Def.name + (item.count > 1 ? " ×" + item.count : "") : "Подобрать";

        public override void Interact(PlayerController p)
        {
            if (ItemInstance.IsEmpty(item)) { Destroy(gameObject); return; }
            var d = item.Def;
            EquipSlot slot;
            if (TrySlot(d, out slot) && p.loadout.Get(slot) == null)
            {
                p.loadout.Set(slot, item);
                p.RefreshEquipment();
                Done(p, "Экипировано: " + d.name);
                return;
            }
            int before = item.count;
            if (p.loadout.AutoPlace(item)) { Done(p, "Подобрано: " + d.name); return; }
            if (item.count < before) Hud.Notify("Подобрано частично, нет места", 2f);
            else Hud.Notify("Нет места в рюкзаке/разгрузке/карманах", 2f);
        }

        void Done(PlayerController p, string msg)
        {
            SFX.Play2D(SFX.Pickup, 0.6f);
            Hud.Notify(msg, 2f);
            RaidManager.I?.OnItemPicked(item);
            if (!SuppressNet) NetTaken?.Invoke(this);
            Destroy(gameObject);
        }

        static bool TrySlot(ItemDef d, out EquipSlot s)
        {
            s = EquipSlot.Primary;
            if (d == null) return false;
            if (d.type == ItemType.Weapon) { s = d.isPistol ? EquipSlot.Holster : EquipSlot.Primary; return true; }
            if (d.type == ItemType.Armor) { s = EquipSlot.Armor; return true; }
            if (d.type == ItemType.Helmet) { s = EquipSlot.Helmet; return true; }
            return false;
        }

        public static WorldItem Drop(ItemInstance it, Vector3 pos, Transform parent = null)
        {
            if (ItemInstance.IsEmpty(it)) return null;
            var d = it.Def;
            var g = new GameObject("Item_" + d.id);
            if (parent == null && RaidManager.I != null) parent = RaidManager.I.runtimeRoot;
            if (parent != null) g.transform.SetParent(parent, true);
            if (Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
            g.transform.position = pos + Vector3.up * 0.02f;
            g.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            Vector3 size;
            if (d.type == ItemType.Weapon)
            {
                var v = WeaponModels.Build(d, g.transform, false, false);
                v.root.transform.localPosition = new Vector3(0, 0.04f, 0);
                v.root.transform.localRotation = Quaternion.Euler(0, 0, 90) * v.rest;   // on its side, barrel along +Z (v09: keep FBX rest rotation)
                size = new Vector3(0.25f, 0.2f, d.isPistol ? 0.3f : 1.0f);
            }
            else
            {
                size = new Vector3(0.12f * d.w, 0.07f + 0.02f * Mathf.Min(d.w, d.h), 0.12f * d.h);
                Prim.Box(g.transform, new Vector3(0, size.y * 0.5f, 0), size, GameAssets.Lit(d.color * 1.4f, 0.2f), false, null, "mesh");
            }
            var col = g.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0, Mathf.Max(0.1f, size.y * 0.5f), 0);
            col.size = new Vector3(Mathf.Max(0.3f, size.x), Mathf.Max(0.25f, size.y), Mathf.Max(0.3f, size.z));
            var wi = g.AddComponent<WorldItem>();
            wi.item = it;
            if (!SuppressNet) NetDropped?.Invoke(wi);
            return wi;
        }
    }
}
