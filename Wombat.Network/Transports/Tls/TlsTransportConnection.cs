using Pipelines.Sockets.Unofficial;
using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Wombat.Network.Transports.Abstractions;

namespace Wombat.Network.Transports.Tls;

public sealed class TlsTransportConnection : ITransportConnection, IDisposable
{
    private readonly ITransportConnection _innerConnection;
    private readonly SslStream _sslStream;
    private readonly Func<SslStream, CancellationToken, Task> _authenticateAsync;
    private readonly PipeOptions _pipeOptions;
    private readonly string _transportId;
    private IDuplexPipe _transport;
    private int _closed;

    private TlsTransportConnection(
        ITransportConnection innerConnection,
        SslStream sslStream,
        Func<SslStream, CancellationToken, Task> authenticateAsync,
        PipeOptions pipeOptions,
        string transportId,
        string id)
    {
        _innerConnection = innerConnection ?? throw new ArgumentNullException(nameof(innerConnection));
        _sslStream = sslStream ?? throw new ArgumentNullException(nameof(sslStream));
        _authenticateAsync = authenticateAsync ?? throw new ArgumentNullException(nameof(authenticateAsync));
        _pipeOptions = pipeOptions;
        _transportId = transportId;
        Id = string.IsNullOrWhiteSpace(id) ? innerConnection.Id : id;
    }

    public string Id { get; }
    public EndPoint LocalEndPoint => _innerConnection.LocalEndPoint;
    public EndPoint RemoteEndPoint => _innerConnection.RemoteEndPoint;
    public IDuplexPipe Transport => _transport ?? throw new InvalidOperationException("TLS transport is not started.");

    public static TlsTransportConnection CreateClient(
        ITransportConnection innerConnection,
        string targetHost,
        RemoteCertificateValidationCallback validationCallback = null,
        PipeOptions pipeOptions = null,
        string id = null)
    {
        if (string.IsNullOrWhiteSpace(targetHost))
        {
            throw new ArgumentException("Target host is required.", nameof(targetHost));
        }

        var stream = StreamConnection.GetDuplex(innerConnection.Transport, innerConnection.Id);
        var sslStream = new SslStream(stream, false, validationCallback);
        return new TlsTransportConnection(
            innerConnection,
            sslStream,
            (ssl, _) => ssl.AuthenticateAsClientAsync(targetHost),
            pipeOptions,
            id,
            id);
    }

    public static TlsTransportConnection CreateServer(
        ITransportConnection innerConnection,
        X509Certificate certificate,
        bool clientCertificateRequired = false,
        SslProtocols enabledSslProtocols = SslProtocols.Tls12,
        bool checkCertificateRevocation = false,
        RemoteCertificateValidationCallback validationCallback = null,
        PipeOptions pipeOptions = null,
        string id = null)
    {
        if (certificate == null)
        {
            throw new ArgumentNullException(nameof(certificate));
        }

        var stream = StreamConnection.GetDuplex(innerConnection.Transport, innerConnection.Id);
        var sslStream = new SslStream(stream, false, validationCallback);
        return new TlsTransportConnection(
            innerConnection,
            sslStream,
            (ssl, _) => ssl.AuthenticateAsServerAsync(certificate, clientCertificateRequired, enabledSslProtocols, checkCertificateRevocation),
            pipeOptions,
            id,
            id);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _innerConnection.StartAsync(cancellationToken).ConfigureAwait(false);
        await _authenticateAsync(_sslStream, cancellationToken).ConfigureAwait(false);
        _transport = StreamConnection.GetDuplex(_sslStream, _pipeOptions, _transportId);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        try { _transport?.Input.Complete(); } catch { }
        try { _transport?.Output.Complete(); } catch { }
        try { _sslStream.Dispose(); } catch { }
        await _innerConnection.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
    }
}
