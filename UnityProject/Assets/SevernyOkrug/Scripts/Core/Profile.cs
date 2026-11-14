// Player profile + JSON save (Application.persistentDataPath/severny_profile.json).
using System;
using System.IO;
using UnityEngine;

namespace Severny
{
    [Serializable]
    public class PlayerProfile
    {
        public int version = 1;
        public string nickname = "Выживший";
        public int roubles = 150000;
        public int xp;
        public int raids, survived, kia, mia, kills;
        public long lootSoldTotal;
        public bool tutorialDone, tutorialOffered;
        public InventoryGrid stash = new InventoryGrid("Схрон", 10, 40);
        public Loadout loadout = new Loadout();

        public int Level => 1 + Mathf.FloorToInt(Mathf.Sqrt(xp / 1000f));

        public static PlayerProfile CreateNew()
        {
            var p = new PlayerProfile();
            var L = p.loadout;
            var rifle = ItemInstance.Create("av74"); rifle.ammo = 30; rifle.ammoId = "a545"; rifle.chamber = 1; L.primary = rifle;
            var pistol = ItemInstance.Create("p9m"); pistol.ammo = 8; pistol.ammoId = "a918"; pistol.chamber = 1; L.holster = pistol;
            L.armor = ItemInstance.Create("vest3");
            for (int i = 0; i < 2; i++) { var m = ItemInstance.Create("mag_av74"); m.ammo = 30; m.ammoId = "a545"; L.rig.AutoPlace(m); }
            { var m = ItemInstance.Create("mag_p9m"); m.ammo = 8; m.ammoId = "a918"; L.rig.AutoPlace(m); }
            L.rig.AutoPlace(ItemInstance.Create("a545", 60));
            L.pockets.AutoPlace(ItemInstance.Create("bandage"));
            L.pockets.AutoPlace(ItemInstance.Create("bandage"));
            L.pockets.AutoPlace(ItemInstance.Create("medkit"));
            L.secure.AutoPlace(ItemInstance.Create("painkill"));
            // stash starter kit
            p.stash.AutoPlace(ItemInstance.Create("av74"));
            p.stash.AutoPlace(ItemInstance.Create("p9m"));
            for (int i = 0; i < 3; i++) p.stash.AutoPlace(ItemInstance.Create("mag_av74"));
            for (int i = 0; i < 2; i++) p.stash.AutoPlace(ItemInstance.Create("mag_p9m"));
            p.stash.AutoPlace(ItemInstance.Create("a545", 60)); p.stash.AutoPlace(ItemInstance.Create("a545", 60));
            p.stash.AutoPlace(ItemInstance.Create("a918", 50));
            p.stash.AutoPlace(ItemInstance.Create("helm2"));
            for (int i = 0; i < 3; i++) p.stash.AutoPlace(ItemInstance.Create("bandage"));
            p.stash.AutoPlace(ItemInstance.Create("medkit"));
            p.stash.AutoPlace(ItemInstance.Create("bigkit"));
            return p;
        }

        public void FixupAfterLoad()
        {
            if (stash == null) stash = new InventoryGrid("Схрон", 10, 40);
            stash.items.RemoveAll(i => ItemInstance.IsEmpty(i));
            if (loadout == null) loadout = new Loadout();
            loadout.FixupAfterLoad();
        }

        /// <summary>Gives a minimal free kit if the player has no weapon at all (prevents soft-lock).</summary>
        public bool GiveInsuranceKitIfBroke()
        {
            bool hasWeapon = loadout.Get(EquipSlot.Primary) != null || loadout.Get(EquipSlot.Holster) != null;
            foreach (var i in stash.items) if (i.Def != null && i.Def.type == ItemType.Weapon) hasWeapon = true;
            if (hasWeapon) return false;
            var pistol = ItemInstance.Create("p9m"); pistol.ammo = 8; pistol.ammoId = "a918"; pistol.chamber = 1;
            loadout.holster = pistol;
            var m = ItemInstance.Create("mag_p9m"); m.ammo = 8; m.ammoId = "a918"; loadout.pockets.AutoPlace(m);
            loadout.pockets.AutoPlace(ItemInstance.Create("a918", 24));
            loadout.pockets.AutoPlace(ItemInstance.Create("bandage"));
            return true;
        }
    }

    public static class SaveSystem
    {
        public static string PathFile => Path.Combine(Application.persistentDataPath, Slot <= 1 ? "severny_profile.json" : "severny_profile_" + Slot + ".json");

        // A second copy of the game on the same PC (LAN test) gets its own profile file instead of overwriting the first one.
        // The first free lock file wins (released by the OS when the game exits). "-profile N" forces a slot. Editor: locks only in Play Mode.
        static int _slot;
        static FileStream _lock;
        public static int Slot { get { if (_slot <= 0) PickSlot(); return _slot; } }

        static void PickSlot()
        {
            int forced = 0;
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-profile") int.TryParse(args[i + 1], out forced);
            }
            catch (Exception) { }
            if (forced >= 1) { _slot = Mathf.Clamp(forced, 1, 9); TryLock(_slot); return; }
#if UNITY_EDITOR
            if (!Application.isPlaying) { _slot = 1; return; }                 // edit mode never holds the lock
#endif
            for (int s = 1; s <= 4; s++) if (TryLock(s)) { _slot = s; if (s > 1) Debug.Log("[Severny] Another copy of the game is running: using profile slot " + s); return; }
            _slot = 1;
        }

