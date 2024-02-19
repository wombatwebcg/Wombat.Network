using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Wombat.Network.Sockets
{
    public readonly struct SslApplicationProtocol : IEquatable<SslApplicationProtocol>
    {
        private static readonly Encoding s_utf8 = Encoding.GetEncoding(Encoding.UTF8.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private static readonly byte[] s_http3Utf8 = Encoding.UTF8.GetBytes("h3");
        private static readonly byte[] s_http2Utf8 = Encoding.UTF8.GetBytes("h2");
        private static readonly byte[] s_http11Utf8 = Encoding.UTF8.GetBytes("http/1.1");

        // Refer to IANA on ApplicationProtocols: https://www.iana.org/assignments/tls-extensiontype-values/tls-extensiontype-values.xhtml#alpn-protocol-ids
        /// <summary>Defines a <see cref="SslApplicationProtocol"/> instance for HTTP 3.0.</summary>
        public static readonly SslApplicationProtocol Http3 = new SslApplicationProtocol(s_http3Utf8, copy: false);
        /// <summary>Defines a <see cref="SslApplicationProtocol"/> instance for HTTP 2.0.</summary>
        public static readonly SslApplicationProtocol Http2 = new SslApplicationProtocol(s_http2Utf8, copy: false);
        /// <summary>Defines a <see cref="SslApplicationProtocol"/> instance for HTTP 1.1.</summary>
        public static readonly SslApplicationProtocol Http11 = new SslApplicationProtocol(s_http11Utf8, copy: false);

        private readonly byte[] _readOnlyProtocol;

        internal SslApplicationProtocol(byte[] protocol, bool copy)
        {
            Debug.Assert(protocol != null);

            // RFC 7301 states protocol size <= 255 bytes.
            if (protocol.Length == 0 || protocol.Length > 255)
            {
                throw new ArgumentException("RFC 7301 states protocol size <= 255 bytes.");
            }

            _readOnlyProtocol = copy ?
                protocol.AsSpan().ToArray() :
                protocol;
        }

        public SslApplicationProtocol(byte[] protocol) :
            this(protocol ?? throw new ArgumentNullException(nameof(protocol)), copy: true)
        {
        }

        public SslApplicationProtocol(string protocol) :
            this(s_utf8.GetBytes(protocol ?? throw new ArgumentNullException(nameof(protocol))), copy: false)
        {
        }

        public ReadOnlyMemory<byte> Protocol => _readOnlyProtocol;

        public bool Equals(SslApplicationProtocol other) =>
            ((ReadOnlySpan<byte>)_readOnlyProtocol).SequenceEqual(other._readOnlyProtocol);

        //public override bool Equals([NotNullWhen(true)] object? obj) => obj is SslApplicationProtocol protocol && Equals(protocol);

        public override bool Equals(object obj)
        {
            if (obj is SslApplicationProtocol)
            {
                SslApplicationProtocol protocol = (SslApplicationProtocol)obj;
                return Equals(protocol);
            }

            return false;
        }
        public override int GetHashCode()
        {
            byte[] arr = _readOnlyProtocol;
            if (arr == null)
            {
                return 0;
            }

            int hash = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                hash = ((hash << 5) + hash) ^ arr[i];
            }

            return hash;
        }

        public override string ToString()
        {
            byte[] arr = _readOnlyProtocol;
            try
            {
                return
                    arr is null ? string.Empty :
                    ReferenceEquals(arr, s_http3Utf8) ? "h3" :
                    ReferenceEquals(arr, s_http2Utf8) ? "h2" :
                    ReferenceEquals(arr, s_http11Utf8) ? "http/1.1" :
                    s_utf8.GetString(arr);
            }
            catch
            {
                // In case of decoding errors, return the byte values as hex string.
                char[] byteChars = new char[arr.Length * 5];
                int index = 0;

                for (int i = 0; i < byteChars.Length; i += 5)
                {
                    byte b = arr[index++];
                    byteChars[i] = '0';
                    byteChars[i + 1] = 'x';
                    byteChars[i + 2] = ToCharLower(b >> 4);
                    byteChars[i + 3] = ToCharLower(b);
                    byteChars[i + 4] = ' ';
                }
                

                return new string(byteChars, 0, byteChars.Length - 1);
            }
            char ToCharLower(int value)
            {
                if (value >= 0 && value <= 9)
                {
                    return (char)('0' + value);
                }
                else if (value >= 10 && value <= 15)
                {
                    return (char)('a' + value - 10);
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
            }

        }

        public static bool operator ==(SslApplicationProtocol left, SslApplicationProtocol right) =>
            left.Equals(right);

        public static bool operator !=(SslApplicationProtocol left, SslApplicationProtocol right) =>
            !(left == right);
    }
}
