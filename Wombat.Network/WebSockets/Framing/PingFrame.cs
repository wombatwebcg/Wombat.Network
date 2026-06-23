namespace Wombat.Network.WebSockets
{
    internal sealed class PingFrame : ControlFrame
    {
        public PingFrame(string data, bool isMasked = true)
        {
            Data = data;
            IsMasked = isMasked;
        }

        public string Data { get; }
        public bool IsMasked { get; }
        public override OpCode OpCode => OpCode.Ping;
    }
}
