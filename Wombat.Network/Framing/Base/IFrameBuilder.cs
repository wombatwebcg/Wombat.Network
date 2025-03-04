﻿namespace Wombat.Network
{
    public interface IFrameBuilder
    {
        IFrameEncoder Encoder { get; }
        IFrameDecoder Decoder { get; }
    }
}
