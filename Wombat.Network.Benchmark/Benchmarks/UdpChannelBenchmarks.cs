using System.Net;
using BenchmarkDotNet.Attributes;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Transports.Udp;

namespace Wombat.Network.Benchmark.Benchmarks;

[Config(typeof(BenchmarkConfig))]
public class UdpChannelBenchmarks
{
    private UdpDatagramTransport _serverTransport = null!;
    private DatagramMessageChannel _serverChannel = null!;
    private DatagramMessageChannel[] _clientChannels = null!;
    private byte[] _payload = null!;
    private long _lastBytes;

    [ParamsSource(nameof(PayloadSizes))]
    public int PayloadSize { get; set; }

    [ParamsSource(nameof(ClientCounts))]
    public int ClientCount { get; set; }

    [ParamsSource(nameof(UseLengthFieldFramingValues))]
    public bool UseLengthFieldFraming { get; set; }

    public IEnumerable<int> PayloadSizes =>
        BenchmarkConfig.QuickMode ? [512, 4096] : [64, 512, 4096, 32768];

    public IEnumerable<int> ClientCounts =>
        BenchmarkConfig.QuickMode ? [1, 8] : [1, 8, 32];

    public IEnumerable<bool> UseLengthFieldFramingValues =>
        BenchmarkConfig.QuickMode ? [false] : [false, true];

    [GlobalSetup]
    public void Setup() => SetupCoreAsync().GetAwaiter().GetResult();

    [GlobalCleanup]
    public void Cleanup() => CleanupCoreAsync().GetAwaiter().GetResult();

    [Benchmark]
    public long SingleDatagramRoundTrip() => _lastBytes = SingleDatagramRoundTripCoreAsync().GetAwaiter().GetResult();

    [Benchmark]
    public long ConcurrentClientsSend() => _lastBytes = ConcurrentClientsSendCoreAsync().GetAwaiter().GetResult();

    [Benchmark]
    public long SingleClientBurst100() => _lastBytes = SingleClientBurst100CoreAsync().GetAwaiter().GetResult();

    private async Task<long> SingleDatagramRoundTripCoreAsync()
    {
        await _clientChannels[0].SendAsync(_payload).ConfigureAwait(false);
        var received = await _serverChannel.ReceiveAsync().ConfigureAwait(false);
        await _serverChannel.SendToAsync(_payload, received!.Value.RemoteEndPoint).ConfigureAwait(false);
        var echoed = await _clientChannels[0].ReceiveAsync().ConfigureAwait(false);
        return echoed!.Value.Payload.Length;
    }

    private async Task<long> ConcurrentClientsSendCoreAsync()
    {
        var sends = new Task[ClientCount];
        for (var i = 0; i < ClientCount; i++)
        {
            sends[i] = _clientChannels[i].SendAsync(_payload).AsTask();
        }

        await Task.WhenAll(sends).ConfigureAwait(false);

        long total = 0;
        for (var i = 0; i < ClientCount; i++)
        {
            var received = await _serverChannel.ReceiveAsync().ConfigureAwait(false);
            total += received!.Value.Payload.Length;
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
            var received = await _serverChannel.ReceiveAsync().ConfigureAwait(false);
            total += received!.Value.Payload.Length;
        }

        return total;
    }

    private async Task SetupCoreAsync()
    {
        _payload = BenchmarkData.CreatePayload(PayloadSize);
        var messagePipe = UseLengthFieldFraming ? new LengthFieldMessagePipe() : null;

        _serverTransport = new UdpDatagramTransport(new IPEndPoint(IPAddress.Loopback, 0), id: "udp-server");
        await _serverTransport.StartAsync().ConfigureAwait(false);
        var serverEndPoint = (IPEndPoint)_serverTransport.LocalEndPoint;

        _serverChannel = new DatagramMessageChannel(_serverTransport, messagePipe: messagePipe);
        _clientChannels = new DatagramMessageChannel[ClientCount];

        for (var i = 0; i < ClientCount; i++)
        {
            var clientTransport = new UdpDatagramTransport(new IPEndPoint(IPAddress.Loopback, 0), serverEndPoint, $"udp-client-{i}");
            await clientTransport.StartAsync().ConfigureAwait(false);
            _clientChannels[i] = new DatagramMessageChannel(
                clientTransport,
                defaultRemoteEndPoint: serverEndPoint,
                messagePipe: UseLengthFieldFraming ? new LengthFieldMessagePipe() : null);
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

        if (_serverChannel != null)
        {
            await _serverChannel.CloseAsync().ConfigureAwait(false);
        }
    }
}
