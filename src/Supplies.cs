using System;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Buildings;

namespace KingdomCharters
{
    internal static class Supplies
    {
        private static int balance;
        private static bool collecting;
        private static ResourceType Owed(CharterState run) =>
            run.choice == Charter.Forester ? ResourceType.Stone : ResourceType.Wood;

        internal static void ResetBalance()
        {
            var run = Plugin.Instance.Run;
            balance = run != null && run.debt > 0
                ? ResourceManager.Instance.GetResourceAvailableAmount(Owed(run), false) : 0;
        }

        [HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.GiveStartingResources))]
        private static class Opening
        {
            private static void Postfix(ResourceManager __instance)
            {
                var run = Plugin.Instance.Run;
                if (run == null || run.openingComplete) return;
                if (run.choice == Charter.Forester) __instance.AddResource(ResourceType.Wood, 3);
                if (run.choice == Charter.Mason) __instance.AddResource(ResourceType.Stone, 2);
                ResetBalance();
                run.openingComplete = true;
            }
        }

        [HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.ResourceAmountChanged))]
        private static class Collect
        {
            private static void Prefix(ResourceManager __instance, ResourceType type)
            {
                var run = Plugin.Instance.Run;
                if (collecting || run == null || run.debt == 0 || !run.openingComplete ||
                    type != Owed(run) || GameManager.Instance == null || !GameManager.Instance.IsGameLoaded()) return;
                int now = __instance.GetResourceAvailableAmount(type, false);
                int payment = Math.Min(run.debt, now - balance);
                balance = now;
                if (payment <= 0) return;
                collecting = true;
                try
                {
                    var building = CityManager.Instance.GetBuilding(RaceManager.Instance.GetBuildingTypeToTakeFrom(type));
                    if (building == null)
                    {
                        // Native spending also handles materials held for a future building.
                        __instance.SpendResource(type, payment);
                    }
                    else
                    {
                        TakeFromStorage(building.GetStorage(), type, payment);
                    }
                    balance = __instance.GetResourceAvailableAmount(type, false);
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
