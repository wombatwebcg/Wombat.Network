namespace Wombat.Network.WebSockets
{
    internal sealed class PongFrame : ControlFrame
    {
        public PongFrame(string data, bool isMasked = true)
        {
            Data = data;
            IsMasked = isMasked;
        }

        public string Data { get; }
        public bool IsMasked { get; }
        public override OpCode OpCode => OpCode.Pong;
    }
}
