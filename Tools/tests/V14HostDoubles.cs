// Standalone logic-test host. No Unity physics, rendering, native calls, disk persistence or network is simulated.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1f) { this.r=r;this.g=g;this.b=b;this.a=a; }
    }
    public static class Mathf
    {
        public static int Max(int a,int b)=>Math.Max(a,b); public static float Max(float a,float b)=>Math.Max(a,b);
        public static int Min(int a,int b)=>Math.Min(a,b); public static float Min(float a,float b)=>Math.Min(a,b);
        public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b); public static float Clamp01(float v)=>Math.Clamp(v,0f,1f);
        public static float Pow(float a,float b)=>(float)Math.Pow(a,b);
        public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
        public static int RoundToInt(float v)=>(int)Math.Round(v,MidpointRounding.ToEven);
        public static int CeilToInt(float v)=>(int)Math.Ceiling(v);
    }
}
namespace Severny
{
    public class BalanceOverride { public string itemId; public int price,rarity; }
    public class V14Settings { public static V14Settings Current=new V14Settings(); public BalanceOverride[] items=Array.Empty<BalanceOverride>(); }
    public class PlayerProfile { public int roubles,xp; public List<QuestState> quests=new List<QuestState>(); public InventoryGrid stash=new InventoryGrid("test",10,10); }
    public static class SaveSystem
    {
        public static PlayerProfile Current=new PlayerProfile(); public static bool Success=true; public static int Saves;
        public static bool TrySave() { Saves++;return Success; }
    }
    public class GameBootstrap { public static GameBootstrap I; public bool InRaid,Busy; }
    public class RaidManager { public static RaidManager I; public bool Running,IsTutorial; }
    public static class Hud { public static void Notify(string s){} }
}
