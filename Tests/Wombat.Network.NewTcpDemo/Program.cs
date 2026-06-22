using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network;
using Wombat.Network.Channels;
using Wombat.Network.Protocols.Framing;
using Wombat.Network.Transports.Tcp;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "server";
var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 22334;
var messagePipe = new LengthFieldMessagePipe(LengthField.FourBytes);

if (mode == "server")
{
    await RunServerAsync(port, messagePipe);
    return;
}

if (mode == "client")
{
    var text = args.Length > 2 ? args[2] : "hello";
    await RunClientAsync(port, text, messagePipe);
    return;
}

Console.WriteLine("Usage:");
Console.WriteLine("  dotnet run --project Tests/Wombat.Network.NewTcpDemo -- server [port]");
Console.WriteLine("  dotnet run --project Tests/Wombat.Network.NewTcpDemo -- client [port] [message]");

static async Task RunServerAsync(int port, LengthFieldMessagePipe messagePipe)
{
    using var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, port));
    await listener.StartAsync();
    Console.WriteLine($"listening on 127.0.0.1:{port}");

    while (true)
    {
        var connection = (TcpTransportConnection)await listener.AcceptAsync();
        _ = Task.Run(async () =>
        {
            await connection.StartAsync();
            var channel = new StreamMessageChannel(connection, messagePipe);

            try
            {
                while (true)
                {
                    var message = await channel.ReceiveAsync();
                    if (!message.HasValue)
                    {
                        break;
                    }

                    var text = Encoding.UTF8.GetString(ToArray(message.Value.Payload));
                    Console.WriteLine($"recv {connection.RemoteEndPoint}: {text}");
                    await channel.SendAsync(Encoding.UTF8.GetBytes($"echo:{text}"));
                }
            }
            finally
            {
                await channel.CloseAsync();
            }
        });
    }
}

static async Task RunClientAsync(int port, string text, LengthFieldMessagePipe messagePipe)
{
    using var connection = await TcpTransportConnection.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
    await connection.StartAsync();
    var channel = new StreamMessageChannel(connection, messagePipe);

    await channel.SendAsync(Encoding.UTF8.GetBytes(text));
    var response = await channel.ReceiveAsync(CancellationToken.None);
    Console.WriteLine(response.HasValue
        ? Encoding.UTF8.GetString(ToArray(response.Value.Payload))
        : "connection closed");

    await channel.CloseAsync();
}

static byte[] ToArray(in ReadOnlySequence<byte> sequence)
{
    var buffer = new byte[sequence.Length];
    sequence.CopyTo(buffer);
    return buffer;
}
