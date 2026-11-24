// Run inside the installed game's Mono, in a separate process without a Unity scene.
// Substitutions below stand in for the scene grant, carrier spawn, animated item spawn
// and the engine JSON serializer. The tested mod and optional third-party hooks are real.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;
using SuperFantasyKingdom.UI;
using SuperFantasyKingdom.TitleScreen;
using UnityEngine;
using UnityEngine.Events;

internal static class Interactions
{
    private static Assembly mod;
    private static object plugin, run;
    private static Type state, pluginType;
    private static ResourceType wood;
    private static int carriers;
    private static readonly Harmony scene = new Harmony("kingdomcharters.tests.scene");

    private static T SceneObject<T>() where T : UnityEngine.Object
    {
        var obj = (T)FormatterServices.GetUninitializedObject(typeof(T));
        // Managed Unity equality checks recognize the fixture as a live object.
        AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(obj, new IntPtr(1));
        return obj;
    }

    private static void Set(object obj, string name, object value)
    {
        AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    }

    private static void EmptyList(object obj, string name)
    {
        var field = AccessTools.Field(obj.GetType(), name);
        field.SetValue(obj, Activator.CreateInstance(field.FieldType));
    }

    private static object Call(Type t, string method, object obj, params object[] args)
    {
        return AccessTools.Method(t, method).Invoke(obj, args);
    }

    private static void Replace(Type t, string method, Type[] parameters, string replacement)
    {
        scene.Patch(AccessTools.Method(t, method, parameters), new HarmonyMethod(
            AccessTools.Method(typeof(Interactions), replacement)) { priority = Priority.Last });
    }

    private static bool Starting(ResourceManager __instance)
    {
        __instance.AddResource(wood, 8);
        __instance.AddResource(ResourceType.Stone, 5);
        return false;
    }

    private static void LateSupplies(ResourceManager __instance)
    {
        __instance.AddResource(ResourceType.Stone, 2);
    }

    private static bool CityEventFixture(CityEvent cityEvent)
    {
        if (cityEvent == CityEvent.Carrier) carriers++;
        return false;
    }

    private static bool LoadScene() { return false; }

    private static bool SpawnFixture(List<DragItem> ___m_DragItems)
    {
        ___m_DragItems.Add(null);
        return false;
    }

    private static ResourceData Material(ResourceType type)
    {
        var data = SceneObject<ResourceData>();
        data.type = type;
        data.buildingToTakeFrom = BuildingType.Castle;
        return data;
    }

    private static void SetupStock(bool mapped, string choice)
    {
        carriers = 0;
        wood = mapped ? ResourceType.Board : ResourceType.Wood;
        var race = RaceManager.Instance = SceneObject<RaceManager>();
        var timber = Material(wood);
        Set(race, "resourceWood", timber);
        Set(race, "resourceBoard", Material(ResourceType.Board));
        Set(race, "resourceStone", Material(ResourceType.Stone));
        ResourceManager.Instance = SceneObject<ResourceManager>();
        Set(ResourceManager.Instance, "m_StartingResources", new Dictionary<ResourceType, int>());
        CityManager.Instance = SceneObject<CityManager>();
        EmptyList(CityManager.Instance, "m_Buildings");
        EmptyList(CityManager.Instance, "m_Outposts");
        GameManager.Instance = SceneObject<GameManager>();
        Set(GameManager.Instance, "m_IsGameLoaded", true);
        MainManager.Instance = SceneObject<MainManager>();
        plugin = FormatterServices.GetUninitializedObject(pluginType);
        AccessTools.Field(pluginType, "Instance").SetValue(null, plugin);
        run = Call(state, "Start", null, Enum.Parse(mod.GetType("KingdomCharters.Charter"), choice));
        Set(plugin, "Run", run);
        Set(plugin, "RunId", "test-run");
        Call(mod.GetType("KingdomCharters.Supplies"), "BindRun", null);
    }

