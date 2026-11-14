// Raid modifiers chosen on the raid setup screen (or received from the LAN host): time of day incl. night,
// winter variant of a map, weather, loot luck and bot multiplier. Resolve() is called once at raid start.
namespace Severny
{
    public static class RaidOptions
    {
        public const int TodMorning = 0, TodDay = 1, TodEvening = 2, TodNight = 3;
        /// <summary>v13: 4 = «после дождя» — sun, wet streets and puddles (the main look of the «ТЦ «Волна»» references).</summary>
        public const int WeatherRandom = 0, WeatherClear = 1, WeatherPrecip = 2, WeatherFog = 3, WeatherAfterRain = 4, WeatherMax = 4;
        public static readonly string[] TodNames = { "Утро", "День", "Вечер", "Ночь" };
        public static readonly string[] WeatherNames = { "Случайная", "Ясно", "Осадки", "Туман", "После дождя" };

        public static int TimeOfDay = TodDay;
        public static bool Winter;
        public static int WeatherSetting;
        public static int ResolvedWeather = WeatherClear;
        public static int Seed;
        public static bool Tutorial;

        public static bool Night => TimeOfDay == TodNight;
        public static bool Precip => ResolvedWeather == WeatherPrecip;
        public static bool Foggy => ResolvedWeather == WeatherFog;
        public static bool AfterRain => ResolvedWeather == WeatherAfterRain;
        /// <summary>Wet streets: rain or after rain (not in winter).</summary>
        public static bool Wet => !Winter && (ResolvedWeather == WeatherPrecip || ResolvedWeather == WeatherAfterRain);

        /// <summary>Night raids: many more enemies (they are also better equipped, see BotController.FillLoot).</summary>
        public static float BotMultiplier => Tutorial ? 1f : Night ? 1.6f : Winter ? 1.1f : 1f;
        /// <summary>Loot quality: 1.25 normal, winter 1.4, night 1.75, winter night 1.9.</summary>
        public static float Luck => Tutorial ? 1.25f : Night ? (Winter ? 1.9f : 1.75f) : Winter ? 1.4f : 1.25f;

        /// <summary>volna = map «ТЦ «Волна»» (v13): random weather there is mostly «ясно» / «после дождя» (its references).
        /// Deterministic from the seed (the LAN host and the clients get the same weather).</summary>
        public static void Resolve(int timeOfDay, bool winter, int weather, int seed, bool volna = false)
        {
            TimeOfDay = timeOfDay < 0 || timeOfDay > 3 ? TodDay : timeOfDay;
            Winter = winter;
            WeatherSetting = weather;
            Seed = seed;
            if (weather >= WeatherClear && weather <= WeatherMax) ResolvedWeather = winter && weather == WeatherAfterRain ? WeatherClear : weather;
            else
            {
                double x = new System.Random(seed ^ 0x5EED).NextDouble();
                // winter: snowfall is the norm; otherwise mostly dry
                ResolvedWeather = winter ? (x < 0.55 ? WeatherPrecip : x < 0.8 ? WeatherClear : WeatherFog)
                                : volna ? (x < 0.35 ? WeatherClear : x < 0.7 ? WeatherAfterRain : x < 0.85 ? WeatherPrecip : WeatherFog)
                                         : (x < 0.5 ? WeatherClear : x < 0.78 ? WeatherPrecip : WeatherFog);
            }
            ItemDB.Luck = Luck;
        }

        public static void Reset()
        {
            Tutorial = false; Winter = false; TimeOfDay = TodDay; ResolvedWeather = WeatherClear;
            ItemDB.Luck = 1.25f;
        }

        public static string PrecipName => Winter ? "снег" : "дождь";
        public static string Describe()
        {
            string w = ResolvedWeather == WeatherPrecip ? (Winter ? "снегопад" : "дождь") : ResolvedWeather == WeatherFog ? "туман" :
                       ResolvedWeather == WeatherAfterRain ? "после дождя" : "ясно";
            return TodNames[TimeOfDay] + (Winter ? " · зима" : "") + " · " + w;
        }
    }
}
