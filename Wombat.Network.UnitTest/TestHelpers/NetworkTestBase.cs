using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Wombat.Network.UnitTest.TestHelpers
{
    /// <summary>
    /// 网络测试基类，提供通用的测试基础设施
    /// </summary>
    public abstract class NetworkTestBase : IDisposable
    {
        protected readonly CancellationTokenSource _testCancellationTokenSource;
        protected readonly Random _random;
        
        // 测试用端口范围 (避免与系统端口冲突)
        protected const int TestPortRangeStart = 30000;
        protected const int TestPortRangeEnd = 35000;
        
        protected NetworkTestBase()
        {
            _testCancellationTokenSource = new CancellationTokenSource();
            _random = new Random();
        }

        /// <summary>
        /// 获取一个可用的测试端口
        /// </summary>
        protected int GetAvailablePort()
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                int port = _random.Next(TestPortRangeStart, TestPortRangeEnd);
                if (IsPortAvailable(port))
                {
                    return port;
                }
            }
            throw new InvalidOperationException("无法找到可用的测试端口");
        }

        /// <summary>
        /// 检查端口是否可用
        /// </summary>
        protected bool IsPortAvailable(int port)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 生成测试数据
        /// </summary>
        protected byte[] GenerateTestData(int length)
        {
            var data = new byte[length];
            _random.NextBytes(data);
            return data;
        }

        /// <summary>
        /// 等待指定条件成立或超时
        /// </summary>
        protected async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, TimeSpan? interval = null)
        {
            var actualInterval = interval ?? TimeSpan.FromMilliseconds(50);
            var endTime = DateTime.UtcNow.Add(timeout);
            
            while (DateTime.UtcNow < endTime)
            {
                if (condition())
                    return true;
                    
                await Task.Delay(actualInterval);
            }
            
            return false;
        }

        public virtual void Dispose()
        {
            _testCancellationTokenSource?.Cancel();
            _testCancellationTokenSource?.Dispose();
        }
    }
} 
