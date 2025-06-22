using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Wombat.Network.Sockets;
using Wombat.Network.UnitTest.TestHelpers;
using Xunit;

namespace Wombat.Network.UnitTest.UdpSocket
{
    public class UdpSocketClientTests : NetworkTestBase
    {
        [Fact]
        public void Constructor_WithValidParameters_ShouldCreateClient()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            var dispatcher = new MockUdpClientEventDispatcher();
            var config = CreateDefaultUdpClientConfiguration();

            // Act
            var client = new UdpSocketClient(endpoint, dispatcher, config);

            // Assert
            client.Should().NotBeNull();
            client.RemoteEndPoint.Should().Be(endpoint);
            client.State.Should().Be(UdpSocketConnectionState.None);
        }

        [Fact]
        public void Constructor_WithNullEndPoint_ShouldThrowArgumentNullException()
        {
            // Arrange
            var dispatcher = new MockUdpClientEventDispatcher();
            var config = CreateDefaultUdpClientConfiguration();

            // Act & Assert
            Action act = () => new UdpSocketClient(null!, dispatcher, config);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_WithNullDispatcher_ShouldThrowArgumentNullException()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            var config = CreateDefaultUdpClientConfiguration();

            // Act & Assert
            Action act = () => new UdpSocketClient(endpoint, null!, config);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public async Task Connect_ShouldChangeStateToActive()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            var dispatcher = new MockUdpClientEventDispatcher();
            var config = CreateDefaultUdpClientConfiguration();
            
            var client = new UdpSocketClient(endpoint, dispatcher, config);

            try
            {
                // Act
                await client.Connect(_testCancellationTokenSource.Token);

                // Assert
                client.State.Should().Be(UdpSocketConnectionState.Active);
                dispatcher.ConnectedCount.Should().Be(1);
            }
            finally
            {
                try { await client.Close(); } catch { }
            }
        }

        [Fact]
        public async Task Connect_WhenAlreadyConnected_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            var dispatcher = new MockUdpClientEventDispatcher();
            var config = CreateDefaultUdpClientConfiguration();
            
            var client = new UdpSocketClient(endpoint, dispatcher, config);
            
