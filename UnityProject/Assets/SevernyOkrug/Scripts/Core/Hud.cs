// Decoupled HUD facade: gameplay code calls these, the HUD screen subscribes.
using System;

namespace Severny
{
    public static class Hud
    {
        public static Action<string, float> NotifyHandler;
        public static Action<string> PromptHandler;
        public static Action<string, float> ProgressHandler; // label, 0..1 (null label hides)
        public static Action DamageFlashHandler;

        public static void Notify(string s, float dur = 2.5f) => NotifyHandler?.Invoke(s, dur);
        public static void Prompt(string s) => PromptHandler?.Invoke(s);
        public static void Progress(string label, float t) => ProgressHandler?.Invoke(label, t);
        public static void DamageFlash() => DamageFlashHandler?.Invoke();
    }
}
