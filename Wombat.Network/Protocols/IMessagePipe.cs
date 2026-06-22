using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Protocols;

public interface IMessagePipe
{
    ValueTask WriteAsync(
        PipeWriter writer,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    bool TryRead(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> payload);
}
