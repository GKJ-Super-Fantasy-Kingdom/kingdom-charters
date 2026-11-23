using System;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.UI;

namespace KingdomCharters
{
    internal static class Merchant
    {
        private const int CaravanGuest = 80;
        private static readonly Func<GuestCaravan, int> ScheduledDay =
            AccessTools.MethodDelegate<Func<GuestCaravan, int>>(AccessTools.DeclaredMethod(typeof(GuestCaravan), "GetDay"));

        private static readonly Action<GuestCaravan, int> LoadCaravan =
            AccessTools.MethodDelegate<Action<GuestCaravan, int>>(AccessTools.DeclaredMethod(typeof(GuestCaravan), "OnLoad"));
        private static GuestCaravan candidate, arriving;

        private static bool Scheduled(int day, GuestCaravan guest)
        {
            if (day == ScheduledDay(guest)) return true;
            var race = RaceDataManager.Instance;
            switch (day)
            {
                case 7: return race.GetGuestWeek1() == CaravanGuest;
                case 14: return race.GetGuestWeek2() == CaravanGuest;
                case 21: return race.GetGuestWeek3() == CaravanGuest;
                case 28: return race.GetGuestWeek4() == CaravanGuest;
                default: return false;
            }
        }

        [HarmonyPatch(typeof(GuestCaravan), "CheckDay")]
        private static class Appointment
        {
            private static void Postfix(GuestCaravan __instance, int day, ref bool __result)
            {
                if (!Plugin.Merchant) return;
                bool tomorrow = Scheduled(day + 1, __instance);
                if (!__result && arriving == null && tomorrow && Plugin.Instance.Run.firstVisit == 0)
                    candidate = __instance;
                __result = Plugin.Instance.Run.VisitToday(day, __result, tomorrow && arriving == __instance);
            }
        }

        // Wait for every guest's OnGameLoad before trying the early slot. Day 6, for
        // example, can already belong to the monster hunter ahead of a week-one caravan.
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.ChangeState))]
        private static class FreeSlot
        {
            private static void Postfix(GameState newState)
            {
                if (newState != GameState.Loading) return;
                var guest = candidate;
                candidate = null;
                if (guest == null || !Plugin.Merchant || Plugin.Instance.Run.firstVisit != 0 ||
                    WorldManager.Instance.GetGuest() != null || WorldManager.Instance.HasGuestPlaceHolder() ||
                    !DaytimeManager.Instance.IsLoading() ||
                    !RaceDataManager.Instance.HasRaceEventFlag(RaceEvent.GateOpened)) return;
                arriving = guest;
                try
                {
                    guest.gameObject.SetActive(true);
                    LoadCaravan(guest, DaytimeManager.Instance.CurrentDay());
                }
                finally { arriving = null; }
            }
        }

        [HarmonyPatch(typeof(Guest), "CanSpeakToday")]
        private static class EarlyDialogue
        {
            private static void Postfix(Guest __instance, ref bool __result)
            {
                if (__instance is GuestCaravan && Plugin.Merchant &&
                    Plugin.Instance.Run.firstVisit == DaytimeManager.Instance.CurrentDay() + 1) __result = true;
            }
        }

        [HarmonyPatch(typeof(GuestCaravan), "OnLoad")]
        private static class Arrival
        {
            private static void Postfix(GuestCaravan __instance, int day)
            {
                if (!Plugin.Merchant || Plugin.Instance.Run.firstVisit != 0 ||
                    WorldManager.Instance.GetGuest() != __instance) return;
                Plugin.Instance.Run.firstVisit = Scheduled(day, __instance) ? day : day + 1;
            }
        }

        // Only the cards constructed inside this caravan's inventory callback receive the fee.
        private static UIOverlayMerchant pricing;

        [HarmonyPatch(typeof(GuestCaravan), "Reroll")]
        private static class Inventory
        {
            private static void Prefix(UIOverlayMerchant merchant, out UIOverlayMerchant __state)
            {
                __state = pricing;
                pricing = Plugin.Merchant ? merchant : null;
            }

            private static void Finalizer(UIOverlayMerchant __state) => pricing = __state;
        }

        [HarmonyPatch(typeof(UIOverlayMerchant), nameof(UIOverlayMerchant.AddItem))]
        private static class Price
        {
            private static void Prefix(UIOverlayMerchant __instance, ref ResourceAmount[] costs)
            {
                if (pricing == null || __instance != pricing) return;
                if (costs == null || costs.Length == 0)
                    costs = new[] { new ResourceAmount(ResourceType.Coins, 1) };
                else if (costs.Length == 1 && costs[0].type == ResourceType.Coins)
                    costs = new[] { new ResourceAmount(ResourceType.Coins, costs[0].amount + 1) };
            }
        }
    }
}