    private static int Count(ResourceType type)
    {
        return ResourceManager.Instance.GetResourceAvailableAmount(type, false);
    }

    private static int Debt() { return (int)AccessTools.Field(state, "debt").GetValue(run); }

    private static void Opening(bool extraCarrier, string choice, bool mapped)
    {
        SetupStock(mapped, choice);
        bool forester = choice == "Forester";
        if (forester)
        {
            var castle = SceneObject<BuildingCastle>();
            Set(castle, "type", BuildingType.Castle);
            var storage = SceneObject<ResourceStorage>();
            Set(storage, "m_ResourcesAmount", new int[Enum.GetValues(typeof(ResourceType)).Length]);
            EmptyList(storage, "m_Resources");
            Set(storage, "generateResourcePackets", true);
            Set(castle, "storage", storage);
            Set(CityManager.Instance, "m_Buildings", new List<BuildingCity> { castle });
        }
        ResourceManager.Instance.GiveStartingResources();
        Smoke.Check(Count(wood) == (forester ? 11 : 8) && Count(ResourceType.Stone) == (forester ? 7 : 9) &&
            Debt() == (forester ? 2 : 3) && carriers == (extraCarrier ? 1 : 0),
            choice + " opening; modified supplies, carrier=" + carriers + ", wood slot=" + wood);
        var owed = forester ? ResourceType.Stone : wood;
        int before = Count(owed);
        ResourceManager.Instance.AddResource(owed, 1);
        ResourceManager.Instance.ResourceAmountChanged(owed);
        Smoke.Check(Count(owed) == before && Debt() == (forester ? 1 : 2), "one production credit with duplicate notification");
    }

    private static void RepeatOpening()
    {
        int timber = Count(wood);
        int stone = Count(ResourceType.Stone);
        ResourceManager.Instance.GiveStartingResources();
        Smoke.Check(Count(wood) == timber + 8 && Count(ResourceType.Stone) == stone + 7 && Debt() == 2,
            "repeated opening hook adds only fixture supplies and preserves outstanding debt");
    }

    private static void Reload()
    {
        var game = new GameData { runID = "test-run", hash = "checkpoint" };
        var json = new JObject { { "runID", game.runID }, { "hash", game.hash },
            { "kingdomChartersV1", JObject.FromObject(run) }, { "otherMod", 42 } };
        Attach(game, json.ToString());
        Attach(new GameData { runID = "preview" }, "{\"runID\":\"preview\"}");
        var manager = GameDataManager.Instance = SceneObject<GameDataManager>();
        Set(manager, "m_GameData", game);
        Set(manager, "m_RunID", game.runID);
        int stock = Count(ResourceType.Stone);
        manager.Load();
        run = AccessTools.Field(pluginType, "Run").GetValue(plugin);
        ResourceManager.Instance.ResourceAmountChanged(ResourceType.Stone);
        Smoke.Check(Debt() == 1 && Count(ResourceType.Stone) == stock && Count(wood) == 11,
            "restored checkpoint retains one debt and does not regrant or tax restored stock");
    }

    private static void Attach(GameData data, string json)
    {
        // Simulate only the Unity native JSON parser; call the packaged serializer postfix.
        Call(mod.GetType("KingdomCharters.SaveHooks+Read"), "Postfix", null, json, data);
    }

