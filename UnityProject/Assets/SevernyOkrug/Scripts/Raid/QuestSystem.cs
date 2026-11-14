using System;
using System.Collections.Generic;
namespace Severny
{
    [Serializable] public class QuestState
    {
        public string id;
        public bool active, claimed;
        public int progress;
    }
    public class QuestDef
    {
        public string id, title, description, action, item;
        public int target, money, xp;
    }
    public static class QuestSystem
    {
        public static readonly QuestDef[] All =
        {
            new QuestDef { id = "v14_search", title = "Разведка запасов", description = "Полностью обыщите 3 разных контейнера в обычных рейдах.", action = "search", target = 3, money = 10000, xp = 450 },
            new QuestDef { id = "v14_defend", title = "Опасные соседи", description = "Нейтрализуйте 3 ботов в обычных рейдах.", action = "kill", target = 3, money = 18000, xp = 700 },
            new QuestDef { id = "v14_supplies", title = "Запасы для убежища", description = "Передайте 2 консервы из схрона. Предметы расходуются при сдаче в главном меню.", action = "deliver", item = "stew", target = 2, money = 9000, xp = 350 }
        };
        public static QuestState State(PlayerProfile p, string id)
        {
            if (p.quests == null) p.quests = new List<QuestState>();
            var s = p.quests.Find(q => q != null && q.id == id);
            if (s == null) { s = new QuestState { id = id }; p.quests.Add(s); }
            return s;
        }
        public static void Record(string action)
        {
            var raid = RaidManager.I;
            if (raid == null || !raid.Running || raid.IsTutorial) return;
            var p = SaveSystem.Current;
            bool changed = false;
            foreach (var d in All)
            {
                var s = State(p, d.id);
                if (s.active && !s.claimed && d.action == action && s.progress < d.target) { s.progress++; changed = true; }
            }
            if (changed) { Hud.Notify("Прогресс задания обновлён — журнал F6"); }
        }
        public static bool Accept(QuestDef d)
        {
            if (GameBootstrap.I != null && (GameBootstrap.I.InRaid || GameBootstrap.I.Busy)) return false;
            var s = State(SaveSystem.Current, d.id);
            if (s.active || s.claimed) return false;
            s.active = true;
            if (SaveSystem.TrySave()) return true;
            s.active = false; return false;
        }
        public static int Progress(PlayerProfile p, QuestDef d)
        {
            var s = State(p, d.id);
            if (!s.active && !s.claimed) return 0;
            return d.action == "deliver" && !s.claimed ? Math.Min(d.target, p.stash.Count(d.item)) : Math.Min(d.target, s.progress);
        }
        public static bool Claim(QuestDef d)
        {
            if (GameBootstrap.I != null && (GameBootstrap.I.InRaid || GameBootstrap.I.Busy)) return false;
            var p = SaveSystem.Current;
            var s = State(p, d.id);
            if (!s.active || s.claimed || Progress(p, d) < d.target) return false;
            var originals = new List<ItemInstance>(p.stash.items);
            var counts = originals.ConvertAll(i => i.count);
            int oldMoney = p.roubles, oldXp = p.xp, oldProgress = s.progress;
            if (d.action == "deliver")
            {
                int remaining = d.target;
                for (int i = p.stash.items.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    var it = p.stash.items[i];
                    if (it.id != d.item) continue;
                    int n = Math.Min(remaining, it.count); it.count -= n; remaining -= n;
                    if (it.count <= 0) p.stash.items.RemoveAt(i);
                }
            }
            s.progress = d.target; s.claimed = true;
            p.roubles += d.money; p.xp += d.xp;
            if (SaveSystem.TrySave()) { p.stash.Notify(); return true; }
            p.stash.items.Clear(); p.stash.items.AddRange(originals);
            for (int i = 0; i < originals.Count; i++) originals[i].count = counts[i];
            s.claimed = false; s.progress = oldProgress; p.roubles = oldMoney; p.xp = oldXp;
            return false;
        }
    }
}
