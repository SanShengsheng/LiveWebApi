#!/usr/bin/python
# coding:utf-8

import sys
import signal
import time
from liveMan import DouyinLiveWebFetcher
print(f"Python 解释器路径: {sys.executable}")

# 全局变量存储直播间实例
room = None

def signal_handler(sig, frame):
    """处理终止信号"""
    print(f"收到信号 {sig}，正在停止...")
    if room:
        room.stop()
    time.sleep(1)  # 给清理操作一点时间
    sys.exit(0)

# 注册信号处理函数
signal.signal(signal.SIGINT, signal_handler)  # 处理Ctrl+C
signal.signal(signal.SIGTERM, signal_handler)  # 处理终止信号

def main():
    global room
    if len(sys.argv) < 2:
        sys.exit(1)
    live_id = sys.argv[1]  # 自动读取命令行参数，无需手动输入
    room = DouyinLiveWebFetcher(live_id)
    room.get_room_status()
    room.start()

if __name__ == '__main__':
    main()