    private static void CheckpointIdentity()
    {
        SetupStock(false, "Forester");
        var tavern = SceneObject<SuperFantasyKingdom.Tavern.TavernSaveManager>();
        var restore = mod.GetType("KingdomCharters.SaveHooks+TavernLoad");
        var selected = new GameData { runID = "same-run", hash = "same-native-hash" };
        var preview = new GameData { runID = selected.runID, hash = selected.hash };
        var json = new JObject { { "runID", selected.runID }, { "hash", selected.hash },
            { "kingdomChartersV1", JObject.FromObject(run) } };
        json["kingdomChartersV1"]["debt"] = 1;
        Attach(selected, json.ToString());
        json["kingdomChartersV1"]["debt"] = 2;
        Attach(preview, json.ToString());
        Attach(new GameData { runID = "unrelated" }, "{}");
        selected.hash = "";
        Set(tavern, "m_GameData", selected);
        Call(restore, "Postfix", null, tavern);
        run = AccessTools.Field(pluginType, "Run").GetValue(plugin);
        Smoke.Check(Debt() == 1, "tavern keeps selected debt across same-hash backup and unrelated preview");
        var legacy = new GameData { runID = selected.runID };
        Set(tavern, "m_GameData", legacy);
        Attach(legacy, "{\"runID\":\"same-run\"}");
        Call(restore, "Postfix", null, tavern);
        Smoke.Check(AccessTools.Field(pluginType, "Run").GetValue(plugin) == null,
            "pre-charter checkpoint with the same run ID uses standard rules");
    }

    private static bool FinishStarter() { ResourceManager.Instance.GiveStartingResources(); return false; }

    private static void OpenAndRestart()
    {
        SetupStock(true, "Forester");
        var foreign = new Harmony("kingdomcharters.tests.latestarter");
        var screen = AccessTools.Method(typeof(UIOverlayStarterResources), "StartGame");
        foreign.Patch(screen, new HarmonyMethod(typeof(Interactions), "FinishStarter"),
            new HarmonyMethod(typeof(Interactions), "ExtraStarterStone"));
        SceneObject<UIOverlayStarterResources>().StartGame();
        foreign.UnpatchSelf();
        Smoke.Check(Count(ResourceType.Stone) == 9 && Count(ResourceType.Board) == 11 && Debt() == 2,
            "outer starter grant counts as opening stock with mapped gift");
        var supplies = mod.GetType("KingdomCharters.Supplies");
        var choice = mod.GetType("KingdomCharters.Charter");
        string terms = (string)Call(supplies, "MaterialTerms", null, Enum.Parse(choice, "Forester"));
        Smoke.Check(terms.Contains("3 Board") && terms.Contains("2 Stone"), "review uses mapped names");
        ResourceManager.Instance.AddResource(ResourceType.Stone, 1);
        Smoke.Check(Count(ResourceType.Stone) == 9 && Debt() == 1, "first later delivery pays one debt");
        var manager = GameDataManager.Instance = SceneObject<GameDataManager>();
        Set(manager, "m_FirstLoad", true);
        var restart = mod.GetType("KingdomCharters.SaveHooks+Restart");
        Call(restart, "Postfix", null, true);
        Set(manager, "m_RunID", "fresh-restart");
        manager.Load();
        run = AccessTools.Field(pluginType, "Run").GetValue(plugin);
        Smoke.Check(Debt() == 2 && !(bool)AccessTools.Field(state, "openingComplete").GetValue(run),
            "explicit day-one restart preserves choice and starts a fresh opening");
        Call(restart, "Postfix", null, true);
        Call(mod.GetType("KingdomCharters.SaveHooks+LeaveRun"), "Postfix", null);
        manager.Load();
        Smoke.Check(AccessTools.Field(pluginType, "Run").GetValue(plugin) == null, "leaving clears restart choice");
        Set(plugin, "Pending", Enum.Parse(choice, "Forester"));
        Set(MainManager.Instance, "m_IsChallenge", true);
        manager.Load();
        Smoke.Check(AccessTools.Field(pluginType, "Run").GetValue(plugin) == null &&
            AccessTools.Field(pluginType, "Pending").GetValue(plugin).ToString() == "Standard",
            "challenge entry consumes pending selection without applying a charter");
        Set(MainManager.Instance, "m_IsChallenge", false);
    }

    private static void ExtraStarterStone() { ResourceManager.Instance.AddResource(ResourceType.Stone, 2); }
    private static void Observer() { }

    private static Assembly Package(string directory, string name)
    {
        return Assembly.LoadFrom(Path.Combine(directory, name + ".dll"));
    }

