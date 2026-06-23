using System.Net;
using BenchmarkDotNet.Attributes;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Transports.Tcp;

namespace Wombat.Network.Benchmark.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class TcpChannelBenchmarks
{
    private TcpTransportListener _listener = null!;
    private StreamMessageChannel[] _serverChannels = null!;
    private StreamMessageChannel[] _clientChannels = null!;
    private byte[] _payload = null!;
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
    public long SingleClientRoundTrip() => _lastBytes = SingleClientRoundTripCoreAsync().GetAwaiter().GetResult();

    [Benchmark]
    public long ConcurrentClientsRoundTrip() => _lastBytes = ConcurrentClientsRoundTripCoreAsync().GetAwaiter().GetResult();

    [Benchmark]
    public long SingleClientBurst100() => _lastBytes = SingleClientBurst100CoreAsync().GetAwaiter().GetResult();

    private async Task<long> SingleClientRoundTripCoreAsync()
    {
        await _clientChannels[0].SendAsync(_payload).ConfigureAwait(false);
        await _serverChannels[0].ReceiveAsync().ConfigureAwait(false);
        await _serverChannels[0].SendAsync(_payload).ConfigureAwait(false);
        var echoed = await _clientChannels[0].ReceiveAsync().ConfigureAwait(false);
        return echoed!.Value.Payload.Length;
    }

    private async Task<long> ConcurrentClientsRoundTripCoreAsync()
    {
        var receives = new Task<ReceivedMessage?>[ClientCount];
        for (var i = 0; i < ClientCount; i++)
        {
            receives[i] = _serverChannels[i].ReceiveAsync().AsTask();
        }

        var sends = new Task[ClientCount];
        for (var i = 0; i < ClientCount; i++)
        {
            sends[i] = _clientChannels[i].SendAsync(_payload).AsTask();
        }

        await Task.WhenAll(sends).ConfigureAwait(false);
        await Task.WhenAll(receives).ConfigureAwait(false);

        var echoes = new Task[ClientCount];
        for (var i = 0; i < ClientCount; i++)
        {
            echoes[i] = _serverChannels[i].SendAsync(_payload).AsTask();
        }

        var clientReceives = new Task<ReceivedMessage?>[ClientCount];
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

    private async Task<long> SingleClientBurst100CoreAsync()
    {
        long total = 0;

        for (var i = 0; i < 100; i++)
        {
            await _clientChannels[0].SendAsync(_payload).ConfigureAwait(false);
        }

        for (var i = 0; i < 100; i++)
        {
            var received = await _serverChannels[0].ReceiveAsync().ConfigureAwait(false);
            total += received!.Value.Payload.Length;
        }

        return total;
    }

    private async Task SetupCoreAsync()
    {
        _payload = BenchmarkData.CreatePayload(PayloadSize);
        _listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0), backlog: ClientCount);
        await _listener.StartAsync().ConfigureAwait(false);

        var endPoint = (IPEndPoint)_listener.LocalEndPoint;
        var acceptTasks = new Task<Wombat.Network.Transports.Abstractions.ITransportConnection>[ClientCount];
        var connectTasks = new Task<TcpTransportConnection>[ClientCount];

        for (var i = 0; i < ClientCount; i++)
        {
            acceptTasks[i] = _listener.AcceptAsync();
            connectTasks[i] = TcpTransportConnection.ConnectAsync(endPoint, id: $"tcp-client-{i}");
        }

        var serverConnections = await Task.WhenAll(acceptTasks).ConfigureAwait(false);
        var clientConnections = await Task.WhenAll(connectTasks).ConfigureAwait(false);

        _serverChannels = new StreamMessageChannel[ClientCount];
        _clientChannels = new StreamMessageChannel[ClientCount];

        for (var i = 0; i < ClientCount; i++)
        {
            _serverChannels[i] = new StreamMessageChannel(serverConnections[i], new LengthFieldMessagePipe());
            _clientChannels[i] = new StreamMessageChannel(clientConnections[i], new LengthFieldMessagePipe());
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
