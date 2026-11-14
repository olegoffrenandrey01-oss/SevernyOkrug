// LAN co-op: the raid world as the host sees it (sent once to every client after it loaded the map):
// spawn point, raid timer, exits, containers (seed = same contents; contents only if already touched), loose items,
// bots (position, look seed, gear; dead ones with their loot) and the burning barrels of a night raid.
using System.Collections.Generic;
using UnityEngine;

namespace Severny
{
    public class NetWorld
    {
        public class Cont { public int cnid, table, seed; public Vector3 pos; public float yaw; public List<ItemInstance> items; }
        public class Loose { public int inid; public ItemInstance item; public Vector3 pos; public float yaw; }
        public class Bot { public int nid, bodySeed; public Vector3 pos; public float yaw; public string weapon, ammo, armor, helmet; public bool dead; public List<ItemInstance> loot; }

        public Vector3 spawnPos;
        public float spawnYaw, timeLeft, raidLength;
        public readonly List<KeyValuePair<string, Vector3>> exits = new List<KeyValuePair<string, Vector3>>();
        public readonly List<Cont> containers = new List<Cont>();
        public readonly List<Loose> items = new List<Loose>();
        public readonly List<Bot> bots = new List<Bot>();
        public readonly List<Vector3> fires = new List<Vector3>();

        public static int BotCorpseNid(int botNid) => 1000000 + botNid;
        public static int PlayerCorpseNid(int pid) => 2000000 + pid;

        /// <summary>A grid that holds all the items (keeps their positions when possible, grows if needed).</summary>
        public static InventoryGrid GridFor(List<ItemInstance> items, string title, int w = 8, int h = 8)
        {
            for (int attempt = 0; attempt < 12; attempt++, h += 3)
            {
                var g = new InventoryGrid(title, w, h);
                bool ok = true;
                foreach (var src in items)
                {
                    if (ItemInstance.IsEmpty(src)) continue;
                    var it = src.Clone();
                    if (!g.TryPlaceAt(it, it.x, it.y, it.rot) && !g.AutoPlace(it)) { ok = false; break; }
                }
                if (ok) return g;
            }
            var last = new InventoryGrid(title, w, h);
            foreach (var src in items) if (!ItemInstance.IsEmpty(src)) last.AutoPlace(src.Clone());
            return last;
        }

        // ------------------------------------------------------------------ host: capture + ids
        /// <summary>Gives every container, loose item and bot of the host's raid a network id.</summary>
        public static void AssignIds(Transform runtimeRoot)
        {
            int c = 0;
            foreach (var lc in runtimeRoot.GetComponentsInChildren<LootContainer>(true))
                if (lc.nid == 0 && !lc.isCorpse) lc.nid = ++c;
            int i = 0;
            foreach (var wi in WorldItem.All) if (wi != null && wi.nid == 0) wi.nid = ++i;
            int b = 0;
            foreach (var bot in BotController.All) if (bot != null && bot.nid == 0) bot.nid = ++b;
        }

        public static NetWorld Capture(RaidManager rm)
        {
            var w = new NetWorld { spawnPos = rm.spawnPos, spawnYaw = rm.spawnYaw, timeLeft = rm.timeLeft, raidLength = rm.raidLength };
            foreach (var e in rm.exits) if (e != null) w.exits.Add(new KeyValuePair<string, Vector3>(e.exitName, e.transform.position));
            foreach (var lc in rm.runtimeRoot.GetComponentsInChildren<LootContainer>(true))
            {
                if (lc.nid <= 0 || lc.isCorpse || lc.nid >= 1000000) continue;
                w.containers.Add(new Cont
                {
                    cnid = lc.nid, table = (int)lc.table, seed = lc.seed, pos = lc.transform.position, yaw = lc.transform.eulerAngles.y,
                    items = lc.Generated && lc.grid != null ? new List<ItemInstance>(lc.grid.items) : null
                });
            }
            foreach (var wi in WorldItem.All)
                if (wi != null && wi.nid != 0 && !ItemInstance.IsEmpty(wi.item))
                    w.items.Add(new Loose { inid = wi.nid, item = wi.item, pos = wi.transform.position, yaw = wi.transform.eulerAngles.y });
            foreach (var bot in rm.runtimeRoot.GetComponentsInChildren<BotController>(true))
            {
                if (bot.nid == 0) continue;
                var lc = bot.IsDead ? bot.GetComponent<LootContainer>() : null;
                w.bots.Add(new Bot
                {
                    nid = bot.nid, bodySeed = bot.bodySeed, pos = bot.transform.position, yaw = bot.transform.eulerAngles.y,
                    weapon = bot.weaponItem != null ? bot.weaponItem.id : "p9m", ammo = bot.AmmoDef != null ? bot.AmmoDef.id : "",
                    armor = bot.health != null && !ItemInstance.IsEmpty(bot.health.armor) ? bot.health.armor.id : "",
                    helmet = bot.health != null && !ItemInstance.IsEmpty(bot.health.helmet) ? bot.health.helmet.id : "",
                    dead = bot.IsDead, loot = lc != null && lc.grid != null ? new List<ItemInstance>(lc.grid.items) : null
                });
            }
            if (WeatherFX.I != null) w.fires.AddRange(WeatherFX.I.FirePositions);
            return w;
        }

