using FluentAssertions;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using Wombat.Network.Protocols.Framing;
using Xunit;

namespace Wombat.Network.UnitTest.NewModel;

public class LengthFieldMessagePipeTests
{
    [Fact]
    public async Task WriteAsync_ThenTryRead_ShouldRoundTrip()
    {
        var pipe = new Pipe();
        var messagePipe = new LengthFieldMessagePipe(LengthField.FourBytes);
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        await messagePipe.WriteAsync(pipe.Writer, payload);
        pipe.Writer.Complete();

        var result = await pipe.Reader.ReadAsync();
        var buffer = result.Buffer;

        var success = messagePipe.TryRead(ref buffer, out var decoded);

        success.Should().BeTrue();
        decoded.ToArray().Should().Equal(payload);
        buffer.Length.Should().Be(0);
        pipe.Reader.AdvanceTo(result.Buffer.End);
    }

    [Fact]
    public void TryRead_WithHalfPacket_ShouldReturnFalse()
    {
        var messagePipe = new LengthFieldMessagePipe(LengthField.FourBytes);
        var partialFrame = new byte[] { 0, 0, 0, 5, 1, 2 };
        ReadOnlySequence<byte> buffer = new ReadOnlySequence<byte>(partialFrame);

        var success = messagePipe.TryRead(ref buffer, out var payload);

        success.Should().BeFalse();
        payload.IsEmpty.Should().BeTrue();
        buffer.ToArray().Should().Equal(partialFrame);
    }

    [Fact]
    public void TryRead_WithOversizedPacket_ShouldThrow()
    {
        var messagePipe = new LengthFieldMessagePipe(LengthField.FourBytes, maxPayloadLength: 4);
        var frame = new byte[] { 0, 0, 0, 5, 1, 2, 3, 4, 5 };
        ReadOnlySequence<byte> buffer = new ReadOnlySequence<byte>(frame);

        Action act = () => messagePipe.TryRead(ref buffer, out _);

        act.Should().Throw<InvalidOperationException>();
    }
}
