using Douyin;
// 引用Protobuf类的命名空间（必须与Douyin.cs中的命名空间一致）
using LiveWebApi.Protobuf;
using LiveWebApi.Utils;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiveWebApi.Services
{
    public class DouyinWebSocketService
    {
        private readonly HttpUtils _httpUtils;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private string _roomId;
        private string _ttwid;
        private bool _isConnected;

        // 事件：弹幕消息（使用Protobuf生成的类）
        public event Action<WebcastChatMessage> OnChatMessageReceived;
        // 事件：礼物消息（使用Protobuf生成的类）
        public event Action<WebcastGiftMessage> OnGiftMessageReceived;

        public bool IsConnected => _isConnected;

        public DouyinWebSocketService(HttpUtils httpUtils)
        {
            _httpUtils = httpUtils;
            _webSocket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// 连接到抖音直播WebSocket服务器
        /// </summary>
        public async Task Connect(string roomId, string ttwid)
        {
            if (_isConnected)
                throw new InvalidOperationException("已处于连接状态，无需重复连接");

            _roomId = roomId;
            _ttwid = ttwid;

            try
            {
                // 构造WebSocket URL（参考Python项目的wss地址，需验证）
                var timestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                var wssUrl = $"wss://webcast5-ws-web-hl.douyin.com/webcast/im/push/v2/?room_id={_roomId}&aid=6383&version_code=190500&webcast_sdk_version=1.3.0&live_id=1&device_platform=web&device_type=windows&ac=wifi&identity=audience&timestamp={timestamp}&sign=";

                // 生成签名
                var signature = _httpUtils.GenerateSignature(wssUrl);
                wssUrl += signature;

                // 设置请求头
                _webSocket.Options.SetRequestHeader("Cookie", $"ttwid={_ttwid}");
                _webSocket.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                // 建立连接
                await _webSocket.ConnectAsync(new Uri(wssUrl), _cts.Token);
                _isConnected = true;
                Console.WriteLine("WebSocket连接成功");

                // 启动消息接收和心跳
                _ = Task.Run(ReceiveLoop);
                _ = Task.Run(HeartbeatLoop);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket连接失败：{ex.Message}");
                _isConnected = false;
                throw;
            }
        }

        /// <summary>
        /// 接收消息循环
        /// </summary>
        private async Task ReceiveLoop()
        {
            var buffer = new byte[1024 * 4];
            while (_isConnected && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await Close();
                        break;
                    }

                    // 提取有效消息字节
                    var messageBytes = new byte[result.Count];
                    Array.Copy(buffer, messageBytes, result.Count);

                    // 解析消息
                    await ParseMessage(messageBytes);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"消息接收失败：{ex.Message}");
                    await Task.Delay(3000);
                    await Reconnect();
                }
            }
        }

        /// <summary>
        /// 解析Protobuf消息（核心）
        /// </summary>
        private async Task ParseMessage(byte[] messageBytes)
        {
            try
            {
                // 解压Gzip
                byte[] decompressedBytes;
                using (var ms = new MemoryStream(messageBytes))
                using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
                using (var outputMs = new MemoryStream())
                {
                    await gzip.CopyToAsync(outputMs);
                    decompressedBytes = outputMs.ToArray();
                }

                // 解析根消息（RootMessage对应.proto中的根结构，需与定义一致）
                // 注意：如果.proto中根消息名不是RootMessage，需替换为实际名称（如PushFrame）
                var rootMessage = RootMessage.Parser.ParseFrom(decompressedBytes);

                // 处理不同类型的消息
                foreach (var msg in rootMessage.MessagesList)
                {
                    switch (msg.Method)
                    {
                        case "WebcastChatMessage":
                            // 解析弹幕消息（需与.proto中的消息名一致）
                            var chatMsg = WebcastChatMessage.Parser.ParseFrom(msg.Payload);
                            OnChatMessageReceived?.Invoke(chatMsg);
                            break;
                        case "WebcastGiftMessage":
                            // 解析礼物消息（需与.proto中的消息名一致）
                            var giftMsg = WebcastGiftMessage.Parser.ParseFrom(msg.Payload);
                            OnGiftMessageReceived?.Invoke(giftMsg);
                            break;
                        // 其他消息类型（如点赞、关注等）
                        default:
                            Console.WriteLine($"未处理的消息类型：{msg.Method}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"消息解析失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 心跳包循环
        /// </summary>
        private async Task HeartbeatLoop()
        {
            while (_isConnected && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // 抖音WebSocket心跳包格式（参考Python项目的心跳包内容）
                    var heartbeat = new byte[] { 0x00, 0x01, 0x00, 0x00 }; // 示例，需验证
                    await _webSocket.SendAsync(new ArraySegment<byte>(heartbeat), WebSocketMessageType.Binary, true, _cts.Token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"心跳发送失败：{ex.Message}");
                }
                await Task.Delay(5000); // 每5秒一次
            }
        }

        /// <summary>
        /// 重连机制
        /// </summary>
        private async Task Reconnect()
        {
            if (!_isConnected) return;
            Console.WriteLine("尝试重连...");
            await Close();
            _webSocket = new ClientWebSocket();
            await Connect(_roomId, _ttwid);
        }

        /// <summary>
        /// 关闭连接
        /// </summary>
        public async Task Close()
        {
            if (!_isConnected) return;

            try
            {
                _isConnected = false;
                _cts.CancelAfter(1000);
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "关闭", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"关闭失败：{ex.Message}");
            }
            finally
            {
                _webSocket.Dispose();
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }
        }
    }
}
