using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Wombat.Network.Sockets;
using Wombat.Network.UnitTest.TestHelpers;
using Xunit;

namespace Wombat.Network.UnitTest.TcpSocket
{
    public class TcpSocketServerTests : NetworkTestBase
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateServer()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockTcpServerEventDispatcher();
            var config = CreateDefaultTcpServerConfiguration();

            // Act
            var server = new TcpSocketServer(port, dispatcher, config);

            // Assert
            server.Should().NotBeNull();
            server.ListenedEndPoint.Port.Should().Be(port);
            server.IsListening.Should().BeFalse();
            server.SessionCount.Should().Be(0);
        }

        [Fact]
        public void Constructor_WithNullDispatcher_ShouldThrowArgumentNullException()
        {
            // Arrange
            var port = GetAvailablePort();
            var config = CreateDefaultTcpServerConfiguration();

            // Act & Assert
            Action act = () => new TcpSocketServer(port, null!, config);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Listen_ShouldStartListening()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockTcpServerEventDispatcher();
            var config = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, dispatcher, config);

            try
            {
                // Act
                server.Listen();

                // Assert
                server.IsListening.Should().BeTrue();
            }
            finally
            {
                try { server.Shutdown(); } catch { }
            }
        }

        [Fact]
        public void Listen_WhenAlreadyListening_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockTcpServerEventDispatcher();
            var config = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, dispatcher, config);
            
            try
            {
                server.Listen();

                // Act & Assert
                Action act = () => server.Listen();
                act.Should().Throw<InvalidOperationException>();
            }
            finally
            {
                try { server.Shutdown(); } catch { }
            }
        }

        [Fact]
        public void Shutdown_ShouldStopListening()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockTcpServerEventDispatcher();
            var config = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, dispatcher, config);
            server.Listen();

            // Act
            server.Shutdown();

            // Assert
            server.IsListening.Should().BeFalse();
        }

        [Fact]
        public async Task AcceptClient_ShouldCreateSession()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockTcpServerEventDispatcher();
            var serverConfig = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, serverDispatcher, serverConfig);
            TcpSocketClient? client = null;

            try
            {
                server.Listen();
                await Task.Delay(200);

                var clientDispatcher = new MockTcpClientEventDispatcher();
                var clientConfig = CreateDefaultTcpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client = new TcpSocketClient(endpoint, clientDispatcher, clientConfig);

                // Act
                await client.Connect(_testCancellationTokenSource.Token);

                // Assert - 等待服务器处理连接
                var sessionCreated = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));
                
                sessionCreated.Should().BeTrue();
                serverDispatcher.StartedSessions.Count.Should().Be(1);
                server.SessionCount.Should().Be(1);
            }
            finally
            {
                if (client != null)
                {
                    try { await client.Close(); } catch { }
                }
                
                try { server.Shutdown(); } catch { }
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task SendToAsync_WithValidSession_ShouldSendData()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockTcpServerEventDispatcher();
            var serverConfig = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, serverDispatcher, serverConfig);
            TcpSocketClient? client = null;

            try
            {
                server.Listen();
                await Task.Delay(200);

                var clientDispatcher = new MockTcpClientEventDispatcher();
                var clientConfig = CreateDefaultTcpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client = new TcpSocketClient(endpoint, clientDispatcher, clientConfig);

                // 建立连接
                await client.Connect(_testCancellationTokenSource.Token);

                // 等待服务器接受连接
                var sessionCreated = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));
                sessionCreated.Should().BeTrue();

                var sessionKey = serverDispatcher.StartedSessions[0];
                var testData = GenerateTestData(100);

                // Act
                await server.SendToAsync(sessionKey, testData);

                // Assert - 等待客户端接收数据
                var dataReceived = await WaitForConditionAsync(
                    () => clientDispatcher.ReceivedData.Count > 0, 
                    TimeSpan.FromSeconds(5));
                
                dataReceived.Should().BeTrue();
                clientDispatcher.ReceivedData[0].data.Should().BeEquivalentTo(testData);
            }
            finally
            {
                if (client != null)
                {
                    try { await client.Close(); } catch { }
                }
                
                try { server.Shutdown(); } catch { }
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task SendToAsync_WithInvalidSession_ShouldLogWarning()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockTcpServerEventDispatcher();
            var serverConfig = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, serverDispatcher, serverConfig);
            
            try
            {
                server.Listen();
                await Task.Delay(200);

                var testData = GenerateTestData(100);

                // Act - 发送到不存在的会话
                await server.SendToAsync("nonexistent", testData);

                // Assert - 不应该抛出异常，只应该记录警告
                // (这里我们只验证没有抛出异常)
            }
            finally
            {
                try { server.Shutdown(); } catch { }
            }
        }

        [Fact]
        public async Task BroadcastAsync_WithMultipleClients_ShouldSendToAll()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockTcpServerEventDispatcher();
            var serverConfig = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, serverDispatcher, serverConfig);
            TcpSocketClient? client1 = null;
            TcpSocketClient? client2 = null;

            try
            {
                server.Listen();
                await Task.Delay(200);

                // 创建两个客户端
                var client1Dispatcher = new MockTcpClientEventDispatcher();
                var client2Dispatcher = new MockTcpClientEventDispatcher();
                var clientConfig = CreateDefaultTcpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client1 = new TcpSocketClient(endpoint, client1Dispatcher, clientConfig);
                client2 = new TcpSocketClient(endpoint, client2Dispatcher, clientConfig);

                // 连接两个客户端
                await client1.Connect(_testCancellationTokenSource.Token);
                await Task.Delay(100);
                await client2.Connect(_testCancellationTokenSource.Token);

                // 等待服务器接受两个连接
                var bothConnected = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count >= 2, 
                    TimeSpan.FromSeconds(5));
                bothConnected.Should().BeTrue();

                var testData = GenerateTestData(150);

                // Act - 广播数据
                await server.BroadcastAsync(testData);

                // Assert - 等待两个客户端都接收到数据
                var client1Received = await WaitForConditionAsync(
                    () => client1Dispatcher.ReceivedData.Count > 0, 
                    TimeSpan.FromSeconds(5));
                var client2Received = await WaitForConditionAsync(
                    () => client2Dispatcher.ReceivedData.Count > 0, 
                    TimeSpan.FromSeconds(5));
                
                client1Received.Should().BeTrue();
                client2Received.Should().BeTrue();
                client1Dispatcher.ReceivedData[0].data.Should().BeEquivalentTo(testData);
                client2Dispatcher.ReceivedData[0].data.Should().BeEquivalentTo(testData);
            }
            finally
            {
                if (client1 != null)
                {
                    try { await client1.Close(); } catch { }
                }
                
                if (client2 != null)
                {
                    try { await client2.Close(); } catch { }
                }
                
                try { server.Shutdown(); } catch { }
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task CloseSession_ShouldRemoveSession()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockTcpServerEventDispatcher();
            var serverConfig = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, serverDispatcher, serverConfig);
            TcpSocketClient? client = null;

            try
            {
                server.Listen();
                await Task.Delay(200);

                var clientDispatcher = new MockTcpClientEventDispatcher();
                var clientConfig = CreateDefaultTcpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client = new TcpSocketClient(endpoint, clientDispatcher, clientConfig);

                // 建立连接
                await client.Connect(_testCancellationTokenSource.Token);

                // 等待服务器接受连接
                var sessionCreated = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));
                sessionCreated.Should().BeTrue();

                var sessionKey = serverDispatcher.StartedSessions[0];
                server.SessionCount.Should().Be(1);

                // Act - 关闭会话
                await server.CloseSession(sessionKey);

                // Assert - 会话应该被移除
                var sessionRemoved = await WaitForConditionAsync(
                    () => server.SessionCount == 0, 
                    TimeSpan.FromSeconds(5));
                sessionRemoved.Should().BeTrue();
                
                // 客户端应该检测到断开连接
                var clientDisconnected = await WaitForConditionAsync(
                    () => clientDispatcher.DisconnectedCount > 0, 
                    TimeSpan.FromSeconds(5));
                clientDisconnected.Should().BeTrue();
            }
            finally
            {
                if (client != null)
                {
                    try { await client.Close(); } catch { }
                }
                
                try { server.Shutdown(); } catch { }
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task HasSession_WithExistingSession_ShouldReturnTrue()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockTcpServerEventDispatcher();
            var serverConfig = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, serverDispatcher, serverConfig);
            TcpSocketClient? client = null;

            try
            {
                server.Listen();
                await Task.Delay(200);

                var clientDispatcher = new MockTcpClientEventDispatcher();
                var clientConfig = CreateDefaultTcpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client = new TcpSocketClient(endpoint, clientDispatcher, clientConfig);

                // 建立连接
                await client.Connect(_testCancellationTokenSource.Token);

                // 等待服务器接受连接
                var sessionCreated = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));
                sessionCreated.Should().BeTrue();

                var sessionKey = serverDispatcher.StartedSessions[0];

                // Act & Assert
                server.HasSession(sessionKey).Should().BeTrue();
                server.GetSession(sessionKey).Should().NotBeNull();
            }
            finally
            {
                if (client != null)
                {
                    try { await client.Close(); } catch { }
                }
                
                try { server.Shutdown(); } catch { }
                await Task.Delay(100);
            }
        }

        [Fact]
        public void GetSession_WithNonExistentSession_ShouldReturnNull()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockTcpServerEventDispatcher();
            var config = CreateDefaultTcpServerConfiguration();
            
            var server = new TcpSocketServer(port, dispatcher, config);

            try
            {
                server.Listen();

                // Act & Assert
                server.HasSession("nonexistent").Should().BeFalse();
                server.GetSession("nonexistent").Should().BeNull();
            }
            finally
            {
                try { server.Shutdown(); } catch { }
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
} 