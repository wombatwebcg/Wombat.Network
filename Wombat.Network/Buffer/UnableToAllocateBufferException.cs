using System;

namespace Wombat.Network
{
    [Serializable]
    public class UnableToAllocateBufferException : Exception
    {
        public UnableToAllocateBufferException()
            : base("Cannot allocate buffer after few trials.")
        {
        }
    }
}
