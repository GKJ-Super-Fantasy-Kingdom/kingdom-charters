using System;

namespace KingdomCharters
{
    internal enum Charter { Standard, Merchant, Forester, Mason }

    [Serializable]
    internal sealed class CharterState
    {
        public Charter choice;
        public int debt;
        public bool openingComplete;
        // The original scheduled day of the first visit that actually arrived.
        public int firstVisit;

        internal static CharterState Start(Charter choice) => new CharterState
        {
            choice = choice,
            debt = choice == Charter.Forester ? 2 : choice == Charter.Mason ? 3 : 0
        };

        internal bool Valid => choice >= Charter.Merchant && choice <= Charter.Mason &&
            debt >= 0 && debt <= Start(choice).debt && firstVisit >= 0;

        internal static readonly string[] Names =
            { "Standard Run", "Merchant Charter", "Forester’s Charter", "Mason’s Charter" };
        internal static readonly string[] Terms =
        {
            "Play with the usual starting resources and caravan schedule.",
            "Your first scheduled caravan can arrive one day early if the gate is open and the guest slot is free. Each caravan item you accept costs 1 extra coin.",
            "Start with 3 extra wood. The first 2 stone added to your stores after starting supplies go to the charter.",
            "Start with 2 extra stone. The first 3 wood added to your stores after starting supplies go to the charter."
        };

        // A missed early visit keeps its original appointment. Later appointments stay on schedule.
        internal bool VisitToday(int day, bool vanilla, bool scheduledTomorrow)
        {
            if (firstVisit == 0) return vanilla || scheduledTomorrow;
            return vanilla && day != firstVisit;
        }
    }
}
