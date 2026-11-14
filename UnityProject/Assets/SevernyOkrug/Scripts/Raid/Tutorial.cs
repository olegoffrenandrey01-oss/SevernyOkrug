// «Обучение»: a short training course. The player is dropped in naked (temporary loadout, the profile is not touched)
// and learns step by step: moving / sprint / crouch / jump, searching a crate and equipping gear (inventory),
// shooting (range with 3 targets), bleeding + quick heal, a fight with one weak marauder, looting the body, extraction.
// First completion gives a starter kit and money. Started from the main menu (also offered on the first launch).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace Severny
{
    public static class TutorialMap
    {
        public static readonly Vector3 SpawnPos = new Vector3(0f, 0f, 3f);
        public static readonly Vector3 CratePos = new Vector3(0f, 0f, 65f);
        public static readonly Vector3 ExitPos = new Vector3(0f, 0f, 186f);
        public static readonly Vector3 BotPos = new Vector3(-12f, 0f, 160f);
        public static readonly Vector3[] TargetPos = { new Vector3(-4f, 0f, 88f), new Vector3(3.5f, 0f, 98f), new Vector3(-1f, 0f, 108f) };
        /// <summary>Barbed wire across the east corridor (x 8.6..21.6, z 90..93).</summary>
        public static readonly Bounds WireZone = new Bounds(new Vector3(15.1f, 0.5f, 91.5f), new Vector3(13f, 2f, 3f));
        public static readonly List<TutorialTarget> Targets = new List<TutorialTarget>();
        public static LootContainer Crate;

        public static Transform Build(Transform parent, out Bounds bounds)
        {
            Targets.Clear();
            var root = new GameObject("Tutorial_Range").transform;
            root.SetParent(parent, false);
            var ground = GameAssets.Lit(new Color(0.29f, 0.3f, 0.26f), 0.1f);
            var concrete = GameAssets.Lit(new Color(0.46f, 0.46f, 0.43f), 0.15f);
            var dark = GameAssets.Lit(new Color(0.25f, 0.26f, 0.25f), 0.2f);
            var wood = GameAssets.Lit(new Color(0.42f, 0.31f, 0.19f), 0.12f);
            var pipe = GameAssets.Lit(new Color(0.32f, 0.22f, 0.15f), 0.35f, 0.6f);
            var metal = GameAssets.Lit(new Color(0.2f, 0.21f, 0.22f), 0.4f, 0.7f);
            var yellow = GameAssets.Emissive(new Color(1f, 0.78f, 0.2f), 1.6f);
            var sand = GameAssets.Lit(new Color(0.42f, 0.38f, 0.3f), 0.05f);
            Prim.Box(root, new Vector3(0f, -0.5f, 95f), new Vector3(46f, 1f, 214f), ground, true, null, "ground");
            // perimeter
            W(root, -23f, 95f, 1f, 214f, 4f, concrete); W(root, 23f, 95f, 1f, 214f, 4f, concrete);
            W(root, 0f, -12f, 46f, 1f, 4f, concrete); W(root, 0f, 202f, 46f, 1f, 4f, concrete);
            // 1) sprint lane marker
            Prim.Box(root, new Vector3(0f, 0.01f, 38f), new Vector3(10f, 0.02f, 0.4f), yellow, false, null, "Marker");
            // 2) crouch tunnel: low pipes x -3..3, z 46..50 (only way through)
            W(root, -13f, 48f, 20f, 4f, 3.2f, concrete); W(root, 13f, 48f, 20f, 4f, 3.2f, concrete);
            Prim.Box(root, new Vector3(0f, 2.3f, 48f), new Vector3(6.4f, 1.9f, 4f), concrete, true, null, "TunnelTop");
            for (int i = 0; i < 4; i++) Prim.Cyl(root, new Vector3(0f, 1.52f, 46.4f + i * 1.05f), 0.32f, 6f, pipe, true, Quaternion.Euler(0f, 0f, 90f), "Pipe");
            Prim.Box(root, new Vector3(0f, 0.01f, 44.6f), new Vector3(6f, 0.02f, 0.3f), yellow, false, null, "Marker");
            // 3) low wall 0.7 m across the whole width — jump
            W(root, 0f, 56f, 46f, 0.4f, 0.7f, dark);
            // 4) gear crate
            Prim.Box(root, new Vector3(0f, 0.01f, 61.5f), new Vector3(6f, 0.02f, 0.3f), yellow, false, null, "Marker");
            var lc = LootContainer.Create(ItemDB.LootTable.WeaponCrate, CratePos, Quaternion.Euler(0f, 180f, 0f), root, 1);
            lc.title = "Ящик со снаряжением";
            lc.grid = new InventoryGrid("Снаряжение", 8, 6);
            lc.prefilled = true;
            lc.searchTimePerItem = 0.35f;
            var rifle = ItemInstance.Create("av74"); rifle.ammo = 30; rifle.ammoId = "a545"; rifle.chamber = 1;
            lc.grid.AutoPlace(rifle);
            lc.grid.AutoPlace(ItemInstance.Create("vest3"));
            for (int i = 0; i < 2; i++) { var m = ItemInstance.Create("mag_av74"); m.ammo = 30; m.ammoId = "a545"; lc.grid.AutoPlace(m); }
            lc.grid.AutoPlace(ItemInstance.Create("a545", 60));
            lc.grid.AutoPlace(ItemInstance.Create("bandage")); lc.grid.AutoPlace(ItemInstance.Create("bandage"));
            lc.grid.AutoPlace(ItemInstance.Create("medkit"));
            Crate = lc;
            // 5) shooting range in the middle (x -8.5..8.5, z 72..118), corridor on the east side (x 8.6..22)
            W(root, -15.75f, 72f, 14.5f, 1f, 3f, concrete);                     // west part closed
            W(root, 8.6f, 97f, 0.5f, 50f, 3f, concrete);                         // range | corridor
            W(root, -8.6f, 97f, 0.5f, 50f, 3f, concrete);                        // range | west
            W(root, -15.75f, 121.5f, 14.5f, 1f, 3f, concrete);                   // west dead end closed from the arena
            Prim.Box(root, new Vector3(0f, 2.5f, 118.5f), new Vector3(17f, 5f, 2f), sand, true, null, "Backstop");
            Prim.Box(root, new Vector3(0f, 0.45f, 73.2f), new Vector3(6f, 0.9f, 0.8f), wood, true, null, "FiringTable");
            Prim.Box(root, new Vector3(0f, 0.01f, 74.2f), new Vector3(16f, 0.02f, 0.15f), yellow, false, null, "FiringLine");
            foreach (var tp in TargetPos) Targets.Add(TutorialTarget.Create(root, tp, metal, wood));
            // 6) barbed wire across the corridor
            for (int i = 0; i < 4; i++)
            {
                float x = 9.2f + i * 4.1f;
                Prim.Box(root, new Vector3(x, 0.55f, WireZone.min.z + 0.2f), new Vector3(0.08f, 1.1f, 0.08f), wood, false, null, "WirePost");
                Prim.Box(root, new Vector3(x, 0.55f, WireZone.max.z - 0.2f), new Vector3(0.08f, 1.1f, 0.08f), wood, false, null, "WirePost");
            }
            for (int r = 0; r < 5; r++)
                for (int h = 0; h < 3; h++)
                    Prim.Cyl(root, new Vector3(15.1f, 0.25f + h * 0.32f, WireZone.min.z + 0.25f + r * 0.62f), 0.014f, 13f, metal, false,
                             Quaternion.Euler(0f, (r + h) % 2 == 0 ? 2f : -2f, 90f), "Wire");
            Prim.Box(root, new Vector3(15.3f, 0.01f, 88.5f), new Vector3(12.8f, 0.02f, 0.3f), GameAssets.Emissive(new Color(1f, 0.25f, 0.15f), 1.6f), false, null, "Marker");
            // 7) arena z 122..176 with cover
            Prim.Box(root, new Vector3(15.3f, 0.01f, 121f), new Vector3(12.8f, 0.02f, 0.3f), yellow, false, null, "Marker");
            var rng = new System.Random(77);
            var covers = new[] { new Vector3(12f, 0f, 132f), new Vector3(4f, 0f, 138f), new Vector3(-6f, 0f, 134f), new Vector3(15f, 0f, 146f), new Vector3(-2f, 0f, 150f),
                                 new Vector3(-14f, 0f, 145f), new Vector3(7f, 0f, 157f), new Vector3(-9f, 0f, 165f), new Vector3(14f, 0f, 167f), new Vector3(-17f, 0f, 156f) };
            for (int i = 0; i < covers.Length; i++)
            {
                bool tall = i % 3 == 1;
                var size = tall ? new Vector3(1.4f, 2.1f, 1.4f) : new Vector3(3f, 1.15f, 0.9f);
                Prim.Box(root, covers[i] + Vector3.up * size.y * 0.5f, size, i % 2 == 0 ? concrete : wood, true, Quaternion.Euler(0f, rng.Next(-25, 25), 0f), "Cover");
            }
            W(root, 0f, 177f, 30f, 1f, 2.6f, dark);                                // wall before the exit, open on both sides
            Prim.Box(root, new Vector3(0f, 0.01f, 181f), new Vector3(10f, 0.02f, 0.3f), GameAssets.Emissive(new Color(0.3f, 1f, 0.35f), 1.6f), false, null, "Marker");
            // a couple of lamps so the course is readable at any time
            foreach (var lp in new[] { new Vector3(0f, 5f, 20f), new Vector3(0f, 5f, 65f), new Vector3(0f, 5f, 95f), new Vector3(15f, 5f, 100f), new Vector3(0f, 5f, 150f), new Vector3(0f, 5f, 186f) })
            {
                var lg = new GameObject("Lamp"); lg.transform.SetParent(root, false); lg.transform.localPosition = lp;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 22f; l.intensity = 1.2f; l.color = new Color(1f, 0.92f, 0.8f); l.shadows = LightShadows.None;
            }
            bounds = new Bounds(new Vector3(0f, 2f, 95f), new Vector3(46f, 12f, 214f));
            return root;
        }

        static void W(Transform root, float x, float z, float sx, float sz, float h, Material m) =>
            Prim.Box(root, new Vector3(x, h * 0.5f, z), new Vector3(sx, h, sz), m, true, null, "Wall");
    }

    /// <summary>Steel plate target on a post: falls back when hit hard enough.</summary>
    public class TutorialTarget : MonoBehaviour
    {
        public Health health;
        public bool Down { get; private set; }
        Transform _pivot; float _fall;

        public static TutorialTarget Create(Transform parent, Vector3 pos, Material plate, Material post)
        {
            var g = new GameObject("Target");
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var t = g.AddComponent<TutorialTarget>();
            t.health = g.AddComponent<Health>();
            t.health.max = new[] { 15f, 30f, 30f, 30f, 30f, 30f, 30f };
            t.health.ResetFull();
            Prim.Box(g.transform, new Vector3(0f, 0.25f, 0f), new Vector3(0.9f, 0.5f, 0.5f), post, true);
            t._pivot = new GameObject("Pivot").transform; t._pivot.SetParent(g.transform, false); t._pivot.localPosition = new Vector3(0f, 0.5f, 0f);
            var body = Prim.Box(t._pivot, new Vector3(0f, 0.6f, 0f), new Vector3(0.55f, 0.75f, 0.04f), plate, true, null, "Plate");
            var head = Prim.Box(t._pivot, new Vector3(0f, 1.15f, 0f), new Vector3(0.26f, 0.3f, 0.04f), plate, true, null, "Head");
            var hb = body.AddComponent<Hitbox>(); hb.owner = t.health; hb.part = BodyPart.Thorax;
            var hh = head.AddComponent<Hitbox>(); hh.owner = t.health; hh.part = BodyPart.Head;
            var stripe = GameAssets.Emissive(new Color(1f, 0.3f, 0.2f), 1.2f);
            Prim.Box(t._pivot, new Vector3(0f, 0.65f, -0.03f), new Vector3(0.2f, 0.2f, 0.01f), stripe, false, null, "Bull");
            t.health.Died += d => t.Fall();
            t.health.Damaged += d => SFX.PlayAt(SFX.Pick(SFX.MetalHits, SFX.Impact), t.transform.position + Vector3.up, 0.8f, 3f, 120f, 0.1f);
            return t;
        }

        void Fall() { Down = true; foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false; }

        void Update()
        {
            if (!Down || _fall >= 1f) return;
            _fall = Mathf.Min(1f, _fall + Time.deltaTime * 3f);
            _pivot.localRotation = Quaternion.Euler(-85f * _fall * _fall, 0f, 0f);
        }
    }

    /// <summary>Runs the training steps (component on the RaidManager object of a tutorial raid).</summary>
    public class TutorialDirector : MonoBehaviour
    {
        class Step { public string title, text; public Func<bool> done; public Action start; }

        readonly List<Step> _steps = new List<Step>();
        int _i = -1;
        float _stepT, _wireHurtAt = -1f, _doneT = -1f;
        bool _wired;
        BotController _bot;
        Canvas _canvas;
        RectTransform _panel, _segRoot, _doneChip;
        Text _hdr, _title, _body, _stepNo;
        Image[] _segs;
        UIFadeIn _fade;
        float _panelW = -1f, _panelH = 196f;
        int _layoutMode = -1; bool _shownDone;
        const float HudW = 640f, InvW = 712f;

        PlayerController P => PlayerController.Local;
        Vector3 Pos => P != null ? P.transform.position : Vector3.zero;

        void Start()
        {
            var L = P != null ? P.loadout : null;
            Add("Движение", "WASD — идти, мышь — осматриваться.\nShift — бег (расходует выносливость, синяя полоса).\nДойдите до жёлтой линии впереди.", () => Pos.z > 38.5f);
            Add("Присед", "C — присесть / встать. Под трубами не пройти в полный рост —\nприсядьте и проползите. Сидя вы тише и меньше заметны.", () => Pos.z > 50.6f);
            Add("Прыжок", "Пробел — прыжок. Перепрыгните низкую стенку.", () => Pos.z > 57f);
            Add("Обыск", "Вы без снаряжения — его нужно найти. Подойдите к ящику и нажмите F.\nПредметы в контейнерах открываются не сразу: подождите, пока закончится поиск.",
                () => TutorialMap.Crate != null && TutorialMap.Crate.opened);
            Add("Снаряжение", "Ctrl+клик по предмету — быстро забрать или надеть его.\nМожно и перетаскивать мышью (R — повернуть). ПКМ — использовать.\nНаденьте автомат и бронежилет, возьмите магазины и медикаменты. Tab — закрыть.",
                () => L != null && L.Get(EquipSlot.Primary) != null && L.Get(EquipSlot.Armor) != null && MagsCarried(L) >= 1 && UIManager.I != null && UIManager.I.Current == UIScreen.Hud);
            Add("Стрельбище", "ЛКМ — огонь, ПКМ — прицел, R — перезарядка, B — режим огня, T — проверить магазин.\nВстаньте у стола и поразите все три мишени (15, 25 и 35 м).",
                () => TutorialMap.Targets.Count > 0 && TutorialMap.Targets.TrueForAll(t => t == null || t.Down));
            Add("Колючая проволока", "Дальше — по правому коридору. Впереди колючая проволока:\nпройти можно, но вы пораните ногу.", () => _wired);
            Add("Кровотечение", "Вы ранены и теряете кровь (капля у здоровья внизу экрана).\nH — быстрое лечение: бинт останавливает кровотечение, аптечка восстанавливает здоровье.\nТо же самое — ПКМ по медикаменту в инвентаре.",
                () => P != null && !P.health.AnyBleeding && Time.time - _wireHurtAt > 1f);
            Add("Бой", "Впереди, на площадке, — мародёр с пистолетом. Используйте укрытия,\nQ / E — выглянуть из-за угла, C — присесть за укрытием. Уничтожьте противника.",
                () => _bot != null && _bot.IsDead, SpawnBot);
            Add("Трофеи", "Обыщите тело (F) и заберите полезное: патроны, медикаменты, ценности.\nВсё, что вынесете из рейда, останется у вас.",
                () => { var c = _bot != null ? _bot.GetComponent<LootContainer>() : null; return c != null && c.opened && UIManager.I.Current == UIScreen.Hud; });
            Add("Выход", "В каждом рейде есть 2–3 выхода, список — клавиша O.\nИдите к зелёному маяку за стеной и постойте в зоне, пока не закончится отсчёт.", () => false, () =>
            {
                var e = ExtractionPoint.Create("Выход с полигона", TutorialMap.ExitPos, RaidManager.I.runtimeRoot);
                e.duration = 4f;
                RaidManager.I.exits.Add(e);
            });
            BuildUI();
            Next();
        }

        static int MagsCarried(Loadout L)
        {
            int n = 0;
            foreach (var g in L.QuickGrids()) foreach (var it in g.items) if (it.Def != null && it.Def.type == ItemType.Magazine) n++;
            return n;
        }

        void Add(string title, string text, Func<bool> done, Action start = null) => _steps.Add(new Step { title = title, text = text, done = done, start = start });

        void Next()
        {
            _i++;
            _stepT = 0f;
            if (_i >= _steps.Count) return;
            var s = _steps[_i];
            s.start?.Invoke();
            ShowStep();
            if (_i > 0) SFX.Play2D(SFX.Beep, 0.35f);
        }

        void SpawnBot()
        {
            var pos = TutorialMap.BotPos;
            if (NavMesh.SamplePosition(pos, out var h, 4f, NavMesh.AllAreas)) pos = h.position;
            _bot = BotController.Spawn(pos, RaidManager.I.runtimeRoot, 0.32f, new System.Random(1234), "p9m");
            _bot.patrolRadius = 7f; _bot.visionRange = 45f; _bot.botName = "Мародёр (учебный)";
        }

        void Update()
        {
            if (P == null || _i >= _steps.Count || RaidManager.I == null || !RaidManager.I.Running) { Layout(); return; }
            _stepT += Time.deltaTime;
            // barbed wire hurts once while walking through it
            if (!_wired && _i >= 6 && WireHit())
            {
                _wired = true; _wireHurtAt = Time.time;
                P.health.ApplyDamage(new DamageInfo { part = BodyPart.LeftLeg, damage = 7f, point = Pos, dir = Vector3.forward, cause = "wire" });
                P.health.bleeding[(int)BodyPart.LeftLeg] = true;
                Hud.Notify("Колючая проволока: кровотечение!", 2.5f);
            }
            else if (!_wired && _i < 6 && WireHit()) { _wired = true; _wireHurtAt = Time.time; }
            var s = _steps[_i];
            if (_stepT > 0.4f && s.done())
            {
                if (_doneT < 0f) _doneT = Time.time;
                if (Time.time - _doneT > 0.6f) { _doneT = -1f; Next(); }
            }
            Layout();
        }

        bool WireHit()
        {
            var p = Pos;
            return p.x > TutorialMap.WireZone.min.x && p.x < TutorialMap.WireZone.max.x && p.z > TutorialMap.WireZone.min.z + 0.3f && p.z < TutorialMap.WireZone.max.z;
        }

        // ------------------------------------------------------------------ UI: task card (HUD: top-left, inventory: right column)
        void BuildUI()
        {
            _canvas = UIFactory.CreateCanvas(transform, "Canvas_Tutorial", 20);
            var gr = _canvas.GetComponent<GraphicRaycaster>(); if (gr != null) gr.enabled = false;
            _panel = UIFactory.Node(_canvas.transform, "Panel");
            UIFactory.Place(_panel, 24, 24, HudW, _panelH);
            var sh = UIFactory.SetSoft(UIFactory.Sliced(_panel, "shadow", UIAssets.Glow, UITheme.A(Color.black, 0.5f)), "glow", UITheme.A(Color.black, 0.5f)); UIFactory.Stretch(sh.rectTransform, -16, -12, -16, -20);
            var bg = UIFactory.Img(_panel, "bg", UITheme.A(UITheme.Bg1, 0.92f)); UIFactory.Stretch(bg.rectTransform);
            UIFactory.Gradient(_panel, "sheen", 0, 70, UITheme.A(Color.white, 0.03f));
            UIFactory.Border(_panel, UITheme.Line1, "edge");
            var acc = UIFactory.Img(_panel, "accent", UITheme.Amber); var ar = acc.rectTransform;
            ar.anchorMin = Vector2.zero; ar.anchorMax = new Vector2(0f, 1f); ar.pivot = new Vector2(0f, 0.5f); ar.sizeDelta = new Vector2(3f, 0f); ar.anchoredPosition = Vector2.zero;
            UIFactory.Icon(_panel, "info", 22, 14, 14, UITheme.Amber, "icon");
            _hdr = UIFactory.Caps(_panel, "обучение", 42, 8, 220, 26, 12, UITheme.Amber, TextAnchor.MiddleLeft, 2.2f);
            _stepNo = UIFactory.Mono(_panel, "", 0, 8, 10, 26, 13, UITheme.Text2, TextAnchor.MiddleRight);
            _doneChip = UIFactory.Chip(_panel, "выполнено", 0, 10, UITheme.Green, "check", 22, true);
            _doneChip.gameObject.SetActive(false);
            _segRoot = UIFactory.Node(_panel, "segments");
            _title = UIFactory.T(_panel, "", 22, 46, 10, 38, UITheme.Display, 26, UITheme.Text0, TextAnchor.MiddleLeft, "title");
            _body = UIFactory.T(_panel, "", 22, 90, 10, 100, UITheme.Body, 16, UITheme.Text1, TextAnchor.UpperLeft, "body");
            _body.lineSpacing = 1.08f;
            UIFactory.DropShadow(_body, 0.6f, 1f);
            _fade = _panel.gameObject.AddComponent<UIFadeIn>(); _fade.duration = 0.25f; _fade.target = _panel; _fade.slide = 0f;
            Relayout(HudW);
        }

        /// <summary>Step texts: key names (WASD, Shift, F, R, ЛКМ ...) are highlighted.</summary>
        static readonly System.Text.RegularExpressions.Regex KeyRx = new System.Text.RegularExpressions.Regex(
            @"(?<![\p{L}\d])(WASD|Shift|Ctrl\+клик|Ctrl|Tab|ЛКМ|ПКМ|Пробел|[A-Z])(?![\p{L}\d])");

        static string HighlightKeys(string s) => string.IsNullOrEmpty(s) ? "" : KeyRx.Replace(s, m => UITheme.Tint(m.Value, UITheme.AmberHi));

        void ShowStep()
        {
            if (_panel == null || _i >= _steps.Count) return;
            var s = _steps[_i];
            _stepNo.text = "ШАГ " + (_i + 1) + " / " + _steps.Count;
            _title.text = s.title;
            _body.text = HighlightKeys(s.text);
            if (_segs == null || _segs.Length != _steps.Count)
            {
                _segs = new Image[_steps.Count];
                for (int k = 0; k < _segs.Length; k++) _segs[k] = UIFactory.Box(_segRoot, "seg" + k, 0, 0, 10, 3, UITheme.Line1);
            }
            for (int k = 0; k < _segs.Length; k++)
                _segs[k].color = k < _i ? UITheme.Amber : k == _i ? UITheme.AmberHi : UITheme.A(UITheme.Line2, 0.7f);
            _shownDone = false; _doneChip.gameObject.SetActive(false); _stepNo.gameObject.SetActive(true);
            Relayout(_panelW > 0f ? _panelW : HudW);
            if (_fade != null && _panel.gameObject.activeInHierarchy) { _fade.enabled = false; _fade.enabled = true; }
        }

        /// <summary>Width-dependent layout; the height follows the body text.</summary>
        void Relayout(float w)
        {
            _panelW = w;
            float iw = w - 44f;
            UIFactory.Place(_stepNo.rectTransform, w - 22f - 200f, 8, 200, 26);
            float cw = _doneChip.sizeDelta.x; _doneChip.anchoredPosition = new Vector2(w - 20f - cw, -10f);
            UIFactory.Place(_segRoot, 22, 38, iw, 3);
            if (_segs != null && _segs.Length > 0)
            {
                float gap = 4f, sw = (iw - gap * (_segs.Length - 1)) / _segs.Length;
                for (int k = 0; k < _segs.Length; k++) UIFactory.Place(_segs[k].rectTransform, k * (sw + gap), 0, sw, 3);
            }
            UIFactory.Place(_title.rectTransform, 22, 46, iw, 38);
            UIFactory.Place(_body.rectTransform, 22, 90, iw, 200);
            float bh = string.IsNullOrEmpty(_body.text) ? 0f : Mathf.Ceil(_body.preferredHeight);
            UIFactory.Place(_body.rectTransform, 22, 90, iw, bh + 4f);
            _panelH = 90f + bh + 18f;
            _layoutMode = -1;   // re-anchor on the next Layout()
        }

        void Layout()
        {
            if (_panel == null) return;
            bool inv = UIManager.I != null && UIManager.I.Current == UIScreen.Inventory;
            bool show = UIManager.I != null && (UIManager.I.Current == UIScreen.Hud || inv) && _i < _steps.Count;
            if (_panel.gameObject.activeSelf != show) _panel.gameObject.SetActive(show);
            if (!show) return;
            // the step is done: green chip for the short pause before the next one
            bool done = _doneT >= 0f;
            if (done != _shownDone)
            {
                _shownDone = done;
                _doneChip.gameObject.SetActive(done); _stepNo.gameObject.SetActive(!done);
                if (_segs != null && _i < _segs.Length) _segs[_i].color = done ? UITheme.Green : UITheme.AmberHi;
            }
            int mode = inv ? 1 : 0;
            if (mode == _layoutMode) return;
            float w = inv ? InvW : HudW;
            if (!Mathf.Approximately(w, _panelW)) Relayout(w);
            _layoutMode = mode;
            // inventory: right column above the ground drop zone (same centred 1920x1080 frame); HUD: top-left corner
            if (inv) UIFactory.PlaceAt(_panel, 0.5f, 0.5f, 1176, 904f - _panelH, InvW, _panelH);
            else UIFactory.PlaceAt(_panel, 0f, 0f, 24, 24, HudW, _panelH);
        }
    }
}
