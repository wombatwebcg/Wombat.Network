namespace Wombat.Network.WebSockets
{
    internal sealed class Header
    {
        public bool IsFIN { get; set; }
        public bool IsRSV1 { get; set; }
        public bool IsRSV2 { get; set; }
        public bool IsRSV3 { get; set; }
        public OpCode OpCode { get; set; }
        public bool IsMasked { get; set; }
        public int PayloadLength { get; set; }
        public int MaskingKeyOffset { get; set; }
        public int Length { get; set; }
    }
}
