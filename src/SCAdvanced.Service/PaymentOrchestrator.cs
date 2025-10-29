using SCAdvanced.Core.Contracts;
using SCAdvanced.Core.Emun;
using SCAdvanced.Core.Model;
using SCAdvanced.Core.Model.Emun;

namespace SCAdvanced.Service
{
    public sealed class PaymentOrchestrator : IPaymentOrchestrator
    {
        private readonly IBillAcceptorPort _port;

        private PaymentStatus _status = PaymentStatus.Running;
        private decimal _paid = 0m;
        private readonly Dictionary<decimal, int> _notes = new();
        private Note? _escrowNote;              
        private PaymentOptions _opts = new();
        private decimal _targetTotal = 0m;
        private byte[]? _lastRawData;
        private bool _awaitingPostConfirm = false;  
        private bool _escrowCycleActive = false;   
        private DateTime _squelchUntilUtc = DateTime.MinValue;
        public PaymentOrchestrator(IBillAcceptorPort port) => _port = port;

        public event EventHandler<NoteEventArgs>? Escrow;
        public event EventHandler<NoteEventArgs>? Stacked;
        public event EventHandler<Core.Model.ErrorEventArgs>? Error;
        public event EventHandler<PaymentResult>? Completed;

        public PaymentStatus CurrentStatus => _status;
        public decimal AmountPaid => _paid;
        public IReadOnlyDictionary<decimal, int> Notes => _notes;

        public void StartOperation(decimal total, PaymentOptions options)
        {
            _targetTotal = total;
            _opts = options ?? new PaymentOptions();
            _port.Open();
            _status = PaymentStatus.Running;
            _paid = 0m; _notes.Clear(); _escrowNote = null;

            _port.ConfigureInhibits(_opts.AcceptedDenominations);
            _port.EnableExtendedEscrowPupA();

            _port.EnableAccept();
        }

        /// <summary>
        /// Espera até que uma nota entre em ESCROW e retorna seu valor.
        /// Não decide nada; apenas sinaliza para a aplicação.
        /// </summary>
        public Note? WaitForEscrowNote(int timeoutMs, CancellationToken ct = default)
        {
            var deadline = Environment.TickCount + timeoutMs;

            while (!ct.IsCancellationRequested && Environment.TickCount < deadline)
            {
                var pkt = (_port as BillAcceptorAdapter)!.Poll();


                if (_awaitingPostConfirm)
                {
                    if (pkt.MessageType == EbdsMessageType.Type2_OmnibusReply)
                    {

                        _awaitingPostConfirm = false;
                        _escrowCycleActive = false;
                        _escrowNote = null;
                        _squelchUntilUtc = DateTime.UtcNow.AddMilliseconds(600); 
                    }
                    Thread.Sleep(120);
                    continue;
                }

                if (DateTime.UtcNow < _squelchUntilUtc)
                {
                    if (pkt.MessageType == EbdsMessageType.Type2_OmnibusReply)
                    {
                        _squelchUntilUtc = DateTime.MinValue;
                        _escrowCycleActive = false;
                        _escrowNote = null;
                    }
                    Thread.Sleep(120);
                    continue;
                }


                if (pkt.MessageType == EbdsMessageType.Type7_Extended)
                {
                    if (_escrowCycleActive)
                    {
                        Thread.Sleep(120);
                        continue;
                    }

                    if (BillAcceptorAdapter.TryParseExtendedNoteBRL(pkt.Data, out var note))
                    {
                        _escrowNote = note;
                        _escrowCycleActive = true;
                        Escrow?.Invoke(this, new NoteEventArgs(note));
                        return note;
                    }
                }

                if (pkt.MessageType == EbdsMessageType.Type2_OmnibusReply)
                {
                    _escrowCycleActive = false;
                    _escrowNote = null;
                }

                Thread.Sleep(160);
            }
            return null;
        }