        static void ReleaseLock() { try { _lock?.Dispose(); } catch (Exception) { } _lock = null; }

#if UNITY_EDITOR
        // Editor: the lock is held only during Play Mode (a build started meanwhile gets profile 2), released before domain reloads.
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorHooks()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseLock;
            UnityEditor.EditorApplication.quitting += ReleaseLock;
            UnityEditor.EditorApplication.playModeStateChanged += st =>
            {
                if (st == UnityEditor.PlayModeStateChange.ExitingEditMode || st == UnityEditor.PlayModeStateChange.EnteredEditMode)
                {
                    ReleaseLock();
                    _slot = 0; _current = null;
                }
            };
        }
#endif

        static bool TryLock(int s)
        {
            try
            {
                string p = Path.Combine(Application.persistentDataPath, s <= 1 ? "severny_profile.lock" : "severny_profile_" + s + ".lock");
                ReleaseLock();
                _lock = new FileStream(p, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (Exception) { return false; }
        }
        static PlayerProfile _current;
        public static bool Loaded => _current != null;

        public static PlayerProfile Current
        {
            get { if (_current == null) _current = Load(); return _current; }
        }

        public static PlayerProfile Load()
        {
            try
            {
                if (File.Exists(PathFile))
                {
                    var p = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(PathFile));
                    if (p != null) { p.FixupAfterLoad(); return p; }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Severny] Profile load failed, creating new: " + e.Message);
                try { File.Copy(PathFile, PathFile + ".broken_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak", true); } catch { }
            }
            var n = PlayerProfile.CreateNew();
            _current = n;
            Save();
            return n;
        }

        public static void Save()
        {
            if (_current == null) return;
            try
            {
                string tmp = PathFile + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(_current, true));
                if (File.Exists(PathFile)) File.Delete(PathFile);
                File.Move(tmp, PathFile);
            }
            catch (Exception e) { Debug.LogError("[Severny] Profile save failed: " + e.Message); }
        }

        public static void ResetProfile()
        {
            _current = PlayerProfile.CreateNew();
            Save();
        }
    }

    public static class GameSettings
    {
        public static float Sensitivity { get => PlayerPrefs.GetFloat("sev_sens", 1f); set => PlayerPrefs.SetFloat("sev_sens", value); }
        public static float Fov { get => PlayerPrefs.GetFloat("sev_fov", 72f); set => PlayerPrefs.SetFloat("sev_fov", value); }
        public static float Volume { get => PlayerPrefs.GetFloat("sev_vol", 0.8f); set => PlayerPrefs.SetFloat("sev_vol", value); }
        public static bool VSync { get => PlayerPrefs.GetInt("sev_vsync", 1) == 1; set => PlayerPrefs.SetInt("sev_vsync", value ? 1 : 0); }
        public static int FpsCap { get => PlayerPrefs.GetInt("sev_fpscap", 0); set => PlayerPrefs.SetInt("sev_fpscap", value); }
        public static bool ShowFps { get => PlayerPrefs.GetInt("sev_showfps", 0) == 1; set => PlayerPrefs.SetInt("sev_showfps", value ? 1 : 0); }
        public static bool InvertY { get => PlayerPrefs.GetInt("sev_invy", 0) == 1; set => PlayerPrefs.SetInt("sev_invy", value ? 1 : 0); }
        public static int BotAmount { get => PlayerPrefs.GetInt("sev_bots", 1); set => PlayerPrefs.SetInt("sev_bots", value); } // 0 few,1 normal,2 many
        public static int TimeOfDay { get => Mathf.Clamp(PlayerPrefs.GetInt("sev_tod", 1), 0, 3); set => PlayerPrefs.SetInt("sev_tod", value); } // 0 morning,1 day,2 evening,3 night
        public static bool Winter { get => PlayerPrefs.GetInt("sev_winter", 0) == 1; set => PlayerPrefs.SetInt("sev_winter", value ? 1 : 0); }
        public static int Weather { get => Mathf.Clamp(PlayerPrefs.GetInt("sev_weather", 0), 0, RaidOptions.WeatherMax); set => PlayerPrefs.SetInt("sev_weather", value); } // 0 random,1 clear,2 rain/snow,3 fog,4 after rain (v13)
        static string NickKey => SaveSystem.Slot > 1 ? "sev_nick_" + SaveSystem.Slot : "sev_nick";
        public static string Nick { get => PlayerPrefs.GetString(NickKey, ""); set => PlayerPrefs.SetString(NickKey, value); }
        public static string LastJoinIp { get => PlayerPrefs.GetString("sev_joinip", "192.168.1."); set => PlayerPrefs.SetString("sev_joinip", value); }
        public static float RenderScale { get => PlayerPrefs.GetFloat("sev_rscale", 1f); set => PlayerPrefs.SetFloat("sev_rscale", value); }
        public static int GraphicsPreset { get => Mathf.Clamp(PlayerPrefs.GetInt("sev_gfxq", 2), 0, 3); set => PlayerPrefs.SetInt("sev_gfxq", value); } // v12: 0 low,1 medium,2 high,3 ultra
        public static int AntiAliasing { get => Mathf.Clamp(PlayerPrefs.GetInt("sev_aa", 2), 0, 3); set => PlayerPrefs.SetInt("sev_aa", value); }     // v12: 0 off,1 FXAA,2 SMAA,3 TAA

        public static void Apply()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FpsCap <= 0 ? -1 : FpsCap;
            AudioListener.volume = Volume;
            GfxQuality.Apply();   // v12: graphics preset, anti-aliasing, render scale (+FSR) — before v12 the scale was saved but not applied
            PlayerPrefs.Save();
        }
    }
}
