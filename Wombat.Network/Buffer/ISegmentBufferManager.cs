using System;
using System.Collections.Generic;

namespace Wombat.Network
{
    /// <summary>
    /// 分段缓冲管理器
    /// </summary>
    public interface ISegmentBufferManager
    {
        /// <summary>
        /// 借用缓冲
        /// </summary>
        /// <returns></returns>
        ArraySegment<byte> BorrowBuffer();

        /// <summary>
        /// 借用缓冲区块
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        IEnumerable<ArraySegment<byte>> BorrowBuffers(int count);

        /// <summary>
        /// 返回缓存
        /// </summary>
        /// <param name="buffer"></param>
        void ReturnBuffer(ArraySegment<byte> buffer);

        /// <summary>
        /// 返回缓存区
        /// </summary>
        /// <param name="buffers"></param>
        void ReturnBuffers(IEnumerable<ArraySegment<byte>> buffers);

        /// <summary>
        /// 返回缓存区
        /// </summary>
        /// <param name="buffers"></param>
        void ReturnBuffers(params ArraySegment<byte>[] buffers);
    }
}
