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
            "First caravan one day early if the gate and guest slot are open. Pay 1 extra coin per immediate pick. Prepaid deliveries are free.",
            "Gain 3 of the kingdom’s wood equivalent. Repay 2 of its stone equivalent. Review materials at run start.",
            "Gain 2 of the kingdom’s stone equivalent. Repay 3 of its wood equivalent. Review materials at run start."
        };

        // A missed early visit keeps its original appointment. Later appointments stay on schedule.
        internal bool VisitToday(int day, bool vanilla, bool scheduledTomorrow)
        {
            if (firstVisit == 0) return vanilla || scheduledTomorrow;
            return vanilla && day != firstVisit;
        }
    }
}
