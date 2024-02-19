using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Wombat.Network
{
    /// <summary>
    /// A manager to handle buffers for the socket connections.
    /// </summary>
    /// <remarks>
    /// When used in an async call a buffer is pinned. Large numbers of pinned buffers
    /// cause problem with the GC (in particular it causes heap fragmentation).
    /// This class maintains a set of large segments and gives clients pieces of these
    /// segments that they can use for their buffers. The alternative to this would be to
    /// create many small arrays which it then maintained. This methodology should be slightly
    /// better than the many small array methodology because in creating only a few very
    /// large objects it will force these objects to be placed on the LOH. Since the
    /// objects are on the LOH they are at this time not subject to compacting which would
    /// require an update of all GC roots as would be the case with lots of smaller arrays
    /// that were in the normal heap.
    /// </remarks>
    public class SegmentBufferManager : ISegmentBufferManager
    {
        /// <summary>
        /// 三元组计数
        /// </summary>
        private const int TrialsCount = 100;

        private static SegmentBufferManager _defaultBufferManager;

        private readonly int _segmentChunks;//分段块
        private readonly int _chunkSize;//组块大小
        private readonly int _segmentSize; //段大小
        private readonly bool _allowedToCreateMemory;//允许创建内存

        private readonly ConcurrentStack<ArraySegment<byte>> _buffers = new ConcurrentStack<ArraySegment<byte>>();

        private readonly List<byte[]> _segments;
        private readonly object _creatingNewSegmentLock = new object();

        public static SegmentBufferManager Default
        {
            get
            {
                // default to 1024 1kb buffers if people don't want to manage it on their own;
                if (_defaultBufferManager == null)
                    _defaultBufferManager = new SegmentBufferManager(1024, 1024, 1);
                return _defaultBufferManager;
            }
        }

        /// <summary>
        /// 设置默认缓冲区管理器
        /// </summary>
        /// <param name="manager"></param>
        public static void SetDefaultBufferManager(SegmentBufferManager manager)
        {
            if (manager == null)
                throw new ArgumentNullException("manager");
            _defaultBufferManager = manager;
        }

        /// <summary>
        /// 组块大小
        /// </summary>
        public int ChunkSize
        {
            get { return _chunkSize; }
        }

        /// <summary>
        /// 段数量
        /// </summary>
        public int SegmentsCount
        {
            get { return _segments.Count; }
        }

        /// <summary>
        /// 分段块数量
        /// </summary>
        public int SegmentChunksCount
        {
            get { return _segmentChunks; }
        }

        /// <summary>
        /// 可用缓存数量
        /// </summary>
        public int AvailableBuffers
        {
            get { return _buffers.Count; }
        }

        /// <summary>
        /// 缓冲区总大小
        /// </summary>
        public int TotalBufferSize
        {
            get { return _segments.Count * _segmentSize; }
        }

        /// <summary>
        /// 构造器
        /// </summary>
        /// <param name="segmentChunks"></param>
        /// <param name="chunkSize"></param>
        public SegmentBufferManager(int segmentChunks, int chunkSize)
            : this(segmentChunks, chunkSize, 1) { }

        public SegmentBufferManager(int segmentChunks, int chunkSize, int initialSegments)
            : this(segmentChunks, chunkSize, initialSegments, true) { }

        /// <summary>
        /// Constructs a new <see cref="SegmentBufferManager"></see> object
        /// </summary>
        /// <param name="segmentChunks">The number of chunks to create per segment</param>
        /// <param name="chunkSize">The size of a chunk in bytes</param>
        /// <param name="initialSegments">The initial number of segments to create</param>
        /// <param name="allowedToCreateMemory">If false when empty and checkout is called an exception will be thrown</param>
        public SegmentBufferManager(int segmentChunks, int chunkSize, int initialSegments, bool allowedToCreateMemory)
        {
            if (segmentChunks <= 0)
                throw new ArgumentException("segmentChunks");
            if (chunkSize <= 0)
                throw new ArgumentException("chunkSize");
            if (initialSegments < 0)
                throw new ArgumentException("initialSegments");

            _segmentChunks = segmentChunks;
            _chunkSize = chunkSize;
            _segmentSize = _segmentChunks * _chunkSize;

            _segments = new List<byte[]>();

            _allowedToCreateMemory = true;
            for (int i = 0; i < initialSegments; i++)
            {
                CreateNewSegment(true);
            }
            _allowedToCreateMemory = allowedToCreateMemory;
        }

        private void CreateNewSegment(bool forceCreation)
        {
            if (!_allowedToCreateMemory)
                throw new UnableToCreateMemoryException();

            lock (_creatingNewSegmentLock)
            {
                if (!forceCreation && _buffers.Count > _segmentChunks / 2)
                    return;

                var bytes = new byte[_segmentSize];
                _segments.Add(bytes);
                for (int i = 0; i < _segmentChunks; i++)
                {
                    var chunk = new ArraySegment<byte>(bytes, i * _chunkSize, _chunkSize);
                    _buffers.Push(chunk);
                }
            }
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public ArraySegment<byte> BorrowBuffer()
        {
            int trial = 0;
            while (trial < TrialsCount)
            {
                ArraySegment<byte> result;
                if (_buffers.TryPop(out result))
                    return result;
                CreateNewSegment(false);
                trial++;
            }
            throw new UnableToAllocateBufferException();
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public IEnumerable<ArraySegment<byte>> BorrowBuffers(int count)
        {
            var result = new ArraySegment<byte>[count];
            var trial = 0;
            var totalReceived = 0;

            try
            {
                while (trial < TrialsCount)
                {
                    ArraySegment<byte> piece;
                    while (totalReceived < count)
                    {
                        if (!_buffers.TryPop(out piece))
                            break;
                        result[totalReceived] = piece;
                        ++totalReceived;
                    }
                    if (totalReceived == count)
                        return result;
                    CreateNewSegment(false);
                    trial++;
                }
                throw new UnableToAllocateBufferException();
            }
            catch
            {
                if (totalReceived > 0)
                    ReturnBuffers(result.Take(totalReceived));
                throw;
            }
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public void ReturnBuffer(ArraySegment<byte> buffer)
        {
            if (ValidateBuffer(buffer))
            {
                _buffers.Push(buffer);
            }
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public void ReturnBuffers(IEnumerable<ArraySegment<byte>> buffers)
        {
            if (buffers == null)
                throw new ArgumentNullException("buffers");

            foreach (var buf in buffers)
            {
                if (ValidateBuffer(buf))
                {
                    _buffers.Push(buf);
                }
            }
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public void ReturnBuffers(params ArraySegment<byte>[] buffers)
        {
            if (buffers == null)
                throw new ArgumentNullException("buffers");

            foreach (var buf in buffers)
            {
                if (ValidateBuffer(buf))
                {
                    _buffers.Push(buf);
                }
            }
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        private bool ValidateBuffer(ArraySegment<byte> buffer)
        {
            if (buffer.Array == null || buffer.Count == 0 || buffer.Array.Length < buffer.Offset + buffer.Count)
                return false;

            if (buffer.Count != _chunkSize)
                return false;

            return true;
        }
    }
}
