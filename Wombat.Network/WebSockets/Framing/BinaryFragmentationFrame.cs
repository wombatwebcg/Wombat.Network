using System;

namespace Wombat.Network.WebSockets
{
    internal sealed class BinaryFragmentationFrame : Frame
    {
        public BinaryFragmentationFrame(OpCode opCode, byte[] data, int offset, int count, bool isFin = false, bool isMasked = true)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(nameof(data));

            OpCode = opCode;
            Data = data;
            Offset = offset;
            Count = count;
            IsFin = isFin;
            IsMasked = isMasked;
        }

        public byte[] Data { get; }
        public int Offset { get; }
        public int Count { get; }
        public bool IsFin { get; }
        public bool IsMasked { get; }
        public override OpCode OpCode { get; }
    }
}
