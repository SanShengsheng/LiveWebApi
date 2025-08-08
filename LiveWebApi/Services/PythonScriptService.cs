using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LiveWebApi.Services
{
    public class PythonScriptService
    {
        private readonly PythonSettings _pythonSettings;
        private readonly string _scriptFullPath;

        public PythonScriptService(IOptions<PythonSettings> pythonSettings)
        {
            _pythonSettings = pythonSettings.Value;

            // 计算脚本绝对路径（基于输出目录）
            _scriptFullPath = Path.Combine(AppContext.BaseDirectory, _pythonSettings.ScriptPath);
        }

        /// <summary>
        /// 执行Python脚本（传入liveId参数）
        /// </summary>
        public async Task<string> ExecuteScriptAsync(string liveId)
        {
            if (string.IsNullOrWhiteSpace(liveId))
                throw new ArgumentException("liveId不能为空", nameof(liveId));

            if (string.IsNullOrWhiteSpace(_pythonSettings.PythonPath))
                throw new InvalidOperationException("Python路径未配置");

            if (!File.Exists(_scriptFullPath))
                throw new FileNotFoundException("Python脚本文件不存在", _scriptFullPath);

            using (var process = new Process())
            {
                process.StartInfo.FileName = _pythonSettings.PythonPath;
                process.StartInfo.Arguments = $"\"{_scriptFullPath}\" \"{liveId}\"";

                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;

                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                string output = await outputTask;
                string error = await errorTask;

                if (process.ExitCode != 0)
                    throw new Exception($"脚本执行失败（ExitCode: {process.ExitCode}）\n错误信息: {error}\n输出日志: {output}");

                return $"脚本执行成功（liveId: {liveId}）\n输出日志: {output}";
            }
        }
    }
}