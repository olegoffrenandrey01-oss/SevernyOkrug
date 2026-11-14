// Available raid locations.
using UnityEngine;

namespace Severny
{
    public class MapDef
    {
        public string id, title, scene, desc;
        public bool hidden;
        public int minutes;
        public int[] bots; // few / normal / many
        public float fog;
        public bool SceneAvailable => !string.IsNullOrEmpty(scene) && Application.CanStreamedLevelBeLoaded(scene);
        public bool Playable => string.IsNullOrEmpty(scene) || SceneAvailable;
        /// <summary>Procedural maps are always ready; scene maps after menu Severny > 3 Place Map.</summary>
        public bool Placed => string.IsNullOrEmpty(scene) || (GameAssets.SO != null && GameAssets.SO.severnyMapPlaced);
    }

    public static class MapCatalog
    {
        public static readonly MapDef Severny = new MapDef
        {
            id = "severny", title = "Северный округ", scene = "Raid_Severny", minutes = 40, bots = new[] { 5, 9, 14 }, fog = 0.009f,
            desc = "Брошенный городской район: панельки, гаражи, дворы. Карта из Blender (FBX-чанки). Нужна сцена Raid_Severny — меню Severny → 3/4."
        };
        /// <summary>Training ground (main menu → «Обучение»), not listed on the raid setup screen.</summary>
        public static readonly MapDef Tutorial = new MapDef
        {
            id = "tutorial", hidden = true, title = "Обучение", scene = null, minutes = 30, bots = new[] { 0, 0, 0 }, fog = 0.006f,
            desc = "Учебный полигон: движение, лут, стрельба, лечение, бой и выход."
        };
        public static readonly MapDef Polygon = new MapDef
        {
            id = "polygon", title = "ТЦ «Волна»", scene = null, minutes = 40, bots = new[] { 6, 10, 15 }, fog = 0.012f,
            desc = "Большая карта ~470×430 м: ТЦ «Волна» с интерьером (два этажа, магазины, супермаркет, фудкорт, служебные коридоры), " +
                   "жилой квартал, гаражи ГСК «Мотор», котельная, парковка, АЗС с магазином, промзона со складами, Ж/Д ветка. " +
                   "11 точек входа, 7 выходов (2–3 активны). Модели из Blender (Resources/PolygonKit); если набора нет — процедурный полигон."
        };
        static MapDef[] _all;
        /// <summary>Maps shown in the game. In a player build a scene map is listed only when it is in the build and placed
        /// (a build made without the FBX city shows just «ТЦ «Волна»»); the editor always lists everything.</summary>
        public static MapDef[] All
        {
            get
            {
                if (_all != null) return _all;
                var l = new System.Collections.Generic.List<MapDef>();
                foreach (var m in new[] { Severny, Polygon })
                    if (Application.isEditor || (m.Playable && m.Placed)) l.Add(m);
                if (l.Count == 0) l.Add(Polygon);
                return _all = l.ToArray();
            }
        }
        public static MapDef Get(string id)
        {
            if (id == Tutorial.id) return Tutorial;
            foreach (var m in All) if (m.id == id) return m;
            foreach (var m in new[] { Severny, Polygon }) if (m.id == id) return m;
            return Polygon;
        }
    }
}
