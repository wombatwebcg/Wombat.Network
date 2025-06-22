using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Wombat.Network.Sockets;
using Wombat.Network.UnitTest.TestHelpers;
using Xunit;

namespace Wombat.Network.UnitTest.UdpSocket
{
    public class UdpSocketServerTests : NetworkTestBase
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateServer()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockUdpServerEventDispatcher();
            var config = CreateDefaultUdpServerConfiguration();

            // Act
            var server = new UdpSocketServer(port, dispatcher, config);

            // Assert
            server.Should().NotBeNull();
            server.ListenedEndPoint.Port.Should().Be(port);
            server.State.Should().Be(UdpSocketConnectionState.None);
            server.SessionCount.Should().Be(0);
        }

        [Fact]
        public void Constructor_WithNullDispatcher_ShouldThrowArgumentNullException()
        {
            // Arrange
            var port = GetAvailablePort();
            var config = CreateDefaultUdpServerConfiguration();

            // Act & Assert
            Action act = () => new UdpSocketServer(port, null!, config);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public async Task Listen_ShouldStartListening()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockUdpServerEventDispatcher();
            var config = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, dispatcher, config);

            try
            {
                // Act
                await server.Listen(_testCancellationTokenSource.Token);

                // Assert
                server.State.Should().Be(UdpSocketConnectionState.Active);
            }
            finally
            {
                try { await server.Close(); } catch { }
            }
        }

        [Fact]
        public async Task Listen_WhenAlreadyListening_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockUdpServerEventDispatcher();
            var config = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, dispatcher, config);
            
            try
            {
                await server.Listen(_testCancellationTokenSource.Token);

                // Act & Assert
                await Assert.ThrowsAsync<InvalidOperationException>(async () => 
                    await server.Listen(_testCancellationTokenSource.Token));
            }
            finally
            {
                try { await server.Close(); } catch { }
            }
        }

        [Fact]
        public async Task Close_ShouldStopListening()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockUdpServerEventDispatcher();
            var config = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, dispatcher, config);
            await server.Listen(_testCancellationTokenSource.Token);

            // Act
            await server.Close();

            // Assert
            server.State.Should().Be(UdpSocketConnectionState.Closed);
        }

        [Fact]
        public async Task ReceiveData_ShouldCreateSession()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockUdpServerEventDispatcher();
            var serverConfig = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, serverDispatcher, serverConfig);
            UdpSocketClient? client = null;

            try
            {
                await server.Listen(_testCancellationTokenSource.Token);
                await Task.Delay(200);

                var clientDispatcher = new MockUdpClientEventDispatcher();
                var clientConfig = CreateDefaultUdpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client = new UdpSocketClient(endpoint, clientDispatcher, clientConfig);

                // Act
                await client.Connect(_testCancellationTokenSource.Token);
                
                // 发送数据以触发会话创建
                var testData = GenerateTestData(100);
                await client.SendAsync(testData, _testCancellationTokenSource.Token);

                // Assert - 等待服务器处理数据并创建会话
                var sessionCreated = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));
                
                sessionCreated.Should().BeTrue();
                serverDispatcher.StartedSessions.Count.Should().Be(1);
                server.SessionCount.Should().Be(1);
                
                // 验证接收到的数据
                var dataReceived = await WaitForConditionAsync(
                    () => serverDispatcher.ReceivedData.Count > 0, 
                    TimeSpan.FromSeconds(5));
                dataReceived.Should().BeTrue();
                serverDispatcher.ReceivedData[0].data.Should().BeEquivalentTo(testData);
            }
            finally
            {
                if (client != null)
                {
                    try { await client.Close(); } catch { }
                }
                
                try { await server.Close(); } catch { }
            }
        }

        [Fact]
        public async Task SendToAsync_WithValidSession_ShouldSendData()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockUdpServerEventDispatcher();
            var serverConfig = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, serverDispatcher, serverConfig);
            UdpSocketClient? client = null;

            try
            {
                await server.Listen(_testCancellationTokenSource.Token);
                await Task.Delay(200);

                var clientDispatcher = new MockUdpClientEventDispatcher();
                var clientConfig = CreateDefaultUdpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client = new UdpSocketClient(endpoint, clientDispatcher, clientConfig);

                // 建立连接并创建会话
                await client.Connect(_testCancellationTokenSource.Token);
                
                var initialData = GenerateTestData(50);
                await client.SendAsync(initialData, _testCancellationTokenSource.Token);

                // 等待服务器创建会话
                var sessionCreated = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));
                sessionCreated.Should().BeTrue();

                // 使用客户端端点作为会话键（这是UDP服务器内部使用的键）
                var actualSessionKey = client.LocalEndPoint.ToString();
                var testData = GenerateTestData(200);

                // Act
                await server.SendToAsync(actualSessionKey, testData, _testCancellationTokenSource.Token);

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
                
                try { await server.Close(); } catch { }
            }
        }

        [Fact]
        public async Task SendToAsync_WithInvalidSession_ShouldLogWarning()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockUdpServerEventDispatcher();
            var serverConfig = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, serverDispatcher, serverConfig);
            
            try
            {
                await server.Listen(_testCancellationTokenSource.Token);
                await Task.Delay(200);

                var testData = GenerateTestData(100);

                // Act - 发送到不存在的会话
                await server.SendToAsync("nonexistent", testData, _testCancellationTokenSource.Token);

                // Assert - 不应该抛出异常，只应该记录警告
                // (这里我们只验证没有抛出异常)
            }
            finally
            {
                try { await server.Close(); } catch { }
            }
        }

        [Fact]
        public async Task BroadcastAsync_WithMultipleClients_ShouldSendToAll()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockUdpServerEventDispatcher();
            var serverConfig = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, serverDispatcher, serverConfig);
            UdpSocketClient? client1 = null;
            UdpSocketClient? client2 = null;

            try
            {
                await server.Listen(_testCancellationTokenSource.Token);
                await Task.Delay(200);

                // 创建两个客户端
                var client1Dispatcher = new MockUdpClientEventDispatcher();
                var client2Dispatcher = new MockUdpClientEventDispatcher();
                var clientConfig = CreateDefaultUdpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client1 = new UdpSocketClient(endpoint, client1Dispatcher, clientConfig);
                client2 = new UdpSocketClient(endpoint, client2Dispatcher, clientConfig);

                // 连接两个客户端并发送初始数据
                await client1.Connect(_testCancellationTokenSource.Token);
                await client2.Connect(_testCancellationTokenSource.Token);
                
                var initialData1 = GenerateTestData(30);
                var initialData2 = GenerateTestData(40);
                
                await client1.SendAsync(initialData1, _testCancellationTokenSource.Token);
                await Task.Delay(100);
                await client2.SendAsync(initialData2, _testCancellationTokenSource.Token);

                // 等待服务器创建两个会话
                var bothSessionsCreated = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count >= 2, 
                    TimeSpan.FromSeconds(5));
                bothSessionsCreated.Should().BeTrue();

                var testData = GenerateTestData(250);

                // Act - 广播数据
                await server.BroadcastAsync(testData, _testCancellationTokenSource.Token);

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
                
                try { await server.Close(); } catch { }
            }
        }

        [Fact]
        public async Task CloseSession_ShouldRemoveSession()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockUdpServerEventDispatcher();
            var serverConfig = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, serverDispatcher, serverConfig);
            UdpSocketClient? client = null;

            try
            {
                await server.Listen(_testCancellationTokenSource.Token);
                await Task.Delay(200);

                var clientDispatcher = new MockUdpClientEventDispatcher();
                var clientConfig = CreateDefaultUdpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client = new UdpSocketClient(endpoint, clientDispatcher, clientConfig);

                // 建立连接并创建会话
                await client.Connect(_testCancellationTokenSource.Token);
                
                var initialData = GenerateTestData(50);
                await client.SendAsync(initialData, _testCancellationTokenSource.Token);

                // 等待服务器创建会话
                var sessionCreated = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));
                sessionCreated.Should().BeTrue();

                // 使用客户端端点作为会话键
                var actualSessionKey = client.LocalEndPoint.ToString();
                server.SessionCount.Should().Be(1);

                // Act - 关闭会话
                await server.CloseSession(actualSessionKey);

                // Assert - 会话应该被移除
                var sessionRemoved = await WaitForConditionAsync(
                    () => server.SessionCount == 0, 
                    TimeSpan.FromSeconds(5));
                sessionRemoved.Should().BeTrue();
                
                // 验证会话已关闭
                var sessionClosed = await WaitForConditionAsync(
                    () => serverDispatcher.ClosedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));
                sessionClosed.Should().BeTrue();
            }
            finally
            {
                if (client != null)
                {
                    try { await client.Close(); } catch { }
                }
                
                try { await server.Close(); } catch { }
            }
        }

        [Fact]
        public async Task HasSession_WithExistingSession_ShouldReturnTrue()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockUdpServerEventDispatcher();
            var serverConfig = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, serverDispatcher, serverConfig);
            UdpSocketClient? client = null;

            try
            {
                await server.Listen(_testCancellationTokenSource.Token);
                await Task.Delay(200);

                var clientDispatcher = new MockUdpClientEventDispatcher();
                var clientConfig = CreateDefaultUdpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client = new UdpSocketClient(endpoint, clientDispatcher, clientConfig);

                // 建立连接并创建会话
                await client.Connect(_testCancellationTokenSource.Token);
                
                var initialData = GenerateTestData(50);
                await client.SendAsync(initialData, _testCancellationTokenSource.Token);

                // 等待服务器创建会话
                var sessionCreated = await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));
                sessionCreated.Should().BeTrue();

                // 使用客户端端点作为会话键
                var actualSessionKey = client.LocalEndPoint.ToString();

                // Act & Assert
                server.HasSession(actualSessionKey).Should().BeTrue();
                server.GetSession(actualSessionKey).Should().NotBeNull();
                
                // 使用客户端终结点测试
                var clientEndpoint = client.LocalEndPoint;
                server.HasSession(clientEndpoint).Should().BeTrue();
                server.GetSession(clientEndpoint).Should().NotBeNull();
            }
            finally
            {
                if (client != null)
                {
                    try { await client.Close(); } catch { }
                }
                
                try { await server.Close(); } catch { }
            }
        }

        [Fact]
        public void GetSession_WithNonExistentSession_ShouldReturnNull()
        {
            // Arrange
            var port = GetAvailablePort();
            var dispatcher = new MockUdpServerEventDispatcher();
            var config = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, dispatcher, config);

            // Act & Assert
            server.HasSession("nonexistent").Should().BeFalse();
            server.GetSession("nonexistent").Should().BeNull();
            
            var nonExistentEndpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            server.HasSession(nonExistentEndpoint).Should().BeFalse();
            server.GetSession(nonExistentEndpoint).Should().BeNull();
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
} 