    private static void Yield(Assembly cheat, int multiplier)
    {
        var type = cheat.GetType("CheatMenu.Cheats.ProductionYield", true);
        var file = new ConfigFile(Path.Combine(Path.GetDirectoryName(mod.Location), "fixture.cfg"), false);
        Call(type, "Bind", null, file);
        ((ConfigEntry<float>)AccessTools.Field(type, "Multiplier").GetValue(null)).Value = multiplier;
        var producer = SceneObject<BuildingResource>();
        Set(producer, "amount", 2);
        var awake = type.GetNestedType("Building_Awake", BindingFlags.NonPublic);
        Call(awake, "Postfix", null, producer);
        var yield = (int)AccessTools.Field(typeof(BuildingResource), "amount").GetValue(producer);
        int before = Count(ResourceType.Stone);
        ResourceManager.Instance.AddResource(ResourceType.Stone, yield);
        Smoke.Check(yield == multiplier * 2 && Debt() == 0 && Count(ResourceType.Stone) == before + yield - 1,
            "CheatMenu YieldMultiplier=" + multiplier + " output minus exactly one remaining debt");
    }

    private static Action<UIOverlayMerchant> createCards;
    private static object dailyVisit;

    private static GuestCaravan GuestForScreen()
    {
        var guest = SceneObject<GuestCaravan>();
        EmptyList(guest, "m_Chosen");
        if (dailyVisit != null) Set(WorldManager.Instance, "caravan", guest);
        return guest;
    }
    private static bool RerollFixture(UIOverlayMerchant merchant)
    {
        if (createCards == null) return true;
        createCards(merchant);
        return false;
    }

    private static UICardItem Card(string id, ResourceAmount[] costs, bool immediate, UnityAction onAccept)
    {
        var card = SceneObject<UICardItem>();
        EmptyList(card, "m_Callbacks");
        card.AddCallback(onAccept);
        var screen = SceneObject<UIOverlayMerchant>();
        screen.SetUseCardSelectCallback(immediate);
        // Replace the Unity prefab instantiation inside the real patched Reroll scope.
        createCards = delegate(UIOverlayMerchant overlay) {
            Call(mod.GetType("KingdomCharters.Merchant"), "RegisterImmediatePick", null, overlay, card, id, costs);
        };
        try { Call(typeof(GuestCaravan), "Reroll", GuestForScreen(), screen, false); }
        finally { createCards = null; }
        return card;
    }

    private static bool ForeignAppointment(ref bool __result)
    {
        __result = true;
        return false;
    }

    private static void OtherRunRules()
    {
        SetupStock(false, "Merchant");
        var other = new Harmony("kingdomcharters.tests.foreignrules");
        var day = AccessTools.Method(typeof(GuestCaravan), "CheckDay");
        other.Patch(day, new HarmonyMethod(AccessTools.Method(typeof(Interactions), "ForeignAppointment")));
        Set(run, "firstVisit", 16);
        Smoke.Check((bool)day.Invoke(SceneObject<GuestCaravan>(), new object[] { 16 }),
            "foreign scheduler owns its day even when Merchant previously advanced the visit");
        var title = SceneObject<TitleScreenHeroSelectionManager>();
        other.Patch(AccessTools.Method(typeof(TitleScreenHeroSelectionManager), "Play"),
            new HarmonyMethod(AccessTools.Method(typeof(Interactions), "LoadScene")) { priority = Priority.First });
        title.Play();
        Call(pluginType, "StartRun", plugin, title, Enum.Parse(mod.GetType("KingdomCharters.Charter"), "Mason"));
        Smoke.Check(AccessTools.Field(pluginType, "Pending").GetValue(plugin).ToString() == "Standard",
            "foreign Play veto leaves no pending charter");
        other.UnpatchSelf();
        other.Patch(day, postfix: new HarmonyMethod(typeof(Interactions), "Observer"));
        var picker = mod.GetType("KingdomCharters.CharterPicker");
        var menu = FormatterServices.GetUninitializedObject(picker);
        Smoke.Check(((string)Call(picker, "Terms", menu, 1)).Contains("Another mod controls arrivals"),
            "read-only schedule observer disclosed before confirmation");
        other.UnpatchSelf();
        Smoke.Check(((string)Call(picker, "Terms", menu, 1)).Contains("one day early"),
            "ordinary Merchant terms return without an external scheduler");
    }

