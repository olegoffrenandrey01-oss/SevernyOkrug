// All game UI (uGUI, built in code): main menu, raid setup, loading, HUD, inventory/stash/trader, settings,
// base (stats), pause, results, message boxes. Created by GameBootstrap. Partial: UIManager.Menus.cs, UIManager.Hud.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Severny
{
    public enum UIScreen { None, MainMenu, RaidSetup, Loading, Hud, Inventory, Settings, Base, Pause, Results, Lobby, Quests }

    public partial class UIManager : MonoBehaviour
    {
        public static UIManager I;
        public UIScreen Current { get; private set; } = UIScreen.None;

        Canvas _main, _top;
        RectTransform _mainRoot, _topRoot;
        readonly Dictionary<UIScreen, RectTransform> _screens = new Dictionary<UIScreen, RectTransform>();
        InventoryUI _inv;
        UIScreen _settingsReturn = UIScreen.MainMenu;

        // toasts (top canvas): small cards stacked above the bottom edge
        readonly List<Text> _toasts = new List<Text>();
        readonly List<float> _toastUntil = new List<float>();
        readonly List<float> _toastBorn = new List<float>();
        readonly List<RectTransform> _toastCards = new List<RectTransform>();
        readonly List<CanvasGroup> _toastCg = new List<CanvasGroup>();
        readonly List<string> _toastShown = new List<string>();

        public static UIManager Create()
        {
            if (I != null) return I;
            var go = new GameObject("UI");
            if (GameBootstrap.I != null) go.transform.SetParent(GameBootstrap.I.transform, false);
            return go.AddComponent<UIManager>();
        }

        void Awake()
        {
            I = this;
            EnsureEventSystem();
            _main = UIFactory.CreateCanvas(transform, "Canvas_Main", 10);
            _top = UIFactory.CreateCanvas(transform, "Canvas_Top", 60);
            _mainRoot = (RectTransform)_main.transform;
            _topRoot = (RectTransform)_top.transform;
            BuildMainMenu();
            BuildRaidSetup();
            BuildLoading();
            BuildHud();
            _inv = new InventoryUI(this, _mainRoot);
            _screens[UIScreen.Inventory] = _inv.root;
            BuildSettings();
            BuildBase();
            BuildQuests();
            BuildPause();
            BuildResults();
            BuildLobby();
            BuildModal();
            var toastRoot = UIFactory.Node(_topRoot, "Toasts"); UIFactory.PlaceAt(toastRoot, 0.5f, 1f, 0, 0, 1920, 1080);
            for (int i = 0; i < 4; i++)
            {
                var card = UIFactory.Node(toastRoot, "Toast" + i);
                var cg = card.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;
                var sh = UIFactory.SetSoft(UIFactory.Sliced(card, "shadow", UIAssets.Glow, UITheme.A(Color.black, 0.55f)), "glow", UITheme.A(Color.black, 0.55f)); UIFactory.Stretch(sh.rectTransform, -16, -14, -16, -18);
                var bg = UIFactory.Img(card, "bg", UITheme.A(UITheme.Bg1, 0.95f)); UIFactory.Stretch(bg.rectTransform);
                UIFactory.Border(card, UITheme.Line1);
                var bar = UIFactory.Img(card, "bar", UITheme.Amber); var brt = bar.rectTransform;
                brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 0.5f); brt.sizeDelta = new Vector2(3, 0); brt.anchoredPosition = Vector2.zero;
                UIFactory.Icon(card, "info", 16, 11, 18, UITheme.Amber, "icon");
                var t = UIFactory.T(card, "", 44, 0, 1000, 40, UITheme.BodyMedium, 18, UITheme.Text0, TextAnchor.MiddleLeft, "text");
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                _toasts.Add(t); _toastUntil.Add(0f); _toastBorn.Add(-10f); _toastCards.Add(card); _toastCg.Add(cg); _toastShown.Add(null);
            }
            foreach (var kv in _screens) kv.Value.gameObject.SetActive(false);
        }

        void OnDestroy() { if (_lbSubscribed) NetSession.Changed -= OnNetChanged; if (I == this) I = null; }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            DontDestroyOnLoad(go);
        }

        RectTransform NewScreen(UIScreen s, bool fade = true)
        {
            var rt = UIFactory.Stretch(UIFactory.Node(_mainRoot, "Screen_" + s));
            if (fade) rt.gameObject.AddComponent<UIFadeIn>().duration = 0.16f;
            _screens[s] = rt;
            return rt;
        }

        void Show(UIScreen s)
        {
            if (Current == UIScreen.Inventory && s != UIScreen.Inventory) _inv.Close();
            Current = s;
            foreach (var kv in _screens) kv.Value.gameObject.SetActive(kv.Key == s);
            bool gameplay = s == UIScreen.Hud && !AutoBenchmark.Running;
            GameInput.GameplayEnabled = gameplay;
            GameInput.SetCursorLocked(gameplay);
            // LAN: the world keeps running for the other players
            bool paused = InRaid && !NetSession.Active && (s == UIScreen.Pause || s == UIScreen.Quests || (s == UIScreen.Settings && _settingsReturn == UIScreen.Pause));
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;
            if (s == UIScreen.Hud) Hud.Prompt(null);
        }

        bool InRaid => GameBootstrap.I != null && GameBootstrap.I.InRaid && RaidManager.I != null;

        // ------------------------------------------------------------------ public API
        public void ShowMainMenu()
        {
            if (NetSession.InLobby) { OpenLobby(); return; }       // LAN: back to the lobby after a raid
            RefreshMainMenu(); Show(UIScreen.MainMenu); MaybeOfferTutorial();
        }

        public void ShowHud() { Show(UIScreen.Hud); }

        /// <summary>The raid container open in the inventory right now (LAN sync of its contents).</summary>
        public LootContainer OpenContainer => Current == UIScreen.Inventory && _inv != null && _inv.mode == InventoryUI.Mode.Raid ? _inv.container : null;

        public void OpenInventory(LootContainer c)
        {
            if (!InRaid || !RaidManager.I.Running) return;
            _inv.Open(InventoryUI.Mode.Raid, c);
            Show(UIScreen.Inventory);
            if (c == null) SFX.Play2D(SFX.Rustle, 0.5f);   // containers voice their own opening
        }

        public void OpenStash(bool trader)
        {
            _inv.Open(trader ? InventoryUI.Mode.Trader : InventoryUI.Mode.Stash, null);
            Show(UIScreen.Inventory);
        }

        public void CloseInventory()
        {
            if (Current != UIScreen.Inventory) return;
            bool raid = _inv.mode == InventoryUI.Mode.Raid;
            _inv.Close();
            if (raid && InRaid) Show(UIScreen.Hud); else ShowMainMenu();
        }

        public void Toast(string s, float dur = 2.2f)
        {
            if (string.IsNullOrEmpty(s)) return;
            for (int i = _toasts.Count - 1; i > 0; i--) { _toasts[i].text = _toasts[i - 1].text; _toastUntil[i] = _toastUntil[i - 1]; _toastBorn[i] = _toastBorn[i - 1]; }
            _toasts[0].text = s; _toastUntil[0] = Time.unscaledTime + dur; _toastBorn[0] = Time.unscaledTime;
        }

        /// <summary>Toast cards: fit the width to the text, stack upwards from the bottom, slide in, fade out.</summary>
        void UpdateToasts()
        {
            float now = Time.unscaledTime, y = 0f;
            for (int i = 0; i < _toasts.Count; i++)
            {
                float a = Mathf.Clamp01((_toastUntil[i] - now) / 0.4f);
                if (a <= 0f || string.IsNullOrEmpty(_toasts[i].text)) { if (_toastCg[i].alpha != 0f) _toastCg[i].alpha = 0f; continue; }
                if (!ReferenceEquals(_toastShown[i], _toasts[i].text))
                {
                    _toastShown[i] = _toasts[i].text;
                    float w = Mathf.Clamp(UIFactory.TextWidth(_toasts[i]) + 64f, 280f, 1200f);
                    _toastCards[i].sizeDelta = new Vector2(w, 40f);
                    _toasts[i].rectTransform.sizeDelta = new Vector2(w - 56f, 40f);
                }
                float k = Mathf.Clamp01((now - _toastBorn[i]) / 0.18f); k = 1f - (1f - k) * (1f - k);
                var card = _toastCards[i];
                card.anchorMin = card.anchorMax = new Vector2(0.5f, 0f); card.pivot = new Vector2(0.5f, 0f);
                card.anchoredPosition = new Vector2(0f, 118f + y - 14f * (1f - k));
                _toastCg[i].alpha = a * k;
                y += 48f;
            }
        }

        // ------------------------------------------------------------------ input routing
        void Update()
        {
            UpdateToasts();
            if (_modalOpen) { if (GameInput.Down(GKey.Pause)) CloseModal(false); return; }

            if (GameInput.Down(GKey.Journal) && (Current == UIScreen.MainMenu || Current == UIScreen.Base || Current == UIScreen.Hud))
            { OpenQuests(); return; }
            switch (Current)
            {
                case UIScreen.Quests:
                    if (GameInput.Down(GKey.Pause) || GameInput.Down(GKey.Journal)) CloseQuests();
                    else RefreshQuests();
                    break;
                case UIScreen.Hud:
                    UpdateHud();
                    if (!InRaid || AutoBenchmark.Running) break;
                    if (!RaidManager.I.Running) break;
                    if (GameInput.Down(GKey.Pause)) { ShowPause(); break; }
                    if (GameInput.Down(GKey.Inventory)) { OpenInventory(null); break; }
                    break;
                case UIScreen.Inventory:
                    _inv.Tick();
                    if (GameInput.Down(GKey.Inventory) || GameInput.Down(GKey.Pause)) CloseInventory();
                    if (_inv.mode == InventoryUI.Mode.Raid && (!InRaid || !RaidManager.I.Running)) CloseInventory();
                    break;
                case UIScreen.Pause:
                    if (GameInput.Down(GKey.Pause)) ResumeFromPause();
                    break;
                case UIScreen.Settings:
                    if (GameInput.Down(GKey.Pause)) CloseSettings();
                    break;
                case UIScreen.RaidSetup:
                case UIScreen.Base:
                    if (GameInput.Down(GKey.Pause)) ShowMainMenu();
                    break;
                case UIScreen.MainMenu:
                    if (GameInput.Down(GKey.Benchmark)) StartBenchmark();
                    else if (GameInput.Down(GKey.Shots)) StartShots();
                    break;
                case UIScreen.Lobby:
                    TickLobby();
                    break;
                case UIScreen.Loading:
                    UpdateLoading();
                    break;
            }
        }
    }
}