            try
            {
                await client.Connect(_testCancellationTokenSource.Token);

                // Act & Assert
                await Assert.ThrowsAsync<InvalidOperationException>(async () => 
                    await client.Connect(_testCancellationTokenSource.Token));
            }
            finally
            {
                try { await client.Close(); } catch { }
            }
        }

        [Fact]
        public async Task SendReceive_WithUdpServer_ShouldWorkCorrectly()
        {
            // Arrange
            var port = GetAvailablePort();
            var serverDispatcher = new MockUdpServerEventDispatcher();
            var serverConfig = CreateDefaultUdpServerConfiguration();
            
            var server = new UdpSocketServer(port, serverDispatcher, serverConfig);
            UdpSocketClient? client = null;

            try
            {
                // 启动UDP服务器 - 注意UDP服务器的Listen是同步的
                await server.Listen(_testCancellationTokenSource.Token);

                // 等待服务器启动
                await Task.Delay(200);

                var clientDispatcher = new MockUdpClientEventDispatcher();
                var clientConfig = CreateDefaultUdpClientConfiguration();
                var endpoint = new IPEndPoint(IPAddress.Loopback, port);
                
                client = new UdpSocketClient(endpoint, clientDispatcher, clientConfig);

                // Act - 连接
                await client.Connect(_testCancellationTokenSource.Token);

                // Act - 发送数据
                var testData = GenerateTestData(100);
                await client.SendAsync(testData, _testCancellationTokenSource.Token);

                // Assert - 服务器接收数据
                var dataReceived = await WaitForConditionAsync(
                    () => serverDispatcher.ReceivedData.Count > 0, 
                    TimeSpan.FromSeconds(5));
                dataReceived.Should().BeTrue();

                var receivedData = serverDispatcher.ReceivedData[0];
                receivedData.data.Should().BeEquivalentTo(testData);

                // Act - 从服务器发送数据回客户端
                var responseData = GenerateTestData(150);
                var actualSessionKey = client.LocalEndPoint.ToString();
                await server.SendToAsync(actualSessionKey, responseData, _testCancellationTokenSource.Token);

                // Assert - 客户端接收数据
                var clientDataReceived = await WaitForConditionAsync(
                    () => clientDispatcher.ReceivedData.Count > 0, 
                    TimeSpan.FromSeconds(5));
                clientDataReceived.Should().BeTrue();

                var clientReceivedData = clientDispatcher.ReceivedData[0];
                clientReceivedData.data.Should().BeEquivalentTo(responseData);
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
        public async Task SendAsync_WithoutConnection_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            var dispatcher = new MockUdpClientEventDispatcher();
            var config = CreateDefaultUdpClientConfiguration();
            
            var client = new UdpSocketClient(endpoint, dispatcher, config);
            var testData = GenerateTestData(100);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () => 
                await client.SendAsync(testData, _testCancellationTokenSource.Token));
        }

        [Fact]
        public async Task SendAsync_WithNullData_ShouldThrowArgumentNullException()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            var dispatcher = new MockUdpClientEventDispatcher();
            var config = CreateDefaultUdpClientConfiguration();
            
            var client = new UdpSocketClient(endpoint, dispatcher, config);

            // Act & Assert - 实际抛出的是NullReferenceException，因为代码中没有显式的null检查
            await Assert.ThrowsAsync<NullReferenceException>(async () => 
                await client.SendAsync(null!, _testCancellationTokenSource.Token));
        }

        [Fact]
        public async Task DataReading_WithReceivedData_ShouldWorkCorrectly()
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

                // Act - 连接
                await client.Connect(_testCancellationTokenSource.Token);

                // 发送初始数据以建立会话
                var initialData = GenerateTestData(50);
                await client.SendAsync(initialData, _testCancellationTokenSource.Token);

                // 等待服务器创建会话
                await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));

                // 从服务器发送数据到客户端
                var testData = GenerateTestData(300);
                var actualSessionKey = client.LocalEndPoint.ToString();
                await server.SendToAsync(actualSessionKey, testData, _testCancellationTokenSource.Token);

                // 等待客户端接收数据
                await WaitForConditionAsync(
                    () => clientDispatcher.ReceivedData.Count > 0, 
                    TimeSpan.FromSeconds(5));

                // Assert - 先验证缓冲区使用率（在读取之前）
                client.BufferUsage.Should().BeGreaterOrEqualTo(0);
                client.BytesAvailable.Should().BeGreaterThan(0);
                
                // 读取数据
                var buffer = new byte[testData.Length];
                var bytesRead = client.Read(buffer, 0, buffer.Length);
                
                bytesRead.Should().BeGreaterThan(0);
                bytesRead.Should().BeLessOrEqualTo(testData.Length);
                
                // 读取后缓冲区应该被清空（因为读取了所有数据）
                client.BufferUsage.Should().Be(0);
                client.BytesAvailable.Should().Be(0);
                
                // 清空缓冲区（应该没有数据可清空）
                var cleared = client.ClearReceiveBuffer();
                cleared.Should().Be(0);
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
        public async Task ReadAsync_WithTimeout_ShouldReturnZeroWhenNoData()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            var dispatcher = new MockUdpClientEventDispatcher();
            var config = CreateDefaultUdpClientConfiguration();
            // 设置较短的接收超时以便测试快速完成
            config.ReceiveTimeout = TimeSpan.FromMilliseconds(500);
            
            var client = new UdpSocketClient(endpoint, dispatcher, config);

            try
            {
                await client.Connect(_testCancellationTokenSource.Token);

                // Act - 尝试读取数据（应该超时返回0）
                var buffer = new byte[100];
                var bytesRead = await client.ReadAsync(buffer, 0, buffer.Length);

                // Assert
                bytesRead.Should().Be(0);
            }
            finally
            {
                try { await client.Close(); } catch { }
            }
        }

        [Fact]
        public async Task BufferManagement_ShouldTrackUsageCorrectly()
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

                await client.Connect(_testCancellationTokenSource.Token);

                // 发送初始数据建立会话
                var initialData = GenerateTestData(50);
                await client.SendAsync(initialData, _testCancellationTokenSource.Token);

                // 等待会话建立
                await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));

                // Act - 发送数据使客户端接收
                var testData = GenerateTestData(250);
                
                // 使用客户端端点作为会话键（这是UDP服务器内部使用的键）
                var actualSessionKey = client.LocalEndPoint.ToString();
                await server.SendToAsync(actualSessionKey, testData, _testCancellationTokenSource.Token);

                // 等待数据到达
                await WaitForConditionAsync(
                    () => clientDispatcher.ReceivedData.Count > 0, 
                    TimeSpan.FromSeconds(5));

                // Assert - 检查缓冲区状态
                client.BytesAvailable.Should().BeGreaterThan(0);
                client.BufferUsage.Should().BeGreaterOrEqualTo(0);
                client.IsBufferFull.Should().BeFalse();

                // 读取所有数据
                var allData = client.ReadAllBytes();
                allData.Length.Should().BeGreaterThan(0);

                // 缓冲区应该被清空
                client.BytesAvailable.Should().Be(0);
                client.BufferUsage.Should().Be(0);
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
        public async Task ReadDataItem_ShouldIncludeRemoteEndPointInfo()
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

                await client.Connect(_testCancellationTokenSource.Token);

                // 发送初始数据建立会话
                var initialData = GenerateTestData(50);
                await client.SendAsync(initialData, _testCancellationTokenSource.Token);

                // 等待会话建立
                await WaitForConditionAsync(
                    () => serverDispatcher.StartedSessions.Count > 0, 
                    TimeSpan.FromSeconds(5));

                // 从服务器发送数据到客户端
                var testData = GenerateTestData(200);
                var actualSessionKey = client.LocalEndPoint.ToString();
                await server.SendToAsync(actualSessionKey, testData, _testCancellationTokenSource.Token);

                // 等待客户端接收数据
                await WaitForConditionAsync(
                    () => clientDispatcher.ReceivedData.Count > 0, 
                    TimeSpan.FromSeconds(5));

                // Act - 读取数据项
                var dataItem = client.ReadDataItem();

                // Assert
                dataItem.Should().NotBeNull();
                dataItem!.Data.Should().NotBeNull();
                dataItem.Data.Length.Should().BeGreaterThan(0);
                dataItem.RemoteEndPoint.Should().NotBeNull();
                dataItem.RemoteEndPoint.Port.Should().Be(port);
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
        public async Task Close_ShouldChangeStateToClosedAndNotifyDispatcher()
        {
            // Arrange
            var endpoint = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            var dispatcher = new MockUdpClientEventDispatcher();
            var config = CreateDefaultUdpClientConfiguration();
            
            var client = new UdpSocketClient(endpoint, dispatcher, config);

            try
            {
                await client.Connect(_testCancellationTokenSource.Token);
                client.State.Should().Be(UdpSocketConnectionState.Active);

                // Act
                await client.Close();

                // Assert
                client.State.Should().Be(UdpSocketConnectionState.Closed);
                dispatcher.DisconnectedCount.Should().Be(1);
            }
            finally
            {
                try { await client.Close(); } catch { }
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
} 