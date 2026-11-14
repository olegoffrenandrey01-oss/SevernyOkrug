// Places loot containers and loose items: hand-placed map anchors (PolygonKit v2) and/or random NavMesh spots next to walls.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Severny
{
    public static class LootSpawner
    {
        public static int Spawn(Bounds area, int containers, int loose, Transform parent, int seed)
        {
            var rng = new System.Random(seed);
            int placed = 0, tries = 0;
            var used = new List<Vector3>();
            while (placed < containers && tries++ < containers * 30)
            {
                if (!RuntimeNavMesh.RandomPoint(area, out var p, 6f, 4)) continue;
                if (!NavMesh.FindClosestEdge(p, out var edge, NavMesh.AllAreas) || edge.distance > 6f) continue;
                Vector3 pos = edge.position;
                Vector3 n = edge.normal; n.y = 0f;
                if (n.sqrMagnitude < 0.01f) n = Vector3.forward;
                n.Normalize();
                // only next to something solid (wall/fence/car), not open NavMesh borders
                if (!Physics.Raycast(pos + Vector3.up * 0.5f, -n, out var wall, 1.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                bool near = false;
                foreach (var u in used) if ((u - pos).sqrMagnitude < 36f) { near = true; break; }
                if (near) continue;
                used.Add(pos);
                var t = PickTable(rng);
                var rot = Quaternion.LookRotation(n, Vector3.up);
                var lc = LootContainer.Create(t, pos + n * 0.35f, rot, parent, rng.Next(1, int.MaxValue));
                placed++;
            }
            int lp = 0; tries = 0;
            while (lp < loose && tries++ < loose * 20)
            {
                if (!RuntimeNavMesh.RandomPoint(area, out var p, 6f, 4)) continue;
                var d = ItemDB.PickFrom(rng.NextDouble() < 0.5 ? ItemDB.LootTable.Bag : ItemDB.LootTable.Toolbox, rng);
                var it = ItemInstance.Create(d.id);
                if (d.type == ItemType.Ammo) it.count = rng.Next(15, d.maxStack + 1);
                WorldItem.Drop(it, p + Vector3.up * 0.2f, parent);
                lp++;
            }
            Debug.Log("[Severny] Loot: containers " + placed + "/" + containers + ", loose " + lp);
            return placed;
        }

        /// <summary>Containers/items on the map's hand-placed anchors; a random share of them is used each raid.</summary>
        public static int SpawnFromAnchors(Transform parent, int seed, float containerShare = 0.62f, float itemShare = 0.55f)
        {
            var rng = new System.Random(seed);
            float boost = Mathf.Max(1f, ItemDB.Luck / 1.25f);           // night: more of the hand-placed spots are used
            containerShare = Mathf.Min(0.92f, containerShare * boost); itemShare = Mathf.Min(0.9f, itemShare * boost);
            int nc = 0, ni = 0;
            foreach (var a in MapAnchors.Loot)
            {
                if (rng.NextDouble() > containerShare) continue;
                if (!System.Enum.TryParse(a.table, out ItemDB.LootTable t)) t = PickTable(rng);
                LootContainer.Create(t, a.pos, Quaternion.Euler(0f, a.yaw, 0f), parent, rng.Next(1, int.MaxValue));
                nc++;
            }
            foreach (var a in MapAnchors.Items)
            {
                if (rng.NextDouble() > itemShare) continue;
                if (!System.Enum.TryParse(a.table, out ItemDB.LootTable t)) t = ItemDB.LootTable.Bag;
                var d = ItemDB.PickFrom(t, rng);
                var it = ItemInstance.Create(d.id);
                if (d.type == ItemType.Ammo) it.count = rng.Next(15, d.maxStack + 1);
                WorldItem.Drop(it, a.pos + Vector3.up * 0.12f, parent);
                ni++;
            }
            Debug.Log("[Severny] Loot (anchors): containers " + nc + "/" + MapAnchors.Loot.Count + ", loose " + ni + "/" + MapAnchors.Items.Count);
            return nc;
        }

        // container mix; good containers (safe / weapons / ammo / military) get more likely with raid luck (night)
        static readonly ItemDB.LootTable[] Tables = { ItemDB.LootTable.Crate, ItemDB.LootTable.Bag, ItemDB.LootTable.Toolbox, ItemDB.LootTable.Cabinet, ItemDB.LootTable.Medbox,
                                                      ItemDB.LootTable.Safe, ItemDB.LootTable.WeaponCrate, ItemDB.LootTable.AmmoBox, ItemDB.LootTable.Military };
        static readonly float[] Shares = { 0.24f, 0.17f, 0.11f, 0.11f, 0.11f, 0.05f, 0.08f, 0.09f, 0.04f };

        static ItemDB.LootTable PickTable(System.Random r)
        {
            float boost = Mathf.Max(1f, ItemDB.Luck / 1.25f);
            float total = 0f;
            for (int i = 0; i < Shares.Length; i++) total += Shares[i] * (i >= 5 ? boost : 1f);
            float v = (float)r.NextDouble() * total;
            for (int i = 0; i < Shares.Length; i++) { v -= Shares[i] * (i >= 5 ? boost : 1f); if (v <= 0f) return Tables[i]; }
            return ItemDB.LootTable.Crate;
        }
    }
}
