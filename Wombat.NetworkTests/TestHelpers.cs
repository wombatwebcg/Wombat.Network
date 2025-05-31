using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Wombat.NetworkTests
{
    /// <summary>
    /// 测试辅助类，提供常用的测试支持功能
    /// </summary>
    public static class TestHelpers
    {
        private static readonly Random _random = new Random();
        
        /// <summary>
        /// 获取一个可用的随机端口号（通常在动态/私有端口范围内）
        /// </summary>
        /// <returns>可用的端口号</returns>
        public static int GetAvailablePort()
        {
            // 使用动态端口范围 (49152-65535)
            return _random.Next(49152, 65535);
        }
        
        /// <summary>
        /// 创建控制台日志记录器
        /// </summary>
        /// <typeparam name="T">日志类别</typeparam>
        /// <returns>日志记录器实例</returns>
        public static ILogger CreateLogger<T>()
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            
            return loggerFactory.CreateLogger<T>();
        }
        
        /// <summary>
        /// 等待直到满足条件或超时
        /// </summary>
        /// <param name="predicate">要检查的条件</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <param name="pollInterval">轮询间隔（毫秒）</param>
        /// <returns>如果条件在超时前满足返回true，否则返回false</returns>
        public static async Task<bool> WaitUntilAsync(Func<bool> predicate, int timeout = 5000, int pollInterval = 100)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            while (sw.ElapsedMilliseconds < timeout)
            {
                if (predicate())
                {
                    return true;
                }
                
                await Task.Delay(pollInterval);
            }
            
            return false;
        }
        
        /// <summary>
        /// 运行带超时的任务
        /// </summary>
        /// <typeparam name="T">任务结果类型</typeparam>
        /// <param name="task">要执行的任务</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <returns>任务结果</returns>
        /// <exception cref="TimeoutException">如果任务超时</exception>
        public static async Task<T> RunWithTimeoutAsync<T>(Task<T> task, int timeout = 5000)
        {
            using var cts = new CancellationTokenSource();
            var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cts.Token));
            
            if (completedTask == task)
            {
                cts.Cancel();
                return await task;
            }
            
            throw new TimeoutException($"Operation timed out after {timeout}ms");
        }
        
        /// <summary>
        /// 运行带超时的任务
        /// </summary>
        /// <param name="task">要执行的任务</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        /// <exception cref="TimeoutException">如果任务超时</exception>
        public static async Task RunWithTimeoutAsync(Task task, int timeout = 5000)
        {
            using var cts = new CancellationTokenSource();
            var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cts.Token));
            
            if (completedTask == task)
            {
                cts.Cancel();
                await task;
                return;
            }
            
            throw new TimeoutException($"Operation timed out after {timeout}ms");
        }
    }
} 