    private static readonly List<UICardItem> deliveryCards = new List<UICardItem>();

    private static bool GetFixtureItem(string prefabName, ref Item __result)
    {
        __result = SceneObject<Item>();
        Set(__result, "identifier", prefabName);
        return false;
    }

    private static bool CreateDeliveryCard(UIOverlayMerchant __instance, Item item, ResourceAmount[] costs, int id)
    {
        var card = SceneObject<UICardItem>();
        EmptyList(card, "m_Callbacks");
        card.AddCallback(delegate { Call(typeof(UIOverlayMerchant), "Callback", __instance, id, costs); });
        Call(mod.GetType("KingdomCharters.Merchant"), "RegisterImmediatePick", null, __instance, card, item.GetIdentifier(), costs);
        deliveryCards.Add(card);
        return false;
    }

    private static object currentBook;

    // Newer Commissions builds use direct prepaid cards. Substitute Unity prefab
    // construction here and let the packaged OrderBook arbitrate the actual claim.
    private static bool CreatePrepaidChoices(object visit)
    {
        var t = currentBook.GetType();
        var ids = (string[])AccessTools.Field(t, "items").GetValue(currentBook);
        int day = (int)AccessTools.Field(visit.GetType(), "Day").GetValue(visit);
        int number = (int)AccessTools.Field(visit.GetType(), "Number").GetValue(visit);
        Func<string, string> identify = key => AddressablesManager.Instance.GetItem(key).GetIdentifier();
        foreach (string id in ids)
        {
            string key = id;
            var card = SceneObject<UICardItem>();
            EmptyList(card, "m_Callbacks");
            card.AddCallback(delegate {
                Action<string> spawn = item => ItemManager.Instance.SpawnDragItem(item, Vector2.zero);
                Delegate grant = spawn;
                if (AccessTools.Method(t, "Claim").GetParameters().Last().ParameterType == typeof(Func<string, bool>))
                    grant = (Func<string, bool>)(item => {
                        int count = ItemManager.Instance.GetDragItems().Count;
                        spawn(item);
                        return ItemManager.Instance.GetDragItems().Count == count + 1;
                    });
                Call(t, "Claim", currentBook, day, number, ids, key, identify, grant);
            });
            deliveryCards.Add(card);
        }
        return false;
    }

    private static UIOverlayMerchant CommissionVisit(int day)
    {
        Set(DaytimeManager.Instance, "m_CurrentDay", day);
        var screen = SceneObject<UIOverlayMerchant>();
        screen.SetUseCardSelectCallback(true);
        var guest = GuestForScreen();
        screen.SetCallback((Action<int, ResourceAmount[]>)Delegate.CreateDelegate(
            typeof(Action<int, ResourceAmount[]>), guest, AccessTools.Method(typeof(GuestCaravan), "Callback")));
        Set(screen, "m_CallbackCancel", Delegate.CreateDelegate(typeof(Action<UIOverlayMerchant>),
            guest, AccessTools.Method(typeof(GuestCaravan), "Cancel")));
        deliveryCards.Clear();
        Call(typeof(GuestCaravan), "Reroll", guest, screen, false);
        Smoke.Check(deliveryCards.Count == 3, "packaged Commissions offers all three prepaid items on day " + day);
        if (dailyVisit != null && currentBook == null)
        {
            var items = (string[])AccessTools.Field(dailyVisit.GetType(), "items").GetValue(dailyVisit);
            Smoke.Check(items.SequenceEqual(new[] { "ring", "helmet", "bow" }),
                "Daily Caravan inventory hook retained the commissioned stock on day " + day);
        }
        return screen;
    }

