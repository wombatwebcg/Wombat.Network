using BenchmarkDotNet.Attributes;
using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Benchmark.BenchmarkHelpers;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.WebSocket;
using Wombat.Network.Transports.Tcp;

namespace Wombat.Network.Benchmark.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class NewWebSocketChannelBenchmarks : NetworkBenchmarkBase
{
    private TcpTransportListener _listener;
    private CancellationTokenSource _serverCts;
    private Task _serverTask;
    private TcpTransportConnection _clientConnection;
    private WebSocketMessageChannel _clientChannel;
    private int _port;

    [Params(16, 128, 1024)]
    public int MessageSize { get; set; }

    [Params(1, 100, 500)]
    public int MessageCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _port = GetAvailablePort();
        _listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, _port));
        _serverCts = new CancellationTokenSource();
        await _listener.StartAsync();
        _serverTask = RunServerAsync(_serverCts.Token);
        await ConnectClientAsync();
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _serverCts.Cancel();
        if (_clientChannel != null)
        {
            await _clientChannel.CloseAsync();
        }

        if (_listener != null)
        {
            await _listener.CloseAsync();
        }

        if (_serverTask != null)
        {
            try { await _serverTask; } catch { }
        }
    }

    [IterationSetup]
    public async Task IterationSetup()
    {
        if (_clientChannel == null)
        {
            await ConnectClientAsync();
        }
    }

    [IterationCleanup]
    public async Task IterationCleanup()
    {
        if (_clientChannel != null)
        {
            await _clientChannel.CloseAsync();
            _clientChannel = null;
            _clientConnection = null;
        }
    }

    [Benchmark]
    public async Task TextRoundTrip()
    {
        var payload = new string('x', MessageSize);
        for (var i = 0; i < MessageCount; i++)
        {
            await _clientChannel.SendTextAsync(payload);
            var reply = await _clientChannel.ReceiveAsync();
            if (!reply.HasValue || Encoding.UTF8.GetString(ToArray(reply.Value.Payload)) != payload)
            {
                throw new InvalidOperationException("Echo payload mismatch.");
            }
        }
    }

    private async Task ConnectClientAsync()
    {
        _clientConnection = await TcpTransportConnection.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port));
        await _clientConnection.StartAsync();
        await WebSocketHandshakeMiddleware.AcceptClientAsync(_clientConnection, $"127.0.0.1:{_port}", "/bench");
        _clientChannel = new WebSocketMessageChannel(_clientConnection, isClient: true);
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpTransportConnection accepted = null;
            try
            {
                accepted = (TcpTransportConnection)await _listener.AcceptAsync(cancellationToken);
                await accepted.StartAsync(cancellationToken);
                _ = Task.Run(async () =>
                {
                    using (accepted)
                    {
                        await WebSocketHandshakeMiddleware.AcceptServerAsync(accepted, cancellationToken);
                        var channel = new WebSocketMessageChannel(accepted, isClient: false);
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            var inbound = await channel.ReceiveAsync(cancellationToken);
                            if (!inbound.HasValue)
                            {
                                break;
                            }

                            await channel.SendTextAsync(Encoding.UTF8.GetString(ToArray(inbound.Value.Payload)), cancellationToken);
                        }
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (accepted != null)
                {
                    try { await accepted.CloseAsync(); } catch { }
                }
            }
        }
    }

    private static byte[] ToArray(in ReadOnlySequence<byte> sequence)
    {
        var buffer = new byte[sequence.Length];
        sequence.CopyTo(buffer);
        return buffer;
    }
}
