using System;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;
using SuperFantasyKingdom.UI;

namespace KingdomCharters
{
    internal static class Supplies
    {
        private static int balance;
        private static bool collecting;
        private static bool opening;
        private static int openingScopes;
        private static ResourceType gift, owed;
        internal static ResourceType OwedSlot => Plugin.Instance.Run.choice == Charter.Forester ? ResourceType.Stone : ResourceType.Wood;
        internal static string OwedName => MaterialName(OwedSlot);

        private static string MaterialName(ResourceType slot)
        {
            var data = RaceManager.Instance.GetResourceData(slot);
            return data.stringTitle == null ? data.type.ToString() : data.GetName();
        }

        // The race's wood/stone slots can refer to different inventory keys. An absent
        // store cannot support both sides of the trade, so never issue a one-sided gift.
        internal static void BindRun()
        {
            var run = Plugin.Instance.Run;
            gift = owed = ResourceType.None;
            if (run == null || (run.choice != Charter.Forester && run.choice != Charter.Mason)) return;
            var race = RaceManager.Instance;
            var wood = race.GetResourceData(ResourceType.Wood)?.type ?? ResourceType.None;
            var stone = race.GetResourceData(ResourceType.Stone)?.type ?? ResourceType.None;
            if (wood == stone || wood == ResourceType.None || stone == ResourceType.None ||
                race.GetBuildingTypeToTakeFrom(wood) == BuildingType.None ||
                race.GetBuildingTypeToTakeFrom(stone) == BuildingType.None)
            {
                Plugin.Instance.UnsupportedMaterials();
                return;
            }
            gift = run.choice == Charter.Forester ? wood : stone;
            owed = run.choice == Charter.Forester ? stone : wood;
            ResetBalance();
        }

        internal static string MaterialTerms(Charter choice)
        {
            if (!HasMaterialPair) return "This kingdom needs two distinct stored materials for a supply charter. Continue with Standard Run.";
            string woodName = MaterialName(ResourceType.Wood);
            string stoneName = MaterialName(ResourceType.Stone);
            return choice == Charter.Forester
                ? $"Gain 3 {woodName}. Repay 2 {stoneName} from later deliveries."
                : $"Gain 2 {stoneName}. Repay 3 {woodName} from later deliveries.";
        }

        // The selected kingdom's RaceManager lives in its gameplay scene. Let the
        // player see its actual material names before applying the opening grant.
        [HarmonyPatch(typeof(DialogueCollection), nameof(DialogueCollection.Trigger))]
        private static class ReviewMaterials
        {
            private static void Prefix(ref Action<bool> callback)
            {
                var run = Plugin.Instance.Run;
                if (run == null || run.openingComplete || run.choice == Charter.Merchant ||
                    !(callback?.Target is King) || callback.Method.Name != "AfterDialogue") return;
                var next = callback;
                callback = accepted =>
                {
                    var overlay = OverlayManager.Instance;
                    var message = overlay.OverlayMessage();
                    bool answered = false;
                    void Continue(bool apply)
                    {
                        if (answered) return;
                        answered = true;
                        if (!apply) { Plugin.Instance.Run = null; BindRun(); }
                        overlay.Confirm();
                        next(accepted);
                    }
                    message.SetMessage(CharterState.Names[(int)run.choice] + "\n" + MaterialTerms(run.choice));
                    if (HasMaterialPair)
                    {
                        message.SetCancel("Standard Run", () => Continue(false));
                        message.SetAccept("Accept Charter", () => Continue(true));
                    }
                    else
                    {
                        message.RemoveCancel();
                        message.SetAccept("Standard Run", () => Continue(false));
                    }
                };
            }
        }

        internal static bool HasMaterialPair => owed != ResourceType.None;

        private static BuildingCity Store(ResourceType type) =>
            CityManager.Instance.GetBuilding(RaceManager.Instance.GetBuildingTypeToTakeFrom(type));

