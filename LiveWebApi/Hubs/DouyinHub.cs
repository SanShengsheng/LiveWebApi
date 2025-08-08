using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace LiveWebApi.Hubs
{
    public class DouyinHub : Hub
    {
        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            await Clients.Caller.SendAsync("ReceiveMessage", "系统通知", $"已加入直播间: {groupName}");
        }

        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            await Clients.Caller.SendAsync("ReceiveMessage", "系统通知", $"已离开直播间: {groupName}");
        }
    }
}
