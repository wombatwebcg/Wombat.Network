using System;

namespace Wombat.Network
{
    /// <summary>
    /// 缓冲验证器
    /// </summary>
    public class BufferValidator
    {
        /// <summary>
        /// 验证缓冲区
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="bufferParameterName"></param>
        /// <param name="offsetParameterName"></param>
        /// <param name="countParameterName"></param>
        public static void ValidateBuffer(byte[] buffer, int offset, int count,
            string bufferParameterName = null,
            string offsetParameterName = null,
            string countParameterName = null)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(!string.IsNullOrEmpty(bufferParameterName) ? bufferParameterName : "buffer");
            }

            if (offset < 0 || offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(!string.IsNullOrEmpty(offsetParameterName) ? offsetParameterName : "offset");
            }

            if (count < 0 || count > (buffer.Length - offset))
            {
                throw new ArgumentOutOfRangeException(!string.IsNullOrEmpty(countParameterName) ? countParameterName : "count");
            }
        }

        public static void ValidateArraySegment<T>(ArraySegment<T> arraySegment, string arraySegmentParameterName = null)
        {
            if (arraySegment.Array == null)
            {
                throw new ArgumentNullException((!string.IsNullOrEmpty(arraySegmentParameterName) ? arraySegmentParameterName : "arraySegment") + ".Array");
            }

            if (arraySegment.Offset < 0 || arraySegment.Offset > arraySegment.Array.Length)
            {
                throw new ArgumentOutOfRangeException((!string.IsNullOrEmpty(arraySegmentParameterName) ? arraySegmentParameterName : "arraySegment") + ".Offset");
            }

            if (arraySegment.Count < 0 || arraySegment.Count > (arraySegment.Array.Length - arraySegment.Offset))
            {
                throw new ArgumentOutOfRangeException((!string.IsNullOrEmpty(arraySegmentParameterName) ? arraySegmentParameterName : "arraySegment") + ".Count");
            }
        }
    }
}
