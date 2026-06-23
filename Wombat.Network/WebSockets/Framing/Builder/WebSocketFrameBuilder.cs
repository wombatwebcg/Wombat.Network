using System;
using System.Text;

namespace Wombat.Network.WebSockets
{
    internal sealed class WebSocketFrameBuilder
    {
        private static readonly byte[] EmptyArray = Array.Empty<byte>();
        private static readonly Random Rng = new Random(DateTime.UtcNow.Millisecond);
        private const int MaskingKeyLength = 4;

        public byte[] EncodeFrame(PingFrame frame)
        {
            var data = string.IsNullOrEmpty(frame.Data) ? EmptyArray : Encoding.UTF8.GetBytes(frame.Data);
            if (data.Length > 125) throw new InvalidOperationException("All control frames must have a payload length of 125 bytes or less.");
            return Encode(frame.OpCode, data, 0, data.Length, frame.IsMasked);
        }

        public byte[] EncodeFrame(PongFrame frame)
        {
            var data = string.IsNullOrEmpty(frame.Data) ? EmptyArray : Encoding.UTF8.GetBytes(frame.Data);
            if (data.Length > 125) throw new InvalidOperationException("All control frames must have a payload length of 125 bytes or less.");
            return Encode(frame.OpCode, data, 0, data.Length, frame.IsMasked);
        }

        public byte[] EncodeFrame(CloseFrame frame)
        {
            var payloadLength = (string.IsNullOrEmpty(frame.CloseReason) ? 0 : Encoding.UTF8.GetMaxByteCount(frame.CloseReason.Length)) + 2;
            if (payloadLength > 125) throw new InvalidOperationException("All control frames must have a payload length of 125 bytes or less.");

            var payload = new byte[payloadLength];
            payload[0] = (byte)((int)frame.CloseCode / 256);
            payload[1] = (byte)((int)frame.CloseCode % 256);
            if (!string.IsNullOrEmpty(frame.CloseReason))
            {
                var count = Encoding.UTF8.GetBytes(frame.CloseReason, 0, frame.CloseReason.Length, payload, 2);
                return Encode(frame.OpCode, payload, 0, 2 + count, frame.IsMasked);
            }

            return Encode(frame.OpCode, payload, 0, payload.Length, frame.IsMasked);
        }

        public byte[] EncodeFrame(TextFrame frame)
        {
            var data = string.IsNullOrEmpty(frame.Text) ? EmptyArray : Encoding.UTF8.GetBytes(frame.Text);
            return Encode(frame.OpCode, data, 0, data.Length, frame.IsMasked);
        }

        public byte[] EncodeFrame(BinaryFrame frame) => Encode(frame.OpCode, frame.Data, frame.Offset, frame.Count, frame.IsMasked);

        public byte[] EncodeFrame(BinaryFragmentationFrame frame) => Encode(frame.OpCode, frame.Data, frame.Offset, frame.Count, frame.IsMasked, frame.IsFin);

        public bool TryDecodeFrameHeader(byte[] buffer, int offset, int count, out Header frameHeader)
        {
            frameHeader = DecodeFrameHeader(buffer, offset, count);
            return frameHeader != null;
        }

        public void DecodePayload(byte[] buffer, int offset, Header frameHeader, out byte[] payload, out int payloadOffset, out int payloadCount)
        {
            payload = buffer;
            payloadOffset = offset + frameHeader.Length;
            payloadCount = frameHeader.PayloadLength;

            if (!frameHeader.IsMasked)
            {
                return;
            }

            payload = new byte[payloadCount];
            for (var i = 0; i < payloadCount; i++)
            {
                payload[i] = (byte)(buffer[payloadOffset + i] ^ buffer[offset + frameHeader.MaskingKeyOffset + i % MaskingKeyLength]);
            }

            payloadOffset = 0;
            payloadCount = payload.Length;
        }

        private static byte[] Encode(OpCode opCode, byte[] payload, int offset, int count, bool isMasked = true, bool isFin = true)
        {
            byte[] fragment;
            if (count < 126)
            {
                fragment = new byte[2 + (isMasked ? MaskingKeyLength : 0) + count];
                fragment[1] = (byte)count;
            }
            else if (count < 65536)
            {
                fragment = new byte[4 + (isMasked ? MaskingKeyLength : 0) + count];
                fragment[1] = 126;
                fragment[2] = (byte)(count / 256);
                fragment[3] = (byte)(count % 256);
            }
            else
            {
                fragment = new byte[10 + (isMasked ? MaskingKeyLength : 0) + count];
                fragment[1] = 127;
                var left = count;
                for (var i = 9; i > 1; i--)
                {
                    fragment[i] = (byte)(left % 256);
                    left /= 256;
                    if (left == 0) break;
                }
            }

            if (isFin) fragment[0] = 0x80;
            fragment[0] = (byte)(fragment[0] | (byte)opCode);
            if (isMasked) fragment[1] = (byte)(fragment[1] | 0x80);

            if (isMasked)
            {
                var maskingKeyIndex = fragment.Length - (MaskingKeyLength + count);
                for (var i = maskingKeyIndex; i < maskingKeyIndex + MaskingKeyLength; i++)
                {
                    fragment[i] = (byte)Rng.Next(0, 255);
                }

                for (var i = 0; i < count; i++)
                {
                    fragment[fragment.Length - count + i] = (byte)(payload[offset + i] ^ fragment[maskingKeyIndex + i % MaskingKeyLength]);
                }
            }
            else if (count > 0)
            {
                Array.Copy(payload, offset, fragment, fragment.Length - count, count);
            }

            return fragment;
        }

        private static Header DecodeFrameHeader(byte[] buffer, int offset, int count)
        {
            if (count < 2) return null;

            var header = new Header
            {
                IsFIN = (buffer[offset] & 0x80) == 0x80,
                IsRSV1 = (buffer[offset] & 0x40) == 0x40,
                IsRSV2 = (buffer[offset] & 0x20) == 0x20,
                IsRSV3 = (buffer[offset] & 0x10) == 0x10,
                OpCode = (OpCode)(buffer[offset] & 0x0f),
                IsMasked = (buffer[offset + 1] & 0x80) == 0x80,
                PayloadLength = buffer[offset + 1] & 0x7f,
                Length = 2,
            };

            if (header.PayloadLength >= 126)
            {
                header.Length += header.PayloadLength == 126 ? 2 : 8;
                if (count < header.Length) return null;

                if (header.PayloadLength == 126)
                {
                    header.PayloadLength = buffer[offset + 2] * 256 + buffer[offset + 3];
                }
                else
                {
                    var totalLength = 0;
                    var level = 1;
                    for (var i = 7; i >= 0; i--)
                    {
                        totalLength += buffer[offset + i + 2] * level;
                        level *= 256;
                    }

                    header.PayloadLength = totalLength;
                }
            }

            if (header.IsMasked)
            {
                if (count < header.Length + MaskingKeyLength) return null;
                header.MaskingKeyOffset = header.Length;
                header.Length += MaskingKeyLength;
            }

            return header;
        }
    }
}
