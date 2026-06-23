namespace Wombat.Network.WebSockets
{
    internal sealed class CloseFrame : ControlFrame
    {
        public CloseFrame(Protocols.WebSocket.WebSocketCloseCode closeCode, string closeReason, bool isMasked = true)
        {
            CloseCode = closeCode;
            CloseReason = closeReason;
            IsMasked = isMasked;
        }

        public Protocols.WebSocket.WebSocketCloseCode CloseCode { get; }
        public string CloseReason { get; }
        public bool IsMasked { get; }
        public override OpCode OpCode => OpCode.Close;
    }
}
