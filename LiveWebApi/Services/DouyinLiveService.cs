using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LiveWebApi.Utils;
using Newtonsoft.Json;

namespace LiveWebApi.Services
{
    /// <summary>
    /// 抖音直播服务类：处理直播间信息获取、状态查询等核心逻辑
    /// </summary>
    public class DouyinLiveService
    {
        private readonly HttpUtils _httpUtils;
        private string _ttwid; // 抖音会话标识
        private string _roomId; // 直播间真实ID（长ID）
        private readonly HttpClient _httpClient; // 复用HttpClient提升性能

        /// <summary>
        /// 构造函数：注入工具类
        /// </summary>
        public DouyinLiveService(HttpUtils httpUtils)
        {
            _httpUtils = httpUtils;
            _httpClient = new HttpClient();
            // 配置默认请求头（模拟浏览器）
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            );
        }

        /// <summary>
        /// 初始化直播间信息：获取ttwid和room_id
        /// </summary>
        /// <param name="liveId">直播间短ID（如直播链接中的78888888）</param>
        public async Task Init(string liveId)
        {
            if (string.IsNullOrEmpty(liveId))
                throw new ArgumentNullException(nameof(liveId), "直播间短ID不能为空");

            // 1. 获取ttwid（若未获取过）
            if (string.IsNullOrEmpty(_ttwid))
            {
                _ttwid = await _httpUtils.GetTtwid();
                if (string.IsNullOrEmpty(_ttwid))
                    throw new Exception("初始化失败：无法获取有效的ttwid");
            }

            // 2. 获取room_id（若未获取过）
            if (string.IsNullOrEmpty(_roomId))
            {
                _roomId = await GetRoomId(liveId);
                if (string.IsNullOrEmpty(_roomId))
                    throw new Exception("初始化失败：无法解析有效的room_id");
            }
        }

        /// <summary>
        /// 从直播页面HTML中提取room_id（长ID）
        /// </summary>
        private async Task<string> GetRoomId(string liveId)
        {
            var liveUrl = $"https://live.douyin.com/{liveId}";

            // 构造请求头（包含必要Cookie和标识）
            _httpClient.DefaultRequestHeaders.Remove("Cookie");
            _httpClient.DefaultRequestHeaders.Add("Cookie",
                $"ttwid={_ttwid}; msToken={_httpUtils.GenerateMsToken()};");
            _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.douyin.com/");

            try
            {
                // 发起请求获取直播页面HTML
                var response = await _httpClient.GetAsync(liveUrl);
                response.EnsureSuccessStatusCode();
                var htmlContent = await response.Content.ReadAsStringAsync();

                // 正则表达式提取roomId（适配抖音页面JS变量格式）
                var regex = new Regex(@"roomId\s*:\s*""(\d+)""|roomId\\"":\\""(\d+)""");
                var match = regex.Match(htmlContent);

                // 检查所有捕获组，找到有效匹配
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    if (!string.IsNullOrEmpty(match.Groups[i].Value))
                    {
                        return match.Groups[i].Value;
                    }
                }

                // 若未匹配到，打印部分HTML用于调试
                var debugHtml = htmlContent.Length > 500 ? htmlContent.Substring(0, 500) : htmlContent;
                throw new Exception($"未找到roomId，页面片段：{debugHtml}");
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"获取直播页面失败：{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取直播间状态（是否开播、主播信息等）
        /// </summary>
        /// <param name="liveId">直播间短ID</param>
        /// <returns>直播间状态DTO</returns>
        public async Task<RoomStatusDto> GetRoomStatus(string liveId)
        {
            // 确保已初始化
            await Init(liveId);

            // 抖音直播间状态API
            var apiUrl = $"https://live.douyin.com/webcast/room/web/enter/?aid=6383&web_rid={liveId}&room_id_str={_roomId}";

            try
            {
                // 配置请求头
                _httpClient.DefaultRequestHeaders.Remove("Cookie");
                _httpClient.DefaultRequestHeaders.Add("Cookie", $"ttwid={_ttwid}");

                var response = await _httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();
                var jsonContent = await response.Content.ReadAsStringAsync();

                // 解析JSON响应
                var apiResponse = JsonConvert.DeserializeObject<RoomStatusApiResponse>(jsonContent);
                if (apiResponse?.Data?.Room == null)
                    throw new Exception("API返回数据格式异常，未找到直播间信息");

                // 转换为DTO返回
                return new RoomStatusDto
                {
                    LiveId = liveId,
                    RoomId = _roomId,
                    IsLive = apiResponse.Data.Room.Status == 2, // 2表示直播中
                    RoomTitle = apiResponse.Data.Room.Title,
                    AnchorName = apiResponse.Data.Room.Owner?.Nickname,
                    AnchorId = apiResponse.Data.Room.Owner?.UserId,
                    OnlineCount = apiResponse.Data.Room.UserCount,
                    LiveCover = apiResponse.Data.Room.Cover?.UrlList?[0]
                };
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"查询直播间状态失败：{ex.Message}", ex);
            }
            catch (JsonException ex)
            {
                throw new Exception($"解析直播间状态失败：{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 重置状态（用于切换直播间）
        /// </summary>
        public void Reset()
        {
            _roomId = null;
            // 保留ttwid避免重复获取（可复用）
        }
    }

    #region 响应模型类（映射抖音API返回结构）
    /// <summary>
    /// 抖音直播间状态API完整响应
    /// </summary>
    internal class RoomStatusApiResponse
    {
        [JsonProperty("data")]
        public RoomStatusData Data { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("status_code")]
        public int StatusCode { get; set; }
    }

    internal class RoomStatusData
    {
        [JsonProperty("room")]
        public RoomInfo Room { get; set; }
    }

    internal class RoomInfo
    {
        [JsonProperty("status")]
        public int Status { get; set; } // 2=直播中，其他=未开播

        [JsonProperty("title")]
        public string Title { get; set; } // 直播间标题

        [JsonProperty("owner")]
        public AnchorInfo Owner { get; set; } // 主播信息

        [JsonProperty("user_count")]
        public long UserCount { get; set; } // 在线人数

        [JsonProperty("cover")]
        public CoverInfo Cover { get; set; } // 封面图
    }

    internal class AnchorInfo
    {
        [JsonProperty("nickname")]
        public string Nickname { get; set; } // 主播昵称

        [JsonProperty("user_id")]
        public string UserId { get; set; } // 主播ID
    }

    internal class CoverInfo
    {
        [JsonProperty("url_list")]
        public List<string> UrlList { get; set; } // 封面图URL列表
    }
    #endregion

    /// <summary>
    /// 直播间状态数据传输对象（对外暴露的简化信息）
    /// </summary>
    public class RoomStatusDto
    {
        /// <summary>
        /// 直播间短ID（用户输入的ID）
        /// </summary>
        public string LiveId { get; set; }

        /// <summary>
        /// 直播间真实ID（长ID）
        /// </summary>
        public string RoomId { get; set; }

        /// <summary>
        /// 是否正在直播
        /// </summary>
        public bool IsLive { get; set; }

        /// <summary>
        /// 直播间标题
        /// </summary>
        public string RoomTitle { get; set; }

        /// <summary>
        /// 主播昵称
        /// </summary>
        public string AnchorName { get; set; }

        /// <summary>
        /// 主播ID
        /// </summary>
        public string AnchorId { get; set; }

        /// <summary>
        /// 在线人数
        /// </summary>
        public long OnlineCount { get; set; }

        /// <summary>
        /// 直播封面图URL
        /// </summary>
        public string LiveCover { get; set; }
    }
}
