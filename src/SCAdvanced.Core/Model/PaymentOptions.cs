namespace SCAdvanced.Core.Model
{
    public class PaymentOptions
    {
        public TimeSpan InactivityTimeout { get; init; } = TimeSpan.FromSeconds(60);
        public int[] AcceptedDenominations { get; init; } = new[] { 2, 5, 10, 20, 50, 100, 200 };

        public bool GivesChange { get; init; } = false;             // máquina dá troco?
        public decimal MaxChangeAllowed { get; init; } = 20m;       
        public int[] ChangeDenominations { get; init; } = Array.Empty<int>();
        public decimal MaxSessionAmount { get; init; } = 5000m;
        public bool UseExtendedMode { get; init; } = true;
        public bool ManualDecisionPerNote { get; init; } = true;
    }
}