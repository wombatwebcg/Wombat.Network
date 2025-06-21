using System;

namespace Wombat.Network
{
    [Serializable]
    public class UdpSocketException : Exception
    {
        public UdpSocketException(string message)
            : base(message)
        {
        }

        public UdpSocketException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
} 