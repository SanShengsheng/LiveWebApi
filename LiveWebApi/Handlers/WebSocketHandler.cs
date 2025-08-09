using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace LiveWebApi.Handlers
{
    public class WebSocketHandler
    {
        // 线程安全的连接管理字典
        private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
        private readonly ILogger<WebSocketHandler> _logger;

        // 通过依赖注入获取日志服务
        public WebSocketHandler(ILogger<WebSocketHandler> logger)
        {
            _logger = logger;
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
                            await BroadcastMessageAsync(connectionId, message);
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
