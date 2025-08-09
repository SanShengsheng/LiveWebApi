using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LiveWebApi.Handlers
{
    public class WebSocketHandler
    {
        // 线程安全的连接管理字典
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        // 客户端订阅主题字典
        private readonly ConcurrentDictionary<string, List<string>> _clientSubscriptions = new();
        private readonly ILogger<WebSocketHandler> _logger;

        // 通过依赖注入获取日志服务
        public WebSocketHandler(ILogger<WebSocketHandler> logger)
        {
            _logger = logger;
        }

        // 订阅主题
        public void SubscribeToTopic(string clientId, string topic)
        {
            if (!_clientSubscriptions.ContainsKey(clientId))
            {
                _clientSubscriptions.TryAdd(clientId, new List<string>());
            }
            if (!_clientSubscriptions[clientId].Contains(topic))
            {
                _clientSubscriptions[clientId].Add(topic);
                _logger.LogInformation($"客户端 {clientId} 已订阅主题 {topic}");
            }
        }

        // 取消订阅主题
        public void UnsubscribeFromTopic(string clientId, string topic)
        {
            if (_clientSubscriptions.ContainsKey(clientId))
            {
                _clientSubscriptions[clientId].Remove(topic);
                _logger.LogInformation($"客户端 {clientId} 已取消订阅主题 {topic}");
            }
        }

        // 向特定主题的所有订阅者发送消息
        public async Task SendMessageToTopicAsync(string topic, string message, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"向主题 {topic} 的所有订阅者发送消息");
            foreach (var (clientId, topics) in _clientSubscriptions)
            {
                if (topics.Contains(topic) && _connections.TryGetValue(clientId, out var client) && client.State == WebSocketState.Open)
                {
                    await SendMessageToClientAsync(clientId, message, cancellationToken);
                }
            }
        }

        // WebSocket连接处理主方法
        public async Task HandleWebSocketAsync(HttpContext context, CancellationToken cancellationToken)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // 接受WebSocket连接（使用传入的 cancellationToken）
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var connectionId = Guid.NewGuid().ToString();
            _connections.TryAdd(connectionId, webSocket);

            _logger.LogInformation($"新连接: {connectionId}，当前连接数: {_connections.Count}");

            try
            {
                var buffer = new byte[1024 * 4];
                WebSocketReceiveResult result;

                // 持续接收消息（使用 cancellationToken 取消操作）
                do
                {
                    result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken // 传入取消令牌
                    );

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _logger.LogInformation($"收到消息 [{connectionId}]: {message}");
                        
                        // 处理心跳消息
                        if (message == "heartbeat")
                        {
                            // 回复心跳确认
                            var response = Encoding.UTF8.GetBytes("heartbeat_ack");
                            await webSocket.SendAsync(
                                new ArraySegment<byte>(response),
                                WebSocketMessageType.Text,
                                true,
                                cancellationToken);
                        }
                        else
                        {
                            try
                            {
                                // 检查是否可能为JSON格式
                                if (message.TrimStart().StartsWith("{") && message.TrimEnd().EndsWith("}"))
                                {
                                    // 尝试解析JSON消息
                                    using var doc = JsonDocument.Parse(message);
                                    var root = doc.RootElement;

                                    // 检查是否包含消息类型
                                    if (root.TryGetProperty("type", out var typeProp))
                                    {
                                        string messageType = typeProp.GetString();

                                        // 处理订阅请求
                                        if (messageType == "subscribe" && root.TryGetProperty("topic", out var topicProp))
                                        {
                                            string topic = topicProp.GetString();
                                            SubscribeToTopic(connectionId, topic);
                                            await SendMessageToClientAsync(connectionId, $"{{\"status\":\"success\",\"message\":\"已订阅主题 {topic}\"}}", cancellationToken);
                                        }
                                        // 处理取消订阅请求
                                        else if (messageType == "unsubscribe" && root.TryGetProperty("topic", out var unsubTopicProp))
                                        {
                                            string topic = unsubTopicProp.GetString();
                                            UnsubscribeFromTopic(connectionId, topic);
                                            await SendMessageToClientAsync(connectionId, $"{{\"status\":\"success\",\"message\":\"已取消订阅主题 {topic}\"}}", cancellationToken);
                                        }
                                        // 处理点对点消息
                                        else if (messageType == "direct" && root.TryGetProperty("targetId", out var targetIdProp) && root.TryGetProperty("content", out var contentProp))
                                        {
                                            string targetId = targetIdProp.GetString();
                                            string content = contentProp.GetString();
                                            await SendMessageToClientAsync(targetId, $"{{\"type\":\"direct\",\"fromId\":\"{connectionId}\",\"content\":\"{content}\"}}", cancellationToken);
                                            await SendMessageToClientAsync(connectionId, $"{{\"status\":\"success\",\"message\":\"消息已发送给客户端 {targetId}\"}}", cancellationToken);
                                        }
                                        // 处理主题消息
                                        else if (messageType == "topic" && root.TryGetProperty("topic", out var pubTopicProp) && root.TryGetProperty("content", out var pubContentProp))
                                        {
                                            string topic = pubTopicProp.GetString();
                                            // 由于content是对象，使用ToString()获取其JSON表示
                                            string content = pubContentProp.ToString();
                                            await SendMessageToTopicAsync(topic, $"{{\"type\":\"topic\",\"fromId\":\"{connectionId}\",\"topic\":\"{topic}\",\"content\":{content}}}", cancellationToken);
                                            await SendMessageToClientAsync(connectionId, $"{{\"status\":\"success\",\"message\":\"消息已发送到主题 {topic}\"}}", cancellationToken);
                                        }
                                        else
                                        {
                                            _logger.LogWarning($"未知消息类型: {messageType}");
                                            await SendMessageToClientAsync(connectionId, $"{{\"status\":\"error\",\"message\":\"未知消息类型\"}}", cancellationToken);
                                        }
                                    }
                                    else
                                    {
                                        // 兼容旧版消息格式，直接广播
                                        _logger.LogInformation($"不包含消息类型，使用旧版广播模式");
                                        await BroadcastMessageAsync(connectionId, message, cancellationToken);
                                    }
                                }
                                else
                                {
                                    // 非JSON格式消息，直接广播
                                    _logger.LogInformation($"非JSON格式消息，使用旧版广播模式");
                                    await BroadcastMessageAsync(connectionId, message, cancellationToken);
                                }
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogError(ex, $"解析消息失败: {message}");
                                // 发送错误响应给客户端
                                await SendMessageToClientAsync(connectionId, $"{{\"status\":\"error\",\"message\":\"消息格式无效\"}}", cancellationToken);
                            }
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "正常关闭",
                            cancellationToken
                        );
                    }

                } while (!result.CloseStatus.HasValue && !cancellationToken.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"连接已取消: {connectionId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"连接错误 [{connectionId}]");
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
                _logger.LogInformation($"连接关闭: {connectionId}，当前连接数: {_connections.Count}");
            }
        }

        // 广播消息到所有连接
        public async Task BroadcastMessageAsync(string senderId, string message, CancellationToken cancellationToken = default)
        {
            var broadcastMessage = Encoding.UTF8.GetBytes($"[{senderId}]: {message}");
            foreach (var (id, client) in _connections)
            {
                if (id != senderId && client.State == WebSocketState.Open)
                {
                    await client.SendAsync(
                        new ArraySegment<byte>(broadcastMessage),
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken);
                }
            }
        }

        // 获取当前连接数
        public int GetConnectionCount() => _connections.Count;

        // 向特定客户端发送消息
        public async Task SendMessageToClientAsync(string clientId, string message, CancellationToken cancellationToken = default)
        {
            if (_connections.TryGetValue(clientId, out var client) && client.State == WebSocketState.Open)
            {
                var data = Encoding.UTF8.GetBytes(message);
                await client.SendAsync(
                    new ArraySegment<byte>(data),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);
                _logger.LogInformation($"已向客户端 {clientId} 发送消息");
            }
            else
            {
                _logger.LogWarning($"客户端 {clientId} 不存在或连接已关闭");
            }
        }

        // 关闭所有连接
        public async Task CloseAllConnectionsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("正在关闭所有WebSocket连接...");
            var connections = _connections.ToList();
            foreach (var (id, client) in connections)
            {
                if (client.State == WebSocketState.Open)
                {
                    try
                    {
                        await client.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "服务器重启",
                            cancellationToken
                        );
                        _logger.LogInformation($"已关闭连接: {id}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"关闭连接 {id} 时出错");
                    }
                }
                _connections.TryRemove(id, out _);
            }
            _logger.LogInformation($"所有连接已关闭，剩余连接数: {_connections.Count}");
        }
    }
}
