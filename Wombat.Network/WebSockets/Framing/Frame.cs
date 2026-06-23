namespace Wombat.Network.WebSockets
{
    internal abstract class Frame
    {
        public abstract OpCode OpCode { get; }
    }
}
