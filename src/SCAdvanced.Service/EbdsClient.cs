using SCAdvanced.Core.Model;
using SCAdvanced.Core.Model.Emun;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;

namespace SCAdvanced.Service
{
    public sealed class EbdsClient : IDisposable
    {
        private readonly SerialPort _port;
        private readonly object _ioLock = new();
        private volatile bool _nextAckBit;
        private readonly byte _routeBits = 0x00;
        public string ComPort => _port.PortName;

        public EbdsClient(string comPort = null,
                          int baud = 9600,
                          Parity parity = Parity.Even,
                          int dataBits = 7,
                          StopBits stopBits = StopBits.One)
        {
            comPort ??= GetPortName();

            _port = new SerialPort(comPort, baud, parity, dataBits, stopBits)
            {
                ReadTimeout = 5000,
                WriteTimeout = 800,
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true
            };
        }

        public void Open()
        {
            if (!_port.IsOpen) _port.Open();
        }

        public void Dispose()
        {
            try { if (_port.IsOpen) _port.Close(); } catch { /* ignore */ }
            _port.Dispose();
        }
        static string GetPortName()
        {
            string foundPort = null;

            foreach (var portName in SerialPort.GetPortNames())
            {
                try
                {
                    using var port = new SerialPort(portName, 9600, Parity.Even, 7, StopBits.One)
                    {
                        ReadTimeout = 500,
                        WriteTimeout = 500
                    };
                    port.Open();
                    // “ping” mínimo (qualquer frame EBDS válido serve — aqui um placeholder do seu projeto)
                    port.Write(new byte[] { 0x02, 0x03, 0x60, 0x00, 0x9D }, 0, 5);
                    foundPort = portName;
                    port.Close();
                    break;
                }
                catch { Console.WriteLine($"{portName} não respondeu."); }
            }

            if (foundPort is null)
                throw new Exception("Nenhum validador encontrado. Conecte o SC Advance e execute novamente.");

            return foundPort;
        }

        private byte BuildCtrl(EbdsMessageType type)
        {
            byte ctrl = (byte)(((byte)type << 4) | (_routeBits & 0x0E));
            if (_nextAckBit) ctrl |= 0x01;
            return ctrl;
        }

