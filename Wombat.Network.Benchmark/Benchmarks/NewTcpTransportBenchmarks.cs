using BenchmarkDotNet.Attributes;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Benchmark.BenchmarkHelpers;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Transports.Tcp;

namespace Wombat.Network.Benchmark.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob]
    public class NewTcpTransportBenchmarks : NetworkBenchmarkBase
    {
        private TcpTransportListener _listener;
        private Task _acceptLoopTask;
        private CancellationTokenSource _acceptLoopCts;
        private readonly List<TcpTransportConnection> _serverConnections = new();
        private readonly List<StreamMessageChannel> _serverChannels = new();
        private readonly object _sync = new();

        private TcpTransportConnection _client;
        private StreamMessageChannel _clientChannel;
        private LengthFieldMessagePipe _messagePipe;
        private int _port;

        [Params(256, 1024, 4096)]
        public int MessageSize { get; set; }

        [Params(1, 100, 1000)]
        public int MessageCount { get; set; }

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            _port = GetAvailablePort();
            _messagePipe = new LengthFieldMessagePipe(LengthField.FourBytes);
            _listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, _port));
            _acceptLoopCts = new CancellationTokenSource();

            await _listener.StartAsync();
            _acceptLoopTask = RunEchoServerAsync(_acceptLoopCts.Token);
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            _acceptLoopCts.Cancel();

            if (_clientChannel != null)
            {
                await _clientChannel.CloseAsync();
            }

            if (_listener != null)
            {
                await _listener.CloseAsync();
            }

            if (_acceptLoopTask != null)
            {
                try { await _acceptLoopTask; } catch { }
            }

            foreach (var channel in _serverChannels)
            {
                try { await channel.CloseAsync(); } catch { }
            }
        }

        [IterationSetup]
        public async Task IterationSetup()
        {
            _client = await TcpTransportConnection.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port));
            await _client.StartAsync();
            _clientChannel = new StreamMessageChannel(_client, _messagePipe);
        }

        [IterationCleanup]
        public async Task IterationCleanup()
        {
            if (_clientChannel != null)
            {
                await _clientChannel.CloseAsync();
                _clientChannel = null;
                _client = null;
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
                if (!reply.HasValue || GetLength(reply.Value.Payload) != payload.Length)
                {
                    throw new InvalidOperationException("Echo payload mismatch.");
                }
            }
        }

        [Benchmark]
        public async Task ConnectAndClose()
        {
            for (var i = 0; i < MessageCount; i++)
            {
                var connection = await TcpTransportConnection.ConnectAsync(new IPEndPoint(IPAddress.Loopback, _port));
                await connection.CloseAsync();
            }
        }

        private async Task RunEchoServerAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpTransportConnection connection = null;
                try
                {
                    connection = (TcpTransportConnection)await _listener.AcceptAsync(cancellationToken);
                    await connection.StartAsync(cancellationToken);

                    var channel = new StreamMessageChannel(connection, _messagePipe);
                    lock (_sync)
                    {
                        _serverConnections.Add(connection);
                        _serverChannels.Add(channel);
                    }

                    _ = Task.Run(() => EchoLoopAsync(channel, cancellationToken), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    if (connection != null)
                    {
                        try { await connection.CloseAsync(); } catch { }
                    }
                }
            }
        }

        private async Task EchoLoopAsync(StreamMessageChannel channel, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await channel.ReceiveAsync(cancellationToken);
                    if (!message.HasValue)
                    {
                        break;
                    }

                    var payload = ToArray(message.Value.Payload);
                    await channel.SendAsync(payload, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                try { await channel.CloseAsync(); } catch { }
            }
        }

        private static int GetLength(in ReadOnlySequence<byte> sequence) => checked((int)sequence.Length);

        private static byte[] ToArray(in ReadOnlySequence<byte> sequence)
        {
            var buffer = new byte[sequence.Length];
            sequence.CopyTo(buffer);
            return buffer;
        }
    }
}
