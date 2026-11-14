using UnityEngine;
namespace Severny
{
    public static class UtilityItems
    {
        public static bool CanUse(ItemInstance it) => it != null && it.Def != null && (it.Def.type == ItemType.Food || it.id == "tools");
        public static bool HasTools(Loadout l)
        {
            if (l == null) return false;
            foreach (var g in l.AllGrids()) if (g.Count("tools") > 0) return true;
            return false;
        }
        public static string Use(PlayerController p, ItemInstance it, InventoryGrid source)
        {
            if (p == null || source == null || it == null || !source.items.Contains(it) || !CanUse(it)) return "Предмет недоступен";
            bool owned = false;
            foreach (var g in p.loadout.AllGrids()) if (g == source) owned = true;
            if (!owned || p.health.IsDead || p.IsUsingMed) return "Сейчас нельзя использовать";
            if (it.Def.type == ItemType.Food)
            {
                if (p.stamina >= p.staminaMax - 0.01f) return "Выносливость уже восстановлена";
                p.stamina = Mathf.Min(p.staminaMax, p.stamina + it.Def.energy);
                SFX.Play2D(it.id == "water" || it.id == "juice" || it.id == "thermos" ? SFX.Drink : SFX.Eat, 0.65f);
                if (--it.count <= 0) source.Remove(it); else source.Notify();
                return "Выносливость восстановлена";
            }
            var armor = p.loadout.armor;
            if (armor == null || armor.Def == null) return "Сначала наденьте бронежилет";
            if (armor.dur < 0f || armor.dur >= armor.Def.durability) return "Бронежилет не требует ремонта";
            foreach (var g in p.loadout.AllGrids())
                foreach (var tape in g.items)
                    if (tape.id == "tape" && tape.count > 0)
                    {
                        armor.dur = Mathf.Min(armor.Def.durability, armor.dur + V14Settings.Current.armorRepair);
                        if (--tape.count <= 0) g.Remove(tape); else g.Notify();
                        return "Броня отремонтирована; израсходована изолента";
                    }
            return "Для ремонта нужна изолента в инвентаре";
        }
    }
}
