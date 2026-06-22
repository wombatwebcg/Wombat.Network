using Pipelines.Sockets.Unofficial;
using System;
using System.IO.Pipelines;
using System.IO.Ports;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Transports.Abstractions;

namespace Wombat.Network.Transports.Serial;

public sealed class SerialTransportConnection : ITransportConnection, IDisposable
{
    private readonly SerialPort _serialPort;
    private readonly IDuplexPipe _transport;
    private int _closed;

    public SerialTransportConnection(SerialPort serialPort, PipeOptions pipeOptions = null, string id = null)
    {
        _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
        _transport = StreamConnection.GetDuplex(_serialPort.BaseStream, pipeOptions, id);
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }

    public string Id { get; }
    public EndPoint LocalEndPoint => null;
    public EndPoint RemoteEndPoint => null;
    public IDuplexPipe Transport => _transport;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_serialPort.IsOpen)
        {
            _serialPort.Open();
        }

        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            try { _transport.Input.Complete(); } catch { }
            try { _transport.Output.Complete(); } catch { }
            try { _serialPort.Dispose(); } catch { }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
    }
}
