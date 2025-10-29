using SCAdvanced.Model.Emun;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;

namespace SCAdvanced.Model
{
    public sealed class EbdsPacket
    {
        public const byte STX = 0x02;
        public const byte ETX = 0x03;

        public byte Length { get; }
        public byte Ctrl { get; }
        public byte[] Data { get; }
        public byte Checksum { get; }

        public EbdsMessageType MessageType => (EbdsMessageType)(Ctrl >> 4);

        public EbdsPacket(byte length, byte ctrl, byte[] data, byte checksum)
        {
            Length = length; Ctrl = ctrl; Data = data; Checksum = checksum;
        }

        public static byte ComputeXorChecksum(ReadOnlySpan<byte> frame)
        {
            // CHK = XOR dos bytes [1..LEN-3] (LEN, CTL, DATA.., ETX)
            int len = frame[1];
            byte xor = 0x00;
            for (int i = 1; i <= len - 3; i++) xor ^= frame[i];
            return xor;
        }

        public static EbdsPacket Parse(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < 5 || frame[0] != STX)
                throw new InvalidOperationException("Frame inválido (STX).");

            int len = frame[1];
            if (len != frame.Length)
                throw new InvalidOperationException($"Tamanho incorreto. Esperado {len}, recebi {frame.Length}.");

            if (frame[len - 2] != ETX)
                throw new InvalidOperationException("Frame inválido (ETX).");

            byte chk = frame[len - 1];
            byte calc = ComputeXorChecksum(frame);
            if (chk != calc)
                throw new InvalidOperationException($"Checksum inválido. Esperado 0x{calc:X2}, recebi 0x{chk:X2}.");

            byte ctrl = frame[2];
            byte[] data = frame.Slice(3, len - 5).ToArray();
            return new EbdsPacket((byte)len, ctrl, data, chk);
        }

        public static byte[] Build(byte ctrl, ReadOnlySpan<byte> data)
        {
            int len = 5 + data.Length;                 // STX + LEN + CTL + DATA.. + ETX + CHK
            byte[] buf = new byte[len];
            buf[0] = STX;
            buf[1] = (byte)len;
            buf[2] = ctrl;
            if (!data.IsEmpty) data.CopyTo(buf.AsSpan(3));
            buf[len - 2] = ETX;
            buf[len - 1] = ComputeXorChecksum(buf);
            return buf;
        }
    }
}
