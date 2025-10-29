using SCAdvanced.Core.Emun;
using SCAdvanced.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCAdvanced.Core.Contracts
{
    public interface IPaymentOrchestrator
    {
        //PaymentResult StartSession(decimal total, PaymentOptions options, CancellationToken ct = default);
        PaymentStatus CurrentStatus { get; }
        decimal AmountPaid { get; }
        IReadOnlyDictionary<decimal, int> Notes { get; }

        event EventHandler<NoteEventArgs> Escrow;
        event EventHandler<NoteEventArgs> Stacked;
        event EventHandler<Model.ErrorEventArgs> Error;
        event EventHandler<PaymentResult> Completed;
    }
}
