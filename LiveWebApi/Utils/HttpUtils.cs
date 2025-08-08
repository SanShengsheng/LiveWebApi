using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jint;
using Jint.Native; // 引入JsValue所在命名空间
namespace LiveWebApi.Utils
{
    public class HttpUtils
    {
        private readonly HttpClient _httpClient;

        public HttpUtils()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            );
        }

        public async Task<string> GetTtwid(string liveBaseUrl = "https://live.douyin.com/")
        {
            var response = await _httpClient.GetAsync(liveBaseUrl);
            response.EnsureSuccessStatusCode();

            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    if (cookie.StartsWith("ttwid="))
                    {
                        return cookie.Split(';')[0].Split('=')[1];
                    }
                }
            }
            throw new Exception("获取 ttwid 失败");
        }

        public string GenerateMsToken(int length = 107)
        {
            var baseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789=_";
            var random = new Random();
            var sb = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                sb.Append(baseChars[random.Next(baseChars.Length)]);
            }
            return sb.ToString();
        }

        public string GenerateSignature(string wssUrl, string signJsPath = "sign.js")
        {
            // 解析 WSS URL 参数（不依赖 System.Web 的写法）
            var parsedUrl = new Uri(wssUrl);
            var queryParams = new Dictionary<string, string>();
            var queryParts = parsedUrl.Query.TrimStart('?').Split('&');
            foreach (var part in queryParts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                var keyValue = part.Split('=');
                var key = keyValue[0];
                var value = keyValue.Length > 1 ? keyValue[1] : "";
                queryParams[key] = value;
            }

            var targetParams = new List<string>
{
"live_id", "aid", "version_code", "webcast_sdk_version",
"room_id", "sub_room_id", "sub_channel_id", "did_rule",
"user_unique_id", "device_platform", "device_type", "ac", "identity"
};
            var paramList = new List<string>();
            foreach (var param in targetParams)
            {
                queryParams.TryGetValue(param, out var value);
                paramList.Add($"{param}={value ?? ""}");
            }
            var paramStr = string.Join(",", paramList);

            // 计算 MD5
            using (var md5 = MD5.Create())
            {
                var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(paramStr));
                var md5Param = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

                // 执行 JS 函数并处理返回值
                var jsCode = File.ReadAllText(signJsPath);
                var engine = new Engine();
                engine.Execute(jsCode);

                // 调用 JS 函数，获取 JsValue
                JsValue jsResult = engine.Invoke("get_sign", md5Param);

                // 转换为 string（核心修复）
                if (jsResult.IsString())
                {
                    return jsResult.AsString();
                }
                else
                {
                    throw new Exception($"sign.js 返回类型错误，预期字符串，实际为：{jsResult.Type}");
                }
            }
        }
    }
}