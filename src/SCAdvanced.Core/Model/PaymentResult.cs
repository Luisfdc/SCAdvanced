using SCAdvanced.Core.Emun;

namespace SCAdvanced.Core.Model
{
    public class PaymentResult
    {
        public PaymentStatus Status { get; init; }
        public decimal AmountPaid { get; init; }
        public Dictionary<decimal, int> Notes { get; init; } = new();

        public decimal ChangeDue { get; init; } = 0m;
        public Dictionary<decimal, int>? ChangeBreakdown { get; init; }

        public decimal RefundDue { get; init; } = 0m;
        public Dictionary<decimal, int>? RefundBreakdown { get; init; }

        public string? ErrorMessage { get; init; }
    }
}