#!/usr/bin/python
# coding:utf-8

import sys
from liveMan import DouyinLiveWebFetcher

if __name__ == '__main__':
    if len(sys.argv) < 2:
        sys.exit(1)
    live_id = sys.argv[1]  # 自动读取命令行参数，无需手动输入
    room = DouyinLiveWebFetcher(live_id)
    room.get_room_status()
    room.start()