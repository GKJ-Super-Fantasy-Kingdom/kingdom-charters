using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;

internal static class Smoke
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Check(bool value, string what)
    {
        if (!value) throw new Exception(what);
        Console.WriteLine("PASS " + what);
    }

    private static void Rules(Assembly mod)
    {
        var state = mod.GetType("KingdomCharters.CharterState", true);
        var choice = mod.GetType("KingdomCharters.Charter", true);
        var start = state.GetMethod("Start", All);
        var debt = state.GetField("debt");
        int[] expected = { 0, 0, 2, 3 };
        for (int i = 0; i < expected.Length; i++)
        {
            var run = start.Invoke(null, new[] { Enum.ToObject(choice, i) });
            Check((int)debt.GetValue(run) == expected[i], "opening debt for " + Enum.GetName(choice, i));
        }
        var merchant = start.Invoke(null, new[] { Enum.Parse(choice, "Merchant") });
        var visit = state.GetMethod("VisitToday", All);
        Check((bool)visit.Invoke(merchant, new object[] { 15, false, true }), "eligible early appointment");
        Check((bool)visit.Invoke(merchant, new object[] { 16, true, false }), "missed early appointment keeps scheduled visit");
        state.GetField("firstVisit").SetValue(merchant, 16);
        Check(!(bool)visit.Invoke(merchant, new object[] { 16, true, false }), "no duplicate appointment");
        Check((bool)visit.Invoke(merchant, new object[] { 21, true, false }), "later weekly visit retained");
        Check(!(bool)visit.Invoke(merchant, new object[] { 20, false, true }), "later weekly visit stays on time");
    }

    private static void Storage(Assembly mod)
    {
        var game = Assembly.Load("Assembly-CSharp");
        var storage = game.GetType("SuperFantasyKingdom.ResourceStorage", true);
        var packet = game.GetType("SuperFantasyKingdom.ResourcePacket", true);
        var type = game.GetType("SuperFantasyKingdom.ResourceType", true);
        var stone = Enum.Parse(type, "Stone");
        var vector = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector2", true);
        var stock = FormatterServices.GetUninitializedObject(storage);
        var amounts = new int[Enum.GetValues(type).Length];
        var listField = storage.GetField("m_Resources", All);
        var list = (IList)Activator.CreateInstance(listField.FieldType);
        var batch = Activator.CreateInstance(packet, new object[] { 19, stone, Activator.CreateInstance(vector), 3 });
        list.Add(batch);
        amounts[(int)stone] = 3;
        listField.SetValue(stock, list);
        storage.GetField("m_ResourcesAmount", All).SetValue(stock, amounts);
        storage.GetField("generateResourcePackets", All).SetValue(stock, true);
        var take = mod.GetType("KingdomCharters.Supplies").GetMethod("TakeFromStorage", All);
        take.Invoke(null, new object[] { stock, stone, 2 });
        Check(amounts[(int)stone] == 1 && (int)packet.GetMethod("GetAmount").Invoke(batch, null) == 1,
            "partial material payment preserves packet and count");
        packet.GetMethod("Reserve").Invoke(batch, null);
        take.Invoke(null, new object[] { stock, stone, 1 });
        Check(amounts[(int)stone] == 1, "reserved material is preserved");
        packet.GetMethod("Free").Invoke(batch, null);
        take.Invoke(null, new object[] { stock, stone, 1 });
        Check(amounts[(int)stone] == 0 && list.Count == 0, "final payment removes a whole packet");
        storage.GetField("generateResourcePackets", All).SetValue(stock, false);
        amounts[(int)stone] = 4;
        take.Invoke(null, new object[] { stock, stone, 2 });
        Check(amounts[(int)stone] == 2, "bulk storage payment");
    }

    public static int Main(string[] args)
    {
        try
        {
            var mod = Assembly.LoadFrom(args[0]);
            string version = mod.GetName().Version.ToString(3);
            var plugin = mod.GetType("KingdomCharters.Plugin", true).GetCustomAttributesData()
                .Single(a => a.AttributeType.Name == "BepInPlugin");
            Check(mod.GetName().Name == "KingdomCharters" && version == args[1] &&
                plugin.ConstructorArguments[2].Value.ToString() == version, "packaged DLL and plugin versions");
            Rules(mod);
            Storage(mod);
            var harmony = new Harmony("gkj.sfk.kingdomcharters");
            Interactions.ConfigureLoader(mod);
            harmony.PatchAll(mod);
            var targets = harmony.GetPatchedMethods().ToArray();
            foreach (var m in targets) Console.WriteLine("PATCH " + m.DeclaringType.Name + "." + m.Name);
            Check(targets.Length == 25, "25 patches installed against the game assemblies");
            Interactions.Run(mod, args[2]);
            harmony.UnpatchSelf();
            Check(!harmony.GetPatchedMethods().Any(), "patches removed");
            return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
