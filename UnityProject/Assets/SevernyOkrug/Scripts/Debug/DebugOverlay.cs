// F3: debug overlay (FPS, frame/GPU time, batches, triangles, memory, position, bots, NavTester).
// Settings > «Показывать FPS» shows only the first line.
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;

namespace Severny
{
    public class DebugOverlay : MonoBehaviour
    {
        public static bool Full;
        ProfilerRecorder _batches, _setpass, _tris, _sysMem;
        float _fpsSmooth = 60f, _msSmooth = 16f, _gpuMs;
        readonly FrameTiming[] _ft = new FrameTiming[1];
        GUIStyle _style;
        readonly StringBuilder _sb = new StringBuilder(512);
        float _nextText;
        string _text = "";

        void OnEnable()
        {
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _sysMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        }

        void OnDisable() { _batches.Dispose(); _setpass.Dispose(); _tris.Dispose(); _sysMem.Dispose(); }

        void Update()
        {
            if (GameInput.Down(GKey.DebugOverlay)) Full = !Full;
            float dt = Mathf.Max(1e-5f, Time.unscaledDeltaTime);
            _fpsSmooth = Mathf.Lerp(_fpsSmooth, 1f / dt, 0.05f);
            _msSmooth = Mathf.Lerp(_msSmooth, dt * 1000f, 0.05f);
            if (Full)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _ft) > 0 && _ft[0].gpuFrameTime > 0.0) _gpuMs = Mathf.Lerp(_gpuMs, (float)_ft[0].gpuFrameTime, 0.1f);
            }
            if (Time.unscaledTime < _nextText) return;
            _nextText = Time.unscaledTime + 0.25f;
            _sb.Clear();
            _sb.Append("FPS ").Append(_fpsSmooth.ToString("F0")).Append("  (").Append(_msSmooth.ToString("F1")).Append(" мс)");
            if (Full)
            {
                if (_gpuMs > 0f) _sb.Append("  GPU ").Append(_gpuMs.ToString("F1")).Append(" мс");
                _sb.Append("\nBatches ").Append(_batches.Valid ? _batches.LastValue : 0).Append("  SetPass ").Append(_setpass.Valid ? _setpass.LastValue : 0)
                   .Append("  Tris ").Append(((_tris.Valid ? _tris.LastValue : 0) / 1e6).ToString("F2")).Append("M  RAM ")
                   .Append(((_sysMem.Valid ? _sysMem.LastValue : 0) / (1024 * 1024)).ToString()).Append(" MB");
                var p = PlayerController.Local;
                if (p != null && p.isActiveAndEnabled)
                {
                    var pos = p.transform.position;
                    _sb.Append("\nPos ").Append(pos.ToString("F1")).Append("  скорость ").Append(p.SpeedNow.ToString("F1")).Append(" м/с  вес ").Append(p.CarriedWeight.ToString("F1")).Append(" кг");
                    bool onNav = NavMesh.SamplePosition(pos, out _, 1.5f, NavMesh.AllAreas);
                    _sb.Append(onNav ? "  [NavMesh]" : "  [вне NavMesh]");
                }
                int alive = 0, combat = 0;
                foreach (var b in BotController.All) { if (b.state == BotController.S.Dead) continue; alive++; if (b.state == BotController.S.Combat) combat++; }
                _sb.Append("\nБоты: живых ").Append(alive).Append(", в бою ").Append(combat).Append("  Пули в полёте: ").Append(Ballistics.InFlight);
                if (RaidManager.I != null && RaidManager.I.Running) _sb.Append("  Рейд: ").Append(Mathf.FloorToInt(RaidManager.I.timeLeft / 60f)).Append(" мин");
                if (NavTester.Active) _sb.Append('\n').Append(NavTester.Summary);
                var fp = FPArms.DebugLine();                 // v10.2: first-person arms / weapon state (rig check, holder pose)
                if (fp != null) _sb.Append("\nFP ").Append(fp);
                _sb.Append("\nГрафика «").Append(GfxQuality.Names[GfxQuality.Level]).Append("», AA ").Append(GfxQuality.AANames[GfxQuality.AA])
                   .Append(", ").Append(GfxSky.Info).Append("\nОтражения: ").Append(ReflectionGrid.LastInfo).Append("\nОгни: ").Append(CityLights.LastInfo);   // v12
                if (VolnaLook.Active) _sb.Append("\nСвет: ").Append(VolnaLook.Info);   // v13
                _sb.Append("\nF3 — скрыть, F9 — NavTester, F8 — бенчмарк, F7 — фототочки (в главном меню)");
            }
            _text = _sb.ToString();
        }

        void OnGUI()
        {
            if (!Full && !GameSettings.ShowFps) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Screen.height / 60), richText = false };
                _style.normal.textColor = new Color(0.85f, 1f, 0.8f);
                if (GameAssets.Font != null) _style.font = GameAssets.Font;
            }
            var size = _style.CalcSize(new GUIContent(_text));
            float w = Full ? Mathf.Max(size.x, 420f) : size.x;
            var r = new Rect(8, 8, w + 12, (Full ? _style.CalcHeight(new GUIContent(_text), w) : size.y) + 8);
            var old = GUI.color; GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(new Rect(r.x + 6, r.y + 4, w, r.height), _text, _style);
        }
    }
}
