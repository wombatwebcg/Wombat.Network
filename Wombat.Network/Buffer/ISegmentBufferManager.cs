﻿using System;
using System.Collections.Generic;

namespace Wombat.Network
{
    public interface ISegmentBufferManager
    {
        ArraySegment<byte> BorrowBuffer();
        IEnumerable<ArraySegment<byte>> BorrowBuffers(int count);
        void ReturnBuffer(ArraySegment<byte> buffer);
        void ReturnBuffers(IEnumerable<ArraySegment<byte>> buffers);
        void ReturnBuffers(params ArraySegment<byte>[] buffers);
    }
}
