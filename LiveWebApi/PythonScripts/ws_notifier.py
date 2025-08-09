# ws_notifier.py
import asyncio
import websockets
import threading
import time
import atexit

class WSNotifier:
    _instance = None
    _lock = threading.Lock()

    def __new__(cls, *args, **kwargs):
        with cls._lock:
            if cls._instance is None:
                cls._instance = super(WSNotifier, cls).__new__(cls)
            return cls._instance

    def __init__(self, uri="ws://localhost:8690/ws", identity="collection"):
        # 防止重复初始化
        if hasattr(self, '_initialized') and self._initialized:
            return
        self._initialized = True
        self.uri = uri
        self.identity = identity
        self.ws = None
        self.loop = None
        self._reconnect_interval = 5  # 重连间隔(秒)
        self._connected = False
        self._reconnecting = False
        self._start_background_loop()
        print(f"[WSNotifier] Instance created with identity: {self.identity}")

    def _start_background_loop(self):
        thread = threading.Thread(target=self._run_loop, daemon=True)
        thread.start()

    def _run_loop(self):
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        self.loop.run_until_complete(self._ws_client())

    async def _ws_client(self):
        while True:
            if self._reconnecting:
                await asyncio.sleep(0.1)
                continue

            try:
                print(f"[WSNotifier] Trying to connect to {self.uri}...")
                self.ws = await websockets.connect(
                    self.uri,
                    ping_interval=10,
                    ping_timeout=20,
                    close_timeout=5
                )
                self._connected = True
                await self.ws.send(self.identity)
                print(f"[WSNotifier] Connected as {self.identity} successfully")
                # 启动心跳任务
                heartbeat_task = asyncio.create_task(self._heartbeat())
                # 保持连接
                await self._keep_alive()
                # 如果连接正常关闭，取消心跳任务
                heartbeat_task.cancel()
            except Exception as e:
                self._connected = False
                print(f"[WSNotifier] Connection error: {e}")
                # 等待重连
                self._reconnecting = True
                await asyncio.sleep(self._reconnect_interval)
                self._reconnecting = False

    async def _heartbeat(self):
        """发送心跳包"""
        while self._connected:
            try:
                await self.ws.send("heartbeat")
                await asyncio.sleep(30)  # 每30秒发送一次心跳
            except Exception as e:
                print(f"[WSNotifier] Heartbeat error: {e}")
                break

    async def _keep_alive(self):
        """保持连接，监听消息"""
        while self._connected:
            try:
                # 监听服务器消息
                message = await asyncio.wait_for(self.ws.recv(), timeout=60)  # 60秒超时
                print(f"[WSNotifier] Received message: {message}")
                # 处理服务器心跳响应
                if message == "heartbeat_ack":
                    print(f"[WSNotifier] Received heartbeat acknowledgment")
            except asyncio.TimeoutError:
                # 超时不处理，继续等待
                print(f"[WSNotifier] Keep alive timeout, continuing...")
                pass
            except Exception as e:
                print(f"[WSNotifier] Keep alive error: {type(e).__name__} - {e}")
                break

    def send(self, msg):
        if not self.loop or not self._connected or not self.ws:
            print("[WSNotifier] Not connected yet.")
            return

        future = asyncio.run_coroutine_threadsafe(self._send_msg(msg), self.loop)
        try:
            future.result(timeout=3)
        except Exception as e:
            print(f"[WSNotifier] Send failed: {e}")

    async def _send_msg(self, msg):
        if self.ws and self._connected:
            await self.ws.send(msg)

    async def _close_connection(self):
        """关闭WebSocket连接"""
        if self.ws and self._connected:
            try:
                await self.ws.close()
                self._connected = False
                print("[WSNotifier] Connection closed gracefully")
            except Exception as e:
                print(f"[WSNotifier] Error closing connection: {e}")

    def close(self):
        """公共关闭方法，供外部调用"""
        if self.loop and not self.loop.is_closed():
            asyncio.run_coroutine_threadsafe(self._close_connection(), self.loop)
            # 等待连接关闭
            time.sleep(1)

# 注册退出处理函数
def cleanup():
    if 'notifier' in globals():
        notifier.close()
        print("[WSNotifier] Cleanup completed on exit")

atexit.register(cleanup)

# 创建单例实例
notifier = WSNotifier()
print("[WSNotifier] Global notifier instance created")