        private byte[] ReadExact(int count)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = _port.Read(buf, read, count - read);
                if (n <= 0) throw new TimeoutException("Timeout lendo serial.");
                read += n;
            }
            return buf;
        }

        private EbdsPacket ReadFrame()
        {
            int b;
            int deadline = Environment.TickCount + _port.ReadTimeout;

            while (true)
            {
                b = _port.ReadByte();
                if (b == EbdsPacket.STX) break;  // achou STX
                if (b == 0x05) continue;         // ENQ (alguns firmwares emitem)
                if (Environment.TickCount > deadline)
                    throw new TimeoutException("Timeout aguardando STX/ENQ.");
            }

            int len = _port.ReadByte();
            if (len < 5) throw new InvalidOperationException($"LEN inválido ({len}).");

            byte[] rest = ReadExact(len - 2); // LEN já inclui ETX/CHK
            byte[] full = new byte[len];
            full[0] = EbdsPacket.STX; full[1] = (byte)len;
            Array.Copy(rest, 0, full, 2, rest.Length);

            return EbdsPacket.Parse(full);
        }

        private EbdsPacket Exchange(EbdsMessageType type, ReadOnlySpan<byte> data, int retry = 2)
        {
            byte ctrl = BuildCtrl(type);
            byte[] cmd = EbdsPacket.Build(ctrl, data);

            for (int attempt = 0; attempt <= retry; attempt++)
            {
                lock (_ioLock)
                {
                    _port.DiscardInBuffer();
                    _port.Write(cmd, 0, cmd.Length);
                    Thread.Sleep(30);

                    var reply = ReadFrame();

                    // Verifica ACK/NAK (toggle no bit0 do CTRL)
                    bool deviceAcked = ((reply.Ctrl & 0x01) == (_nextAckBit ? 0x01 : 0x00));
                    if (deviceAcked)
                    {
                        _nextAckBit = !_nextAckBit;
                        return reply;
                    }

                    if (attempt == retry)
                        throw new IOException("NAK do dispositivo após múltiplas tentativas.");

                    Thread.Sleep(50);
                }
            }
            throw new IOException("Falha inesperada em Exchange().");
        }

        private static (byte d0, byte d1, byte d2) BuildOmnibusData(
            byte baseEnablesMask, bool escrowMode,
            bool commandStack, bool commandReturn,
            bool extendedNoteReporting, bool pupA)
        {
            byte d0 = baseEnablesMask;

            byte d1 = escrowMode ? (byte)0x1C : (byte)0x18;
            if (commandStack) d1 |= 0x20;   
            if (commandReturn) d1 |= 0x40;  

            byte d2 = (extendedNoteReporting || pupA) ? (byte)0x10 : (byte)0x00;

            return (d0, d1, d2);
        }

        public EbdsPacket PollOmnibus(bool escrowMode = true, bool extended = true, bool pupA = true, byte baseEnablesMask = 0x7F)
        {
            var (d0, d1, d2) = BuildOmnibusData(baseEnablesMask, escrowMode, false, false, extended, pupA);
            Span<byte> data = stackalloc byte[] { d0, d1, d2 };
            return Exchange(EbdsMessageType.Type1_OmnibusCommand, data);
        }

        public EbdsPacket StackNow(bool escrowMode = true, bool extended = true, bool pupA = true, byte baseEnablesMask = 0x7F)
        {
            var (d0, d1, d2) = BuildOmnibusData(baseEnablesMask, escrowMode, true, false, extended, pupA);
            Span<byte> data = stackalloc byte[] { d0, d1, d2 };
            return Exchange(EbdsMessageType.Type1_OmnibusCommand, data);
        }

        public EbdsPacket ReturnNow(bool escrowMode = true, bool extended = true, bool pupA = true, byte baseEnablesMask = 0x7F)
        {
            var (d0, d1, d2) = BuildOmnibusData(baseEnablesMask, escrowMode, false, true, extended, pupA);
            Span<byte> data = stackalloc byte[] { d0, d1, d2 };
            return Exchange(EbdsMessageType.Type1_OmnibusCommand, data);
        }

        private EbdsPacket Type6(byte dataA, byte dataB, byte subType)
        {
            Span<byte> data = stackalloc byte[] { dataA, dataB, subType };
            return Exchange(EbdsMessageType.Type6_Auxiliary, data);
        }

        public string QuerySerialNumber()
        {
            var p = Type6(0x00, 0x00, 0x05);
            return Encoding.ASCII.GetString(p.Data);
        }

        public (uint TotalStacked, uint TotalBarcodesDecoded, uint TotalBarcodesStacked, uint TotalValueCouponsRecognized, uint TotalValueCouponsStacked)
            QueryAuditLifeTimeTotalsExtended()
        {
            var p = Type6(0x00, 0x00, 0x12);
            var vals = DecodeUint32NibbleArray(p.Data, 5);
            return (vals[0], vals[1], vals[2], vals[3], vals[4]);
        }

        public EbdsPacket QueryDiagnosticsSensor_StackerHome()
            => Type6(0x01, 0x00, 0x1D);

        public EbdsPacket Type7(byte subType, ReadOnlySpan<byte> extraData)
        {
            byte[] data = new byte[1 + extraData.Length];
            data[0] = subType;
            if (!extraData.IsEmpty) extraData.CopyTo(data.AsSpan(1));
            return Exchange(EbdsMessageType.Type7_Extended, data);
        }

        public EbdsPacket SetExtendedInhibitsAllowAll()
        {
            byte[] payload = Enumerable.Repeat((byte)0xFF, 19).ToArray();
            return Type7(0x03, payload);
        }

        public EbdsPacket SetExtendedInhibitsAllowNone()
        {
            byte[] payload = new byte[19];
            return Type7(0x03, payload);
        }

        public static uint[] DecodeUint32NibbleArray(byte[] ebdsData, int count)
        {
            var list = new List<uint>(count);
            int need = Math.Min(ebdsData.Length, count * 8);
            need -= need % 8;
            for (int off = 0; off < need; off += 8)
            {
                uint val = 0;
                for (int i = 0; i < 8; i++)
                {
                    byte nibble = (byte)(ebdsData[off + i] & 0x0F);
                    val = (val << 4) | nibble;
                }
                list.Add(val);
            }
            return list.ToArray();
        }

        public record NoteInfo(byte Index, string Currency, int ValueCents)
        {
            public decimal Value => ValueCents / 100m;
            public override string ToString() => $"{Currency} {Value:N2} (idx={Index})";
        }

        public Dictionary<byte, NoteInfo> ValueTable { get; } = new();

        public string QueryVariantName()
        {
            var reply = Type6(0x00, 0x00, 0x08);
            if (reply.Data.Length == 0) return "<sem resposta>";

            int end = Array.IndexOf(reply.Data, (byte)0x00);
            if (end < 0) end = reply.Data.Length;
            return Encoding.ASCII.GetString(reply.Data, 0, end).Trim();
        }

        public void LoadValueTable()
        {
            var resp = Type7(0x06, ReadOnlySpan<byte>.Empty);
            Console.WriteLine("ValueTable RAW: " + BitConverter.ToString(resp.Data));
            LoadValueTable_FromAsciiBRL(resp.Data);

            foreach (var kv in ValueTable.OrderBy(k => k.Value.ValueCents))
                Console.WriteLine($"{kv.Value.Currency} {kv.Value.Value:N2}  idx={kv.Key}");
        }

        public void LoadValueTable_FromAsciiBRL(byte[] raw)
        {
            ValueTable.Clear();
            for (int i = 0; i + 1 < raw.Length; i++)
            {
                if (raw[i + 1] == (byte)'B' && i + 1 + 3 + 3 + 1 + 2 <= raw.Length)
                {
                    byte idx = raw[i];
                    string ccy = "BRL";
                    string mant = Encoding.ASCII.GetString(raw, i + 1 + 3, 3);
                    char plus = (char)raw[i + 1 + 3 + 3];
                    string expS = Encoding.ASCII.GetString(raw, i + 1 + 3 + 3 + 1, 2);
                    if (plus != '+') continue;

                    if (int.TryParse(mant, out int m) && int.TryParse(expS, out int e))
                    {
                        int units = (int)(m * Math.Pow(10, e)); // 005+01 -> 50
                        int cents = checked(units * 100);
                        ValueTable[idx] = new NoteInfo(idx, ccy, cents);
                    }
                }
            }
        }

        private static bool TryParseValueTable_FormatA(byte[] data, Dictionary<byte, NoteInfo> dict)
        {
            if (data.Length < 8 || (data.Length % 8) != 0) return false;

            try
            {
                for (int off = 0; off < data.Length; off += 8)
                {
                    byte idx = data[off];
                    string ccy = Encoding.ASCII.GetString(data, off + 1, 3);
                    int cents = DecodeBcd32ToInt(data.AsSpan(off + 4, 4));
                    if (!string.IsNullOrWhiteSpace(ccy) && cents > 0)
                        dict[idx] = new NoteInfo(idx, ccy, cents);
                }
                return dict.Count > 0;
            }
            catch { return false; }
        }

        private static bool TryParseValueTable_FormatB(byte[] data, Dictionary<byte, NoteInfo> dict)
        {
            try
            {
                int off = 0;
                while (off + 6 <= data.Length)
                {
                    byte idx = data[off++];
                    byte ccyLen = data[off++];
                    if (off + ccyLen + 4 > data.Length) break;
                    string ccy = Encoding.ASCII.GetString(data, off, ccyLen);
                    off += ccyLen;
                    int cents = DecodeBcd32ToInt(data.AsSpan(off, 4));
                    off += 4;

                    if (!string.IsNullOrWhiteSpace(ccy) && cents > 0)
                        dict[idx] = new NoteInfo(idx, ccy, cents);
                }
                return dict.Count > 0;
            }
            catch { return false; }
        }

        private static int DecodeBcd32ToInt(ReadOnlySpan<byte> b4)
        {
            int v = 0;
            for (int i = 0; i < 4; i++)
            {
                byte b = b4[i];
                int hi = (b >> 4) & 0x0F;
                int lo = b & 0x0F;
                if (hi > 9 || lo > 9) return 0;
                v = v * 100 + hi * 10 + lo;
            }
            return v; // ex.: 000500 -> 500 (R$ 5,00)
        }

        public bool TryGetNoteIndexFromType2(byte data0, byte data1, byte data2, bool extendedMode, out int idx)
        {
            idx = 0;

            bool escrowed = (data0 & (1 << 2)) != 0;
            bool stackedEvent = (data0 & (1 << 4)) != 0;
            if (!escrowed && !stackedEvent) return false;

            if (extendedMode) return false; 

            int v = (data2 >> 3) & 0b111;
            if (v == 0) return false;
            idx = v;
            return true;
        }

        private bool TryParseExtendedNoteBRL(byte[] payload, out NoteInfo note)
        {
            note = default!;
            if (payload == null || payload.Length < 8) return false;

            for (int i = 0; i <= payload.Length - 8; i++)
            {
                if (payload[i] == (byte)'B' && payload[i + 1] == (byte)'R' && payload[i + 2] == (byte)'L')
                {
                    if (i + 8 > payload.Length) break;

                    if (i + 3 + 3 + 1 + 2 - 1 >= payload.Length) break;
                    string mant = Encoding.ASCII.GetString(payload, i + 3, 3);
                    if (payload[i + 6] != (byte)'+') continue;
                    string expS = Encoding.ASCII.GetString(payload, i + 7, 2);

                    if (int.TryParse(mant, out int m) && int.TryParse(expS, out int e))
                    {
                        int units = (int)(m * Math.Pow(10, e));
                        int cents = checked(units * 100);
                        note = new NoteInfo(0, "BRL", cents);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryPoll(out EbdsPacket packet)
        {
            packet = PollOmnibus(escrowMode: true, extended: true, pupA: true);
            return true;
        }

        public bool WaitForNoteOnce(int timeoutMs, out NoteInfo note)
        {
            note = default!;
            var deadline = Environment.TickCount + timeoutMs;

            SetExtendedInhibitsAllowAll();
            PollOmnibus(escrowMode: true, extended: true, pupA: true);

            while (Environment.TickCount < deadline)
            {
                var rep = PollOmnibus(escrowMode: true, extended: true, pupA: true);

                if (rep.MessageType == EbdsMessageType.Type7_Extended)
                {
                    if (TryParseExtendedNoteBRL(rep.Data, out var n))
                    {
                        note = n;
                        return true;
                    }
                }
                else if (rep.MessageType == EbdsMessageType.Type2_OmnibusReply)
                {
                }

                Thread.Sleep(180);
            }
            return false;
        }
    }
}
