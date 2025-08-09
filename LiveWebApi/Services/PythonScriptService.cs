using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LiveWebApi.Services
{
    // 用于存储Python脚本配置（需在appsettings.json中配置）
    public class PythonSettings
    {
        public string PythonPath { get; set; } = null!; // Python解释器路径（如：python.exe或/usr/bin/python3）
        public string ScriptPath { get; set; } = null!; // 脚本相对路径（相对于项目输出目录）
    }

    public class PythonScriptService
    {
        private readonly PythonSettings _pythonSettings;
        private readonly string _scriptFullPath;
        // 线程安全集合：跟踪正在运行的liveId及对应进程（键：liveId，值：进程实例）
        private readonly ConcurrentDictionary<string, Process> _activeProcesses = new ConcurrentDictionary<string, Process>();

        public PythonScriptService(IOptions<PythonSettings> pythonSettings)
        {
            _pythonSettings = pythonSettings.Value ?? throw new ArgumentNullException(nameof(pythonSettings));

            // 计算脚本绝对路径（基于项目输出目录）
            _scriptFullPath = Path.Combine(AppContext.BaseDirectory, _pythonSettings.ScriptPath);

            // 初始化检查
            if (string.IsNullOrWhiteSpace(_pythonSettings.PythonPath))
                throw new InvalidOperationException("Python路径未在配置文件中设置（PythonSettings:PythonPath）");
            if (!File.Exists(_scriptFullPath))
                throw new FileNotFoundException("Python脚本文件不存在", _scriptFullPath);
        }

        /// <summary>
        /// 停止Python脚本（异步非阻塞）
        /// </summary>
        public Task<string> StopScriptAsync(string liveId)
        {
            if (string.IsNullOrWhiteSpace(liveId))
                throw new ArgumentException("liveId不能为空", nameof(liveId));

            // 检查是否在运行
            if (!_activeProcesses.TryGetValue(liveId, out var process))
            {
                return Task.FromResult($"直播间 {liveId} 未在监听中，无需停止");
            }

            try
            {
                // 尝试优雅关闭进程
                if (!process.HasExited)
                {
                    try
                    {
                        // 先尝试优雅关闭（发送SIGTERM信号）
                        process.CloseMainWindow();
                        if (!process.WaitForExit(1000)) // 等待1秒
                        {
                            // 优雅关闭失败，强制终止
                            process.Kill();
                            process.WaitForExit(2000); // 等待进程退出，最多等待2秒
                        }
                    }
                    catch
                    {
                        // 关闭主窗口失败，直接强制终止
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                }
                
                // 从活跃列表移除
                _activeProcesses.TryRemove(liveId, out _);
                process.Dispose();
                return Task.FromResult($"直播间 {liveId} 监听任务已停止");
            }
            catch (Exception ex)
            {
                // 清理状态
                _activeProcesses.TryRemove(liveId, out _);
                process?.Dispose();
                throw new Exception($"停止监听任务失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 启动Python脚本（同一liveId不重复启动，异步非阻塞）
        /// </summary>
        public Task<string> ExecuteScriptAsync(string liveId)
        {
            if (string.IsNullOrWhiteSpace(liveId))
                throw new ArgumentException("liveId不能为空", nameof(liveId));

            // 1. 检查是否已在运行
            if (_activeProcesses.ContainsKey(liveId))
            {
                return Task.FromResult($"直播间 {liveId} 已在监听中，无需重复启动");
            }

            // 2. 尝试添加到活跃列表（防止并发重复启动）
            var process = new Process();
            if (!_activeProcesses.TryAdd(liveId, process))
            {
                return Task.FromResult($"直播间 {liveId} 已在监听中，无需重复启动");
            }

            // 3. 配置进程信息
            process.StartInfo.FileName = _pythonSettings.PythonPath;
            process.StartInfo.Arguments = $"\"{_scriptFullPath}\" \"{liveId}\""; // 传入liveId参数

            // 启用输出重定向以便捕获Python输出
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true; // 重定向输出
            process.StartInfo.RedirectStandardError = true;  // 重定向错误

            // 注册输出接收事件
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Console.WriteLine($"[Python Output] {e.Data}");
                }
            };

            // 注册错误接收事件
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Console.WriteLine($"[Python Error] {e.Data}");
                }
            };


            // 4. 注册进程退出事件（清理活跃列表）
            process.EnableRaisingEvents = true;
            process.Exited += (sender, e) =>
            {
                // 进程退出后从活跃列表移除
                _activeProcesses.TryRemove(liveId, out _);
                process.Dispose(); // 释放进程资源
            };

            try
            {
                // 5. 启动进程（不等待完成，立即返回）
                process.Start();
                // 开始异步读取输出
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return Task.FromResult($"直播间 {liveId} 监听任务已启动");
            }
            catch (Exception ex)
            {
                // 启动失败时清理状态
                _activeProcesses.TryRemove(liveId, out _);
                process.Dispose();
                throw new Exception($"启动监听任务失败：{ex.Message}");
            }
        }
    }
}