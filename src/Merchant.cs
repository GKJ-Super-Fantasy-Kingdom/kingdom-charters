using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.UI;
using TMPro;
using UnityEngine;

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

        private static readonly MethodInfo CheckDay = AccessTools.DeclaredMethod(typeof(GuestCaravan), "CheckDay");
        private static readonly MethodInfo OnLoad = AccessTools.DeclaredMethod(typeof(GuestCaravan), "OnLoad");
        private static readonly MethodInfo WeeklyGuest = AccessTools.DeclaredMethod(typeof(WorldManager), "IsTodayGuest");

        // A custom eligibility patch can depend on state other than tomorrow's date.
        // Let its owner schedule the visit; the item fee remains independent.
        private static bool CustomSchedule(MethodInfo method)
        {
            var patches = Harmony.GetPatchInfo(method);
            if (patches == null) return false;
            foreach (var owner in patches.Owners)
                if (owner != Plugin.Id) return true;
            return false;
        }
        internal static bool OtherSchedule => CustomSchedule(CheckDay) || CustomSchedule(OnLoad) || CustomSchedule(WeeklyGuest);

        [HarmonyPatch(typeof(GuestCaravan), "CheckDay")]
        private static class Appointment
        {
            private static void Postfix(GuestCaravan __instance, int day, ref bool __result)
            {
                if (!Plugin.Merchant || OtherSchedule) return;
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
                Plugin.Instance.Run.firstVisit = arriving == __instance ? day + 1 : day;
            }
        }

        // Commissions builds delivery cards in its Reroll prefix and skips vanilla Reroll.
        private static UIOverlayMerchant pricing;
        [HarmonyPatch(typeof(GuestCaravan), "Reroll")]
        private static class Inventory
        {
            [HarmonyPriority(Priority.First)]
            [HarmonyBefore("gkj.sfk.caravancommissions")]
            private static void Prefix(UIOverlayMerchant merchant, out UIOverlayMerchant __state)
            {
                __state = pricing;
                pricing = Plugin.Merchant ? merchant : null;
            }

            private static void Finalizer(UIOverlayMerchant __state) => pricing = __state;
        }

        private sealed class Offer
        {
            internal string Item;
            internal long CoinsRequired;
            internal bool Paid;

            internal bool CanSelect(int balance) => !Paid && balance >= CoinsRequired;
        }

        private static readonly ConditionalWeakTable<UICard, Offer> offers = new ConditionalWeakTable<UICard, Offer>();
        private static Offer selecting;
        private static readonly AccessTools.FieldRef<UICardItem, TextMeshProUGUI> Description =
            AccessTools.FieldRefAccess<UICardItem, TextMeshProUGUI>("description");

        // Leave the base cost array, affordability rules and purchase callbacks with their
        // owner. The native buy button only displays/debits costs[0], and a commission
        // delivery deliberately disables that debit callback.
        [HarmonyPatch(typeof(UIOverlayMerchant), nameof(UIOverlayMerchant.AddItem))]
        private static class Price
        {
            private static void Postfix(UIOverlayMerchant __instance, GameObject __result, ResourceAmount[] costs, Item item)
            {
                if (!ReferenceEquals(pricing, __instance) || __result == null) return;
                var card = __result.GetComponent<UICardItem>();
                if (card == null) return;
                var fee = RegisterImmediatePick(__instance, card, item.GetIdentifier(), costs);
                if (!fee.HasValue) return;
                var description = Description(card);
                if (description == null) return;
                description.text = (fee == 1 ? "Merchant Charter: 1 coin" :
                    $"Merchant Charter: +1 coin ({fee} coins required)") + "\n" + description.text;
            }
        }

        private static readonly Func<UIOverlayMerchant, bool> UsesDefaultPick =
            AccessTools.MethodDelegate<Func<UIOverlayMerchant, bool>>(AccessTools.DeclaredMethod(typeof(UIOverlayMerchant), "UseCardSelectCallback"));

        private static long? RegisterImmediatePick(UIOverlayMerchant screen, UICardItem card, string id, ResourceAmount[] costs)
        {
            // Prepaid/custom reward cards take over fulfillment. Older Commissions
            // builds these with AddItem and the default pick disabled; newer builds
            // create their own delivery cards. Neither is a new immediate purchase.
            if (!ReferenceEquals(pricing, screen) || !UsesDefaultPick(screen) || offers.TryGetValue(card, out _))
                return null;
            long required = RequiredCoins(costs);
            offers.Add(card, new Offer { Item = id, CoinsRequired = required });
            return required;
        }

        internal static long RequiredCoins(ResourceAmount[] costs)
        {
            long coins = 1;
            if (costs != null)
                foreach (var cost in costs)
                    if (cost.type == ResourceType.Coins && cost.amount > 0) coins += cost.amount;
            return coins;
        }

        [HarmonyPatch(typeof(UIOverlayMerchant), "Select", typeof(UICard))]
        private static class SelectCard
        {
            private static bool Prefix(UICard selected) =>
                !offers.TryGetValue(selected, out var offer) || offer.CanSelect(ResourceManager.Instance.GetCoins());
        }

        [HarmonyPatch(typeof(UICard), nameof(UICard.Callback))]
        private static class Dispatch
        {
            private static bool Prefix(UICard __instance, out Offer __state)
            {
                __state = selecting;
                selecting = null;
                if (!offers.TryGetValue(__instance, out var offer)) return true;
                if (offer.Paid || __state == offer) return false;
                if (!offer.CanSelect(ResourceManager.Instance.GetCoins()))
                {
                    // The selection animation has hidden the buttons. Release the overlay
                    // without invoking any purchase or delivery callback.
                    OverlayManager.Instance.CloseOverlay();
                    return false;
                }
                selecting = offer;
                return true;
            }

            private static void Finalizer(Offer __state) => selecting = __state;
        }

        [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.SpawnDragItem))]
        private static class Delivered
        {
            // Daily Caravan inspects grant hooks to decide whether a dawn reward can
            // be replayed. A suspended Merchant must leave that decision untouched.
            [HarmonyPrepare]
            private static bool Prepare() => !Plugin.DailyCaravan;

            private static void Prefix(List<DragItem> ___m_DragItems, out int __state) =>
                __state = selecting == null ? -1 : ___m_DragItems.Count;

            private static void Postfix(string itemDataName, bool fromSave, List<DragItem> ___m_DragItems, int __state)
            {
                var offer = selecting;
                if (__state < 0 || offer == null || offer.Paid || fromSave || offer.Item != itemDataName ||
                    ___m_DragItems.Count <= __state) return;
                offer.Paid = true;
                ResourceManager.Instance.AddToCoins(-1);
                ResourceManager.Instance.ResourceAmountChanged(ResourceType.Coins);
            }
        }
    }
}