        // ------------------------------------------------------------------ wire format
        public byte[] Write()
        {
            var m = new NetWriter(Msg.World);
            m.V3(spawnPos).F32(spawnYaw).F32(timeLeft).F32(raidLength);
            m.I16(exits.Count); foreach (var e in exits) m.Str(e.Key).V3(e.Value);
            m.I32(containers.Count);
            foreach (var c in containers)
            {
                m.I32(c.cnid).U8(c.table).I32(c.seed).V3(c.pos).F32(c.yaw).Bool(c.items != null);
                if (c.items != null) m.Items(c.items);
            }
            m.I32(items.Count); foreach (var it in items) m.I32(it.inid).Item(it.item).V3(it.pos).F32(it.yaw);
            m.I32(bots.Count);
            foreach (var b in bots)
            {
                m.I32(b.nid).I32(b.bodySeed).V3(b.pos).F32(b.yaw).Str(b.weapon).Str(b.ammo).Str(b.armor).Str(b.helmet).Bool(b.dead);
                if (b.dead) m.Items(b.loot ?? new List<ItemInstance>());
            }
            m.I16(fires.Count); foreach (var f in fires) m.V3(f);
            return m.ToArray();
        }

        public static NetWorld Read(NetReader r)
        {
            var w = new NetWorld { spawnPos = r.V3(), spawnYaw = r.F32(), timeLeft = r.F32(), raidLength = r.F32() };
            int ne = r.I16(); for (int i = 0; i < ne; i++) { string n = r.Str(); w.exits.Add(new KeyValuePair<string, Vector3>(n, r.V3())); }
            int nc = r.I32();
            for (int i = 0; i < nc; i++)
            {
                var c = new Cont { cnid = r.I32(), table = r.U8(), seed = r.I32(), pos = r.V3(), yaw = r.F32() };
                if (r.Bool()) c.items = r.Items();
                w.containers.Add(c);
            }
            int ni = r.I32(); for (int i = 0; i < ni; i++) w.items.Add(new Loose { inid = r.I32(), item = r.Item(), pos = r.V3(), yaw = r.F32() });
            int nb = r.I32();
            for (int i = 0; i < nb; i++)
            {
                var b = new Bot { nid = r.I32(), bodySeed = r.I32(), pos = r.V3(), yaw = r.F32(), weapon = r.Str(), ammo = r.Str(), armor = r.Str(), helmet = r.Str(), dead = r.Bool() };
                if (b.dead) b.loot = r.Items();
                w.bots.Add(b);
            }
            int nf = r.I16(); for (int i = 0; i < nf; i++) w.fires.Add(r.V3());
            return w;
        }

        // ------------------------------------------------------------------ client: build
        public void Build(Transform root)
        {
            foreach (var c in containers)
            {
                var lc = LootContainer.Create((ItemDB.LootTable)c.table, c.pos, Quaternion.Euler(0f, c.yaw, 0f), root, c.seed);
                lc.nid = c.cnid;
                if (c.items != null) lc.ApplyRemote(c.items);
            }
            WorldItem.SuppressNet = true;
            try
            {
                foreach (var it in items)
                {
                    var wi = WorldItem.Drop(it.item, it.pos, root);
                    if (wi == null) continue;
                    wi.nid = it.inid;
                    wi.transform.position = it.pos;
                    wi.transform.rotation = Quaternion.Euler(0f, it.yaw, 0f);
                }
            }
            finally { WorldItem.SuppressNet = false; }
            foreach (var b in bots)
            {
                var p = NetBotProxy.Create(b.nid, b.pos, b.yaw, b.bodySeed, b.weapon, b.ammo, b.armor, b.helmet, root);
                if (b.dead) p.Die(b.pos + Vector3.up, Vector3.down, b.loot ?? new List<ItemInstance>());
            }
        }
    }
}