        /// <summary>
        /// Decide o que fazer com a nota em ESCROW.
        /// Accept => Stack(), Return => Return()
        /// </summary>
        public NoteProcessResult DecideOnEscrow(EscrowDecision decision, int confirmTimeoutMs = 3000, CancellationToken ct = default)
        {
            if (_escrowNote == null)
                return new NoteProcessResult { Status = NoteProcessStatus.Error, ErrorCode = "NO_ESCROW", Message = "Não há nota em escrow." };
            
            _awaitingPostConfirm = true;    
            
            if (decision == EscrowDecision.Accept)
            {
                _port.Stack();

                var res = WaitForPostCommandConfirm(accept: true, confirmTimeoutMs, ct);
                //_awaitingPostConfirm = false;    
                _squelchUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
                _escrowCycleActive = false;      
                if (res.Status == NoteProcessStatus.Accepted)
                {
                    var n = _escrowNote.Value;
                    _paid += n;
                    _notes[n] = _notes.GetValueOrDefault(n) + 1;
                    Stacked?.Invoke(this, new NoteEventArgs(_escrowNote));
                    _escrowNote = null;

                }
                return res;
            }
            else 
            {
                _port.Return();
                var res = WaitForPostCommandConfirm(accept: false, confirmTimeoutMs, ct);
                _escrowNote = null;
                return res;
            }
        }

        /// <summary> Confirma a operação </summary>
        public PaymentResult ConfirmOperation()
        {
            _port.DisableAccept();

            var change = Math.Max(0m, _paid - _targetTotal);
            Dictionary<decimal, int>? plan = null;

            if (_opts.GivesChange && change > 0m)
            {
                var denoms = _opts.ChangeDenominations.Length > 0 ? _opts.ChangeDenominations : _opts.AcceptedDenominations;
                plan = BuildGreedy(change, denoms);
            }
            else change = 0m;

            var ok = new PaymentResult
            {
                Status = PaymentStatus.Paid,
                AmountPaid = _paid,
                Notes = new(_notes),
                ChangeDue = change,
                ChangeBreakdown = plan
            };
            _status = PaymentStatus.Paid;
            Completed?.Invoke(this, ok);
            return ok;
        }

        /// <summary>
        /// Cancela a operação: devolve a nota em ESCROW (se houver) e informa o valor empilhado para reembolso externo.
        /// </summary>
        public PaymentResult CancelOperation()
        {
            if (_escrowNote != null)
            {
                _port.Return();
                _escrowNote = null;
            }

            _port.DisableAccept();

            var res = new PaymentResult
            {
                Status = PaymentStatus.Cancelled,
                AmountPaid = _paid,
                Notes = new(_notes),
                RefundDue = _paid,
                RefundBreakdown = BuildGreedy(_paid,
                    _opts.ChangeDenominations.Length > 0 ? _opts.ChangeDenominations : _opts.AcceptedDenominations)
            };
            _status = PaymentStatus.Cancelled;
            Completed?.Invoke(this, res);
            return res;
        }


        private NoteProcessResult WaitForPostCommandConfirm(bool accept, int timeoutMs, CancellationToken ct)
        {
            var deadline = Environment.TickCount + timeoutMs;

            while (!ct.IsCancellationRequested && Environment.TickCount < deadline)
            {
                var pkt = (_port as BillAcceptorAdapter)!.Poll();

                if (pkt.MessageType == EbdsMessageType.Type2_OmnibusReply)
                {
                    if (accept)
                        return new NoteProcessResult { Status = NoteProcessStatus.Accepted, Currency = "BRL", Value = _escrowNote?.Value ?? 0m };
                    else
                        return new NoteProcessResult { Status = NoteProcessStatus.Returned, Currency = "BRL", Value = _escrowNote?.Value ?? 0m };
                }

                Thread.Sleep(120);
            }

            return new NoteProcessResult
            {
                Status = NoteProcessStatus.Error,
                ErrorCode = "CONFIRM_TIMEOUT",
                Message = accept ? "Timeout aguardando confirmação de STACKED." : "Timeout aguardando confirmação de RETURNED."
            };
        }

        private static Dictionary<decimal, int>? BuildGreedy(decimal amount, int[] denomInts)
        {
            if (amount <= 0m || denomInts == null || denomInts.Length == 0) return null;
            var denoms = denomInts.Select(d => (decimal)d).OrderByDescending(d => d).ToArray();
            var plan = new Dictionary<decimal, int>();
            var remaining = amount;

            foreach (var d in denoms)
            {
                var q = (int)(remaining / d);
                if (q > 0) { plan[d] = q; remaining -= q * d; }
            }
            if (remaining > 0m && remaining >= 0.01m)
                plan[0.01m] = (int)Math.Round(remaining / 0.01m);
            return plan;
        }
    }
}