        private static int Stock(ResourceManager resources, BuildingCity store, ResourceType type)
        {
            if (store != null) return store.GetResourceQuantity(type, false);
            return resources.GetStartingResourcesDictionary().TryGetValue(type, out var quantity) ? quantity : 0;
        }

        private static void ResetBalance()
        {
            if (HasMaterialPair) balance = Stock(ResourceManager.Instance, Store(owed), owed);
        }

        [HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.GiveStartingResources))]
        private static class Opening
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(out bool __state)
            {
                __state = opening;
                opening = true;
            }

            // Finalizers run after the other mods' ordinary prefixes and postfixes.
            private static void Finalizer(ResourceManager __instance, bool __state, Exception __exception)
            {
                try
                {
                    var run = Plugin.Instance.Run;
                    if (__state || __exception != null || !HasMaterialPair || run == null) return;
                    if (!run.openingComplete)
                    {
                        run.openingComplete = true;
                        int amount = run.choice == Charter.Forester ? 3 : 2;
                        var store = Store(gift);
                        if (store != null) store.GenerateResources(gift, amount);
                        else
                        {
                            __instance.RegisterStartingResource(new ResourceAmount(gift, amount));
                            __instance.ResourceAmountChanged(gift);
                        }
                    }
                    ResetBalance();
                }
                finally { opening = __state; }
            }
        }

        // Moving already-owned starting stock into a newly built store is not production.
        [HarmonyPatch]
        private static class OpeningScope
        {
            private static System.Reflection.MethodBase[] TargetMethods() => new[] {
                AccessTools.Method(typeof(UIOverlayStarterResources), nameof(UIOverlayStarterResources.StartGame)),
                AccessTools.Method(typeof(King), "AfterDialogue"),
                AccessTools.Method(typeof(ResourceManager), "GiveStartingFood")
            };
            [HarmonyPriority(Priority.First)]
            private static void Prefix() => openingScopes++;
            private static void Finalizer()
            {
                if (--openingScopes == 0 && !opening && Plugin.Instance.Run?.debt > 0) ResetBalance();
            }
        }

        [HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.ResourceAmountChanged))]
        private static class Collect
        {
            private static void Prefix(ResourceManager __instance, ResourceType type)
            {
                var run = Plugin.Instance.Run;
                if (openingScopes != 0 || opening || collecting || run == null || run.debt == 0 || !run.openingComplete ||
                    !HasMaterialPair || type != owed || GameManager.Instance == null || !GameManager.Instance.IsGameLoaded()) return;
                var store = Store(type);
                int now = Stock(__instance, store, type);
                int payment = Math.Min(run.debt, now - balance);
                balance = now;
                if (payment <= 0) return;
                collecting = true;
                try
                {
                    if (store == null) __instance.GetStartingResourcesDictionary()[type] = now - payment;
                    else TakeFromStorage(store.GetStorage(), type, payment);
                    balance = Stock(__instance, store, type);
                    run.debt -= Math.Min(payment, Math.Max(0, now - balance));
                }
                finally { collecting = false; }
            }
        }

        private static readonly AccessTools.FieldRef<ResourcePacket, int> PacketAmount =
            AccessTools.FieldRefAccess<ResourcePacket, int>("m_Amount");

        private static void TakeFromStorage(ResourceStorage storage, ResourceType type, int payment)
        {
            if (!storage.ShouldGenerateResourcePackets())
            {
                storage.RemoveResourceWithoutPackage(type, payment);
                return;
            }
            while (payment > 0)
            {
                var packet = storage.FindFreeResourcePacket(type);
                if (packet == null || packet.GetAmount() <= 0) return;
                int take = Math.Min(packet.GetAmount(), payment);
                if (take == packet.GetAmount()) storage.TakeResourcePacket(packet.GetIdentifier());
                else
                {
                    // Keep references to the remaining packet and its storage count consistent.
                    PacketAmount(packet) -= take;
                    storage.RemoveResourceWithoutPackage(type, take);
                }
                payment -= take;
            }
        }
    }
}