    private static void Fulfill(Assembly commissions, object book, int remaining, bool daily)
    {
        var t = commissions.GetType("CaravanCommissions.Plugin", true);
        var patches = new Harmony("gkj.sfk.caravancommissions");
        try
        {
            Call(t, "Reset", null, book);
            var cardsType = commissions.GetType("CaravanCommissions.DeliveryCards");
            if (cardsType != null)
            {
                currentBook = book;
                patches.Patch(AccessTools.Method(cardsType, "Show"),
                    new HarmonyMethod(AccessTools.Method(typeof(Interactions), "CreatePrepaidChoices")));
            }
            patches.CreateClassProcessor(t.GetNestedType("Inventory", BindingFlags.NonPublic)).Patch();
            patches.CreateClassProcessor(t.GetNestedType("Compare", BindingFlags.NonPublic)).Patch();
            var screen = CommissionVisit(16);
            var resources = ResourceManager.Instance;
            Call(typeof(UIOverlayMerchant), "Cancel", screen);
            Smoke.Check(resources.GetCoins() == remaining && Count(ResourceType.Wood) == 0 &&
                !((bool)Call(book.GetType(), "Available", book, 16, 1)) &&
                (bool)AccessTools.Property(book.GetType(), "Pending").GetValue(book, null),
                "packaged Commissions cancel defers order without another payment");
            resources.SetCoins(0);
            CommissionVisit(17);
            var delivery = deliveryCards[1];
            Smoke.Check((bool)Call(mod.GetType("KingdomCharters.Merchant+SelectCard"), "Prefix", null, delivery),
                "prepaid delivery selectable at zero coins");
            delivery.Callback();
            delivery.Callback();
            Smoke.Check(resources.GetCoins() == 0 && ItemManager.Instance.GetDragItems().Count == 2 &&
                !(bool)AccessTools.Property(book.GetType(), "Pending").GetValue(book, null),
                "prepaid claim grants one free item, Daily Caravan=" + daily);
        }
        finally { patches.UnpatchSelf(); currentBook = null; }
    }

    private static void Caravan(Assembly commissions, bool daily)
    {
        SetupStock(false, "Merchant");
        ItemManager.Instance = SceneObject<ItemManager>();
        EmptyList(ItemManager.Instance, "m_DragItems");
        var resources = ResourceManager.Instance;
        var costs = new[] { new ResourceAmount(ResourceType.Board, 2), new ResourceAmount(ResourceType.Coins, 3) };
        var select = mod.GetType("KingdomCharters.Merchant+SelectCard");
        resources.AddResource(ResourceType.Board, 2);
        var card = Card("ring", costs, true, delegate {
            ItemManager.Instance.SpawnDragItem("ring", Vector2.zero);
            resources.SpendResource(costs[0]);
            resources.SpendResource(costs[1]);
        });
        resources.SetCoins(3);
        Smoke.Check((bool)Call(select, "Prefix", null, card) == daily,
            "mixed price affordability with Daily Caravan=" + daily);
        resources.SetCoins(5);
        var rejected = Card("helmet", null, true, delegate { });
        rejected.Callback();
        Smoke.Check(resources.GetCoins() == 5, "no grant, no fee");
        card.Callback();
        if (!daily) card.Callback();
        int remaining = daily ? 2 : 1;
        Smoke.Check(resources.GetCoins() == remaining && Count(ResourceType.Board) == 0 &&
            ItemManager.Instance.GetDragItems().Count == 1 && costs[0].amount == 2 && costs[1].amount == 3,
            "foreign mixed purchase retained, one item, balance=" + remaining);
        if (commissions == null) return;
        var t = commissions.GetType("CaravanCommissions.OrderBook", true);
        var book = Activator.CreateInstance(t, true);
        Call(t, "BeginDay", book, 15);
        resources.AddResource(ResourceType.Wood, 3);
        var ids = new[] { "ring", "helmet", "bow" };
        Func<bool> deposit = delegate {
            resources.SpendResource(ResourceType.Wood, 3);
            return true;
        };
        Smoke.Check((bool)Call(t, "Place", book, 15, 1, "Human", ids, deposit) &&
            !(bool)Call(t, "Place", book, 15, 1, "Human", ids, deposit), "order takes one deposit");
        Fulfill(commissions, book, remaining, daily);
    }

