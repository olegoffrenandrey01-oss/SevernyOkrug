using System;
using System.Collections.Generic;
using Severny;
static class V14LogicChecks
{
    static int checks;
    static void Check(bool condition,string name) { checks++; if(!condition)throw new Exception(name); }
    static void Near(float actual,float expected,string name)=>Check(Math.Abs(actual-expected)<0.001,name);
    static int Main()
    {
        // The production definitions and inventory implementation are compiled directly into this host.
        var ids=new HashSet<string>();
        foreach(var d in ItemDB.All)
        {
            Check(ids.Add(d.id),"duplicate item "+d.id);
            Check(d.price>0 && d.rarity>=1 && d.rarity<=5,"balance "+d.id);
            if(d.type!=ItemType.Weapon)continue;
            var m=ItemDB.Get(d.magId);var a=ItemDB.Get(ItemDB.DefaultAmmo(d.caliber));
            Check(m!=null && m.caliber==d.caliber && m.capacity>0,"magazine "+d.id);
            Check(a!=null && a.caliber==d.caliber,"ammo "+d.id);
        }
        foreach(ItemDB.LootTable table in Enum.GetValues(typeof(ItemDB.LootTable)))
        {
            foreach(var id in ItemDB.TableItems(table))Check(ItemDB.Get(id)!=null,"unknown loot "+id);
            var rng=new Random(1400);
            for(int i=0;i<200;i++)Check(ItemDB.PickFrom(table,rng)!=null,"empty loot pick");
        }
        var grid=new InventoryGrid("full",1,1); var stack=ItemInstance.Create("a545",59);grid.AutoPlace(stack);
        var batch=ItemInstance.Create("a545",30);
        Check(!grid.TryAutoPlaceAll(batch) && stack.count==59 && batch.count==30,"failed purchase mutated inventory");
        Check(grid.TryAutoPlaceAll(ItemInstance.Create("a545",1)) && stack.count==60,"exact stack capacity");
        var partial=new InventoryGrid("partial",1,1);var old=ItemInstance.Create("a545",59);partial.AutoPlace(old);
        var rest=ItemInstance.Create("a545",30);
        Check(!partial.AutoPlace(rest)&&old.count==60&&rest.count==29,"original partial-placement contract changed");
        var room=new InventoryGrid("room",2,1);var st=ItemInstance.Create("a545",59);room.AutoPlace(st);
        Check(room.TryAutoPlaceAll(ItemInstance.Create("a545",30))&&room.Count("a545")==89,"stack plus free slot");
        Check(!room.TryAutoPlaceAll(st),"same reference accepted");
        Near(PenetrationRules.Cost(.02f,.02f,180,1,false),3.6f,"thin wood cost");
        Check(PenetrationRules.Cost(1,.02f,180,1,false)>27,"thick wood protects");
        Near(PenetrationRules.Cost(1,.025f,180,1,true),9,"hollow crate walls");
        Near(PenetrationRules.Cost(.003f,.003f,2500,1,false),7.5f,"thin metal cost");
        Check(PenetrationRules.Cost(.02f,.02f,180,.2f,false)>PenetrationRules.Cost(.02f,.02f,180,1,false),"oblique angle should resist more");
        Check(float.IsPositiveInfinity(PenetrationRules.Cost(float.NaN,.02f,180,1,false)),"NaN path accepted");
        var l=new Loadout();l.primary=ItemInstance.Create("d12");l.secure.AutoPlace(ItemInstance.Create("flash"));l.pockets.AutoPlace(ItemInstance.Create("water"));l.LoseAllExceptSecure();
        Check(l.Get(EquipSlot.Primary)==null&&l.pockets.items.Count==0&&l.secure.Count("flash")==1,"death/secure contract changed");
        var p=SaveSystem.Current;var search=QuestSystem.All[0];var deliver=QuestSystem.All[2];
        Check(!QuestSystem.Claim(search),"claim before accept");
        SaveSystem.Success=false;Check(!QuestSystem.Accept(search)&&!QuestSystem.State(p,search.id).active,"accept save failure rollback");
        SaveSystem.Success=true;Check(QuestSystem.Accept(search),"accept");
        RaidManager.I=new RaidManager{Running=true,IsTutorial=true};QuestSystem.Record("search");
        Check(QuestSystem.Progress(p,search)==0,"tutorial quest farming");
        RaidManager.I.IsTutorial=false;int saves=SaveSystem.Saves;
        for(int i=0;i<10;i++)QuestSystem.Record("search");
        Check(QuestSystem.Progress(p,search)==3&&SaveSystem.Saves==saves,"progress cap/no mid-raid inventory autosave");
        GameBootstrap.I=new GameBootstrap{InRaid=true};Check(!QuestSystem.Claim(search),"claim during raid");GameBootstrap.I.InRaid=false;
        SaveSystem.Success=false;Check(!QuestSystem.Claim(search)&&p.roubles==0&&!QuestSystem.State(p,search.id).claimed,"reward rollback");
        SaveSystem.Success=true;Check(QuestSystem.Claim(search)&&p.roubles==search.money,"reward");Check(!QuestSystem.Claim(search)&&p.roubles==search.money,"duplicate reward");
        Check(QuestSystem.Accept(deliver),"accept delivery");p.stash.AutoPlace(ItemInstance.Create("stew"));
        Check(!QuestSystem.Claim(deliver)&&p.stash.Count("stew")==1,"insufficient handover");p.stash.AutoPlace(ItemInstance.Create("stew"));
        int wallet=p.roubles;SaveSystem.Success=false;
        Check(!QuestSystem.Claim(deliver)&&p.stash.Count("stew")==2&&p.roubles==wallet,"handover rollback lost items");
        SaveSystem.Success=true;Check(QuestSystem.Claim(deliver)&&p.stash.Count("stew")==0&&p.roubles==wallet+deliver.money,"handover consumption");
        Check(!QuestSystem.Claim(deliver),"duplicate handover reward");
        Console.WriteLine("Passed "+checks+" standalone logic assertions. Unity physics/rendering/persistence/LAN not tested.");return 0;
    }
}
