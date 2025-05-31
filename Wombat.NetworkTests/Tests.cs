using System;
using Xunit;
using Wombat.Network.Sockets;
using Wombat.Network.WebSockets;
using Wombat.Network.Pipelines;

namespace Wombat.NetworkTests
{
    /// <summary>
    /// 基本测试类
    /// </summary>
    public class Tests
    {
        [Fact]
        public void ProjectStructureTest()
        {
            // 验证能够引用到主要组件
            Assert.NotNull(typeof(TcpSocketClient));
            Assert.NotNull(typeof(TcpSocketServer));
            Assert.NotNull(typeof(WebSocketClient));
            Assert.NotNull(typeof(WebSocketServer));
            Assert.NotNull(typeof(PipelineSocketConnection));
        }
    }
}
