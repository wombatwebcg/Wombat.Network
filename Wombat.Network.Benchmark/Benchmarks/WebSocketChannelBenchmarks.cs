using System.Net;
using BenchmarkDotNet.Attributes;
using Wombat.Network.Channels;
using Wombat.Network.Transports.Tcp;

namespace Wombat.Network.Benchmark.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class WebSocketChannelBenchmarks
{
    private TcpTransportListener _listener = null!;
    private WebSocketMessageChannel[] _serverChannels = null!;
    private WebSocketMessageChannel[] _clientChannels = null!;
    private byte[] _payload = null!;
    private string _text = null!;
    private long _lastBytes;

    [ParamsSource(nameof(PayloadSizes))]
    public int PayloadSize { get; set; }

    [ParamsSource(nameof(ClientCounts))]
    public int ClientCount { get; set; }

    public IEnumerable<int> PayloadSizes =>
        BenchmarkConfig.QuickMode ? [512, 4096] : [64, 512, 4096, 32768];

    public IEnumerable<int> ClientCounts =>
        BenchmarkConfig.QuickMode ? [1, 8] : [1, 8, 32];

    [GlobalSetup]
    public void Setup() => SetupCoreAsync().GetAwaiter().GetResult();

    [GlobalCleanup]
    public void Cleanup() => CleanupCoreAsync().GetAwaiter().GetResult();

    [Benchmark]
    public long BinaryRoundTrip() => _lastBytes = BinaryRoundTripCoreAsync().GetAwaiter().GetResult();

    [Benchmark]
    public long TextRoundTrip() => _lastBytes = TextRoundTripCoreAsync().GetAwaiter().GetResult();

    [Benchmark]
    public long ConcurrentClientsBinaryRoundTrip() => _lastBytes = ConcurrentClientsBinaryRoundTripCoreAsync().GetAwaiter().GetResult();

    private async Task<long> BinaryRoundTripCoreAsync()
    {
        await _clientChannels[0].SendBinaryAsync(_payload).ConfigureAwait(false);
        await _serverChannels[0].ReceiveAsync().ConfigureAwait(false);
        await _serverChannels[0].SendBinaryAsync(_payload).ConfigureAwait(false);
        var echoed = await _clientChannels[0].ReceiveAsync().ConfigureAwait(false);
        return echoed!.Value.Payload.Length;
    }

    private async Task<long> TextRoundTripCoreAsync()
    {
        await _clientChannels[0].SendTextAsync(_text).ConfigureAwait(false);
        await _serverChannels[0].ReceiveAsync().ConfigureAwait(false);
        await _serverChannels[0].SendTextAsync(_text).ConfigureAwait(false);
        var echoed = await _clientChannels[0].ReceiveAsync().ConfigureAwait(false);
        return echoed!.Value.Payload.Length;
    }

    private async Task<long> ConcurrentClientsBinaryRoundTripCoreAsync()
    {
        var receives = new Task<Wombat.Network.Protocols.WebSocket.WebSocketReceivedMessage?>[ClientCount];
        for (var i = 0; i < ClientCount; i++)
        {
            receives[i] = _serverChannels[i].ReceiveAsync().AsTask();
        }

        var sends = new Task[ClientCount];
        for (var i = 0; i < ClientCount; i++)
        {
            sends[i] = _clientChannels[i].SendBinaryAsync(_payload).AsTask();
        }

        await Task.WhenAll(sends).ConfigureAwait(false);
        await Task.WhenAll(receives).ConfigureAwait(false);

        var echoes = new Task[ClientCount];
        for (var i = 0; i < ClientCount; i++)
        {
            echoes[i] = _serverChannels[i].SendBinaryAsync(_payload).AsTask();
        }

        var clientReceives = new Task<Wombat.Network.Protocols.WebSocket.WebSocketReceivedMessage?>[ClientCount];
        for (var i = 0; i < ClientCount; i++)
        {
            clientReceives[i] = _clientChannels[i].ReceiveAsync().AsTask();
        }

        await Task.WhenAll(echoes).ConfigureAwait(false);
        var echoed = await Task.WhenAll(clientReceives).ConfigureAwait(false);

        long total = 0;
        for (var i = 0; i < echoed.Length; i++)
        {
            total += echoed[i]!.Value.Payload.Length;
        }

        return total;
    }

    private async Task SetupCoreAsync()
    {
        _payload = BenchmarkData.CreatePayload(PayloadSize);
        _text = BenchmarkData.CreateText(PayloadSize);
        _listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0), backlog: ClientCount);
        await _listener.StartAsync().ConfigureAwait(false);

        var endPoint = (IPEndPoint)_listener.LocalEndPoint;
        var acceptTasks = new Task<Wombat.Network.Transports.Abstractions.ITransportConnection>[ClientCount];
        var connectTasks = new Task<TcpTransportConnection>[ClientCount];

        for (var i = 0; i < ClientCount; i++)
        {
            acceptTasks[i] = _listener.AcceptAsync();
            connectTasks[i] = TcpTransportConnection.ConnectAsync(endPoint, id: $"ws-client-{i}");
        }

        var serverConnections = await Task.WhenAll(acceptTasks).ConfigureAwait(false);
        var clientConnections = await Task.WhenAll(connectTasks).ConfigureAwait(false);

        _serverChannels = new WebSocketMessageChannel[ClientCount];
        _clientChannels = new WebSocketMessageChannel[ClientCount];

        for (var i = 0; i < ClientCount; i++)
        {
            _serverChannels[i] = new WebSocketMessageChannel(serverConnections[i], isClient: false);
            _clientChannels[i] = new WebSocketMessageChannel(clientConnections[i], isClient: true);
        }
    }

    private async Task CleanupCoreAsync()
    {
        if (_clientChannels != null)
        {
            foreach (var channel in _clientChannels)
            {
                if (channel != null)
                {
                    await channel.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        if (_serverChannels != null)
        {
            foreach (var channel in _serverChannels)
            {
                if (channel != null)
                {
                    await channel.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        if (_listener != null)
        {
            await _listener.CloseAsync().ConfigureAwait(false);
        }
    }
}
