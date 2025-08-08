# ws_notifier.py
import asyncio
import websockets
import threading

class WSNotifier:
    def __init__(self, uri="ws://localhost:8690/ws", identity="collection"):
        self.uri = uri
        self.identity = identity
        self.ws = None
        self.loop = None
        self._start_background_loop()

    def _start_background_loop(self):
        """启动后台线程和事件循环"""
        thread = threading.Thread(target=self._run_loop, daemon=True)
        thread.start()

    def _run_loop(self):
        """线程内部运行事件循环和 WebSocket 客户端"""
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        self.loop.run_until_complete(self._ws_client())

    async def _ws_client(self):
        """连接 WebSocket 并维持连接"""
        try:
            self.ws = await websockets.connect(self.uri)
            await self.ws.send(self.identity)
            print(f"[WSNotifier] Connected as {self.identity}")
            while True:
                await asyncio.sleep(1)
        except Exception as e:
            print(f"[WSNotifier] Connection error: {e}")

    def send(self, msg):
        """同步接口，发送消息"""
        if not self.loop or not self.ws:
            print("[WSNotifier] Not connected yet.")
            return

        future = asyncio.run_coroutine_threadsafe(self._send_msg(msg), self.loop)
        try:
            future.result(timeout=3)
        except Exception as e:
            print(f"[WSNotifier] Send failed: {e}")

    async def _send_msg(self, msg):
        """异步消息发送"""
        if self.ws:
            await self.ws.send(msg)

# 全局实例，方便导入后直接使用
notifier = WSNotifier()