    internal static void ConfigureLoader(Assembly pluginAssembly)
    {
        var folder = Path.GetDirectoryName(pluginAssembly.Location);
        Call(typeof(Paths), "SetExecutablePath", null, Path.Combine(folder, "fixture.exe"), folder,
            Path.GetDirectoryName(typeof(ResourceManager).Assembly.Location), new string[0]);
    }

    internal static void Run(Assembly pluginAssembly, string external)
    {
        mod = pluginAssembly;
        pluginType = mod.GetType("KingdomCharters.Plugin", true);
        state = mod.GetType("KingdomCharters.CharterState", true);
        Replace(typeof(ResourceManager), "GiveStartingResources", Type.EmptyTypes, "Starting");
        scene.Patch(AccessTools.Method(typeof(ResourceManager), "GiveStartingResources"), postfix:
            new HarmonyMethod(AccessTools.Method(typeof(Interactions), "LateSupplies")) { priority = Priority.Last });
        Replace(typeof(CityManager), "TriggerCityEvent", null, "CityEventFixture");
        Replace(typeof(GameDataManager), "Load", Type.EmptyTypes, "LoadScene");
        Replace(typeof(ItemManager), "SpawnDragItem", null, "SpawnFixture");
        Replace(typeof(GuestCaravan), "Reroll", null, "RerollFixture");
        OtherRunRules();
        OpenAndRestart();
        CheckpointIdentity();
        SetupStock(true, "Mason");
        var supplies = mod.GetType("KingdomCharters.Supplies");
        var mason = Enum.Parse(mod.GetType("KingdomCharters.Charter"), "Mason");
        Smoke.Check(((string)Call(supplies, "MaterialTerms", null, mason)).Contains("3 Board"),
            "Mason review names the mapped repayment");
        Set(plugin, "<Logger>k__BackingField", new BepInEx.Logging.ManualLogSource("Charters fixture"));
        Set(RaceManager.Instance, "resourceStone", Material(ResourceType.Board));
        Call(supplies, "BindRun", null);
        Smoke.Check(!(bool)AccessTools.Property(supplies, "HasMaterialPair").GetValue(null, null) &&
            ((string)Call(supplies, "MaterialTerms", null, mason)).Contains("Standard Run"),
            "identical material slots explain the required standard fallback");
        Opening(false, "Forester", false);
        Reload();
        Opening(false, "Mason", true);
        RepeatOpening();
        Caravan(null, false);
        if (!string.IsNullOrEmpty(external))
        {
            var carrier = new Harmony("ownly.extrastartingcarrier");
            carrier.PatchAll(Package(external, "ExtraStartingCarrier"));
            Opening(true, "Forester", false);
            Reload();
            var cheat = Package(external, "CheatMenu");
            Yield(cheat, 1);
            Opening(true, "Mason", true);
            Opening(true, "Forester", false);
            Reload();
            Yield(cheat, 3);
            DaytimeManager.Instance = SceneObject<DaytimeManager>();
            AddressablesManager.Instance = SceneObject<AddressablesManager>();
            OverlayManager.Instance = SceneObject<OverlayManager>();
            GameCamera.Instance = SceneObject<GameCamera>();
            Replace(typeof(AddressablesManager), "GetItem", null, "GetFixtureItem");
            Replace(typeof(UIOverlayMerchant), "AddItem", null, "CreateDeliveryCard");
            Replace(typeof(OverlayManager), "Confirm", null, "LoadScene");
            Replace(typeof(OverlayManager), "Cancel", null, "LoadScene");
            Replace(typeof(GameCamera), "StartCutscene", null, "LoadScene");
            var commissions = Package(external, "CaravanCommissions");
            var scheduledArrival = commissions.GetType("CaravanCommissions.Plugin+ScheduledArrival");
            if (scheduledArrival != null)
            {
                var observer = new Harmony("gkj.sfk.caravancommissions");
                observer.CreateClassProcessor(scheduledArrival).Patch();
                var picker = mod.GetType("KingdomCharters.CharterPicker");
                var menu = FormatterServices.GetUninitializedObject(picker);
                Smoke.Check(((string)Call(picker, "Terms", menu, 1)).Contains("Another mod controls arrivals"),
                    "Commissions arrival observer discloses retained scheduling");
                observer.UnpatchSelf();
            }
            Caravan(commissions, false);
            var daily = Package(external, "DailyCaravan");
            var dailyPlugin = daily.GetType("DailyCaravan.DailyCaravanPlugin");
            var metadata = (BepInPlugin)dailyPlugin.GetCustomAttributes(typeof(BepInPlugin), false)[0];
            Chainloader.PluginInfos.Add(metadata.GUID, new PluginInfo());
            Smoke.Check(!(bool)AccessTools.Property(pluginType, "Merchant").GetValue(null, null),
                "restored Merchant suspends its fee and schedule when Daily Caravan is registered");
            var visit = daily.GetType("DailyCaravan.Visit", true);
            Smoke.Check((bool)Call(visit, "AddsVisit", null, 16, 16, true, true, false),
                "Daily Caravan binary permits the unlocked day-16 visit");
            var owner = FormatterServices.GetUninitializedObject(dailyPlugin);
            AccessTools.Field(dailyPlugin, "Instance").SetValue(null, owner);
            WorldManager.Instance = SceneObject<WorldManager>();
            Set(owner, "scene", GameDataManager.Instance);
            dailyVisit = Activator.CreateInstance(visit, true);
            Set(owner, "Visit", dailyVisit);
            var arrivalType = daily.GetType("DailyCaravan.Arrival", true);
            var arrival = Activator.CreateInstance(arrivalType, true);
            Set(owner, "arrival", arrival);
            var request = AccessTools.Method(arrivalType, "Request");
            var requestArgs = request.GetParameters().Select(p => (object)Type.Missing).ToArray();
            requestArgs[0] = dailyVisit;
            requestArgs[1] = 1;
            request.Invoke(arrival, requestArgs);
            Smoke.Check(Call(arrivalType, "Take", arrival, dailyVisit, false).ToString() == "StartVisit",
                "Daily Caravan dispatches a supplemental visit with a free guest slot");
            var dailyPatches = new Harmony(metadata.GUID);
            var inventory = daily.GetType("DailyCaravan.CaravanPatches+Inventory");
            if (inventory != null) dailyPatches.CreateClassProcessor(inventory).Patch();
            // Compare the replay classification without our simulated item-spawn hook.
            var own = new Harmony("gkj.sfk.kingdomcharters");
            own.UnpatchSelf();
            own.PatchAll(mod);
            scene.Unpatch(AccessTools.Method(typeof(ItemManager), "SpawnDragItem"),
                AccessTools.Method(typeof(Interactions), "SpawnFixture"));
            var rules = daily.GetType("DailyCaravan.CaravanPatches");
            if (AccessTools.Method(rules, "HasGrantHooks") != null)
            {
                Smoke.Check(!(bool)Call(rules, "HasGrantHooks", null),
                    "Kingdom Charters preserves Daily Caravan native reward replay classification");
            }
            Replace(typeof(ItemManager), "SpawnDragItem", null, "SpawnFixture");
            Caravan(commissions, true);
            dailyPatches.UnpatchSelf();
            dailyVisit = null;
            Chainloader.PluginInfos.Remove(metadata.GUID);
            carrier.UnpatchSelf();
        }
        scene.UnpatchSelf();
    }
}
