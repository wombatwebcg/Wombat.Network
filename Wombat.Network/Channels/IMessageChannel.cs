using System;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.Channels;

public interface IMessageChannel
{
    string Id { get; }

    ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);
    ValueTask<ReceivedMessage?> ReceiveAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
