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
        public string PythonPath { get; set; } // Python解释器路径（如：python.exe或/usr/bin/python3）
        public string ScriptPath { get; set; } // 脚本相对路径（相对于项目输出目录）
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

            // 核心：关闭输出重定向（解决乱码，且无需处理输出）
            //process.StartInfo.UseShellExecute = false;
            //process.StartInfo.CreateNoWindow = true;
            //process.StartInfo.RedirectStandardOutput = false; // 不重定向输出
            //process.StartInfo.RedirectStandardError = false;  // 不重定向错误

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