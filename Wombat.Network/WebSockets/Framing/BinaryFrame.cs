using System;

namespace Wombat.Network.WebSockets
{
    internal sealed class BinaryFrame : DataFrame
    {
        public BinaryFrame(byte[] data, int offset, int count, bool isMasked = true)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(nameof(data));

            Data = data;
            Offset = offset;
            Count = count;
            IsMasked = isMasked;
        }

        public byte[] Data { get; }
        public int Offset { get; }
        public int Count { get; }
        public bool IsMasked { get; }
        public override OpCode OpCode => OpCode.Binary;
    }
}
