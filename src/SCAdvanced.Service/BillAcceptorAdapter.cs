using System.Text;
using SCAdvanced.Core.Contracts;
using SCAdvanced.Core.Model;


namespace SCAdvanced.Service
{
    public sealed class BillAcceptorAdapter : IBillAcceptorPort
    {
        private readonly EbdsClient _cli;
        private volatile bool _open;

        public BillAcceptorAdapter(EbdsClient client) => _cli = client;

        public event EventHandler<NoteEventArgs>? Escrow;
        public event EventHandler<NoteEventArgs>? Stacked;
        public event EventHandler<Core.Model.ErrorEventArgs>? Error;

        public void Open()
        {
            if (_open) return;
            _cli.Open(); 
            //_cli.PollOmnibus(escrowMode: true, extended: true, pupA: true);
            _cli.PollOmnibus(escrowMode: true, extended: true, pupA: true, baseEnablesMask: 0x7F);
            _open = true;
        }

        public void Close()
        {
            if (!_open) return;
            _cli.Dispose();
            _open = false;
        }

        public DeviceInfo GetInfo()
        {
            var serial = _cli.QuerySerialNumber();
            var variant = _cli.QueryVariantName();
            var fw = "<n/a>";
            return new DeviceInfo(serial, variant, fw);
        }

        public void ConfigureInhibits(int[] acceptedDenominations)
        {
            var bits = new byte[19]; 
            foreach (var v in acceptedDenominations)
            {
                byte idx = v switch { 2 => 0x01, 5 => 0x02, 10 => 0x03, 20 => 0x04, 50 => 0x05, 100 => 0x06, 200 => 0x07, _ => (byte)0x00 };
                if (idx == 0) continue;
                int byteIndex = idx / 8; int bit = idx % 8;
                bits[byteIndex] |= (byte)(1 << bit);
            }
            _cli.Type7(0x03, bits); 
        }

        public void EnableExtendedEscrowPupA() => _cli.PollOmnibus(escrowMode: true, extended: true, pupA: true);
        public void EnableAccept() => _cli.PollOmnibus(escrowMode: true, extended: true, pupA: true);
        public void DisableAccept()
        {
            _cli.SetExtendedInhibitsAllowNone();
            _cli.PollOmnibus(escrowMode: true, extended: true, pupA: true, baseEnablesMask: 0x00);
        }

        public void Stack() => _cli.StackNow(escrowMode: true, extended: true, pupA: true);
        public void Return() => _cli.ReturnNow(escrowMode: true, extended: true, pupA: true);

        public EbdsPacket Poll() => _cli.PollOmnibus(escrowMode: true, extended: true, pupA: true);

        public static bool TryParseExtendedNoteBRL(byte[] payload, out Note note)
        {
            note = default!;
            if (payload == null || payload.Length < 8) return false;
            for (int i = 0; i <= payload.Length - 9; i++)
            {
                if (payload[i] == 'B' && payload[i + 1] == 'R' && payload[i + 2] == 'L' && payload[i + 6] == '+')
                {
                    var mant = Encoding.ASCII.GetString(payload, i + 3, 3);
                    var expS = Encoding.ASCII.GetString(payload, i + 7, 2);
                    if (int.TryParse(mant, out int m) && int.TryParse(expS, out int e))
                    {
                        int units = (int)(m * Math.Pow(10, e));
                        note = new Note(units, "BRL");
                        return true;
                    }
                }
            }
            return false;
        }

        public void Dispose() => Close();
    }
}
