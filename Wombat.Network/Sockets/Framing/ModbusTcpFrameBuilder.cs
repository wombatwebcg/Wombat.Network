using System;
using System.Collections.Generic;
using System.Text;

namespace Wombat.Network.Sockets
{    public sealed class ModbusTcpFrameBuilder : FrameBuilder
    {
         public static int Number { get; set; }
        public ModbusTcpFrameBuilder(): this(new ModbusTcpFrameEncoder(), new ModbusTcpFrameDecoder())
        {
        }

        public ModbusTcpFrameBuilder(ModbusTcpFrameEncoder encoder, ModbusTcpFrameDecoder decoder)
            : base(encoder, decoder)
        {
        }

    }
    public sealed class ModbusTcpFrameEncoder : IFrameEncoder
    {
        private  int _number;
        public ModbusTcpFrameEncoder()
        {
        }


        public void EncodeFrame(byte[] payload, int offset, int count, out byte[] frameBuffer, out int frameBufferOffset, out int frameBufferLength)
        {
            byte[] numberBuff = new byte[2] {payload[1],payload[0] };
            ModbusTcpFrameBuilder.Number =  BitConverter.ToUInt16(numberBuff,0);
            frameBuffer = payload;
            frameBufferOffset = offset;
            frameBufferLength = count;
        }
    }

    public sealed class ModbusTcpFrameDecoder : IFrameDecoder
    {
        public ModbusTcpFrameDecoder()
        {
        }


        public bool TryDecodeFrame(byte[] buffer, int offset, int count, out int frameLength, out byte[] payload, out int payloadOffset, out int payloadCount)
        {
            frameLength = 0;
            payload = null;
            payloadOffset = 0;
            payloadCount = 0;
            byte[] numberBuff = new byte[2] { buffer[1], buffer[0] };
            var sendNumber = BitConverter.ToUInt16(numberBuff, 0);

            if (count <= 0)
                return false;

            frameLength = count;
            int buffcount = buffer[5] + 6;
            payload = new byte[buffcount];
            Array.Copy(buffer, 0, payload, 0, buffcount);
            //payload = buffer;
            payloadOffset = offset;
            payloadCount = count;
            if (sendNumber != ModbusTcpFrameBuilder.Number)
                return false;
            return true;
        }
    }

}


