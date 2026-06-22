using BenchmarkDotNet.Attributes;
using System;
using System.Buffers;
using System.Net;
using System.Threading.Tasks;
using Wombat.Network.Benchmark.BenchmarkHelpers;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Transports.Udp;

namespace Wombat.Network.Benchmark.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class NewUdpTransportBenchmarks : NetworkBenchmarkBase
{
    private UdpDatagramTransport _serverTransport;
    private UdpDatagramTransport _clientTransport;
    private DatagramMessageChannel _serverChannel;
    private DatagramMessageChannel _clientChannel;
    private Task _serverLoopTask;
    private int _port;

    [Params(256, 1024, 4096)]
    public int MessageSize { get; set; }

    [Params(1, 100, 1000)]
    public int MessageCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _port = GetAvailablePort();
        var serverEndPoint = new IPEndPoint(IPAddress.Loopback, _port);
        var pipe = new LengthFieldMessagePipe(LengthField.FourBytes);

        _serverTransport = new UdpDatagramTransport(serverEndPoint);
        _clientTransport = new UdpDatagramTransport(defaultRemoteEndPoint: serverEndPoint);
        await _serverTransport.StartAsync();
        await _clientTransport.StartAsync();

        _serverChannel = new DatagramMessageChannel(_serverTransport, messagePipe: pipe);
        _clientChannel = new DatagramMessageChannel(_clientTransport, serverEndPoint, pipe);
        _serverLoopTask = RunEchoLoopAsync();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        if (_clientChannel != null)
        {
            await _clientChannel.CloseAsync();
        }

        if (_serverChannel != null)
        {
            await _serverChannel.CloseAsync();
        }

        if (_serverLoopTask != null)
        {
            try { await _serverLoopTask; } catch { }
        }
    }

    [Benchmark]
    public async Task SendReceiveRoundTrip()
    {
        var payload = GenerateTestData(MessageSize);
        for (var i = 0; i < MessageCount; i++)
        {
            await _clientChannel.SendAsync(payload);
            var reply = await _clientChannel.ReceiveAsync();
            if (!reply.HasValue || reply.Value.Payload.Length != payload.Length)
            {
                throw new InvalidOperationException("Echo payload mismatch.");
            }
        }
    }

    private async Task RunEchoLoopAsync()
    {
        while (true)
        {
            var message = await _serverChannel.ReceiveAsync();
            if (!message.HasValue)
            {
                return;
            }

            await _serverChannel.SendToAsync(ToArray(message.Value.Payload), message.Value.RemoteEndPoint);
        }
    }

    private static byte[] ToArray(in ReadOnlySequence<byte> sequence)
    {
        var buffer = new byte[sequence.Length];
        sequence.CopyTo(buffer);
        return buffer;
    }
}
