using Microsoft.AspNetCore.SignalR;

namespace LostAndFound.Hubs
{
    /// <summary>
    /// SignalR 实时消息 Hub — 向前端推送系统通知
    /// </summary>
    public class NotificationHub : Hub
    {
        /// <summary>
        /// 用户连接时，按 UserId 加入分组（如果已登录）
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = Context.GetHttpContext()?.Session.GetInt32("UserId");
            if (userId.HasValue)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId.Value}");
            }
            // 所有用户默认加入"所有人"组，接收公共通知
            await Groups.AddToGroupAsync(Context.ConnectionId, "all");
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// 发送系统通知到所有在线用户
        /// </summary>
        public static async Task SendToAll(IHubContext<NotificationHub> hub, string type, string title, string message)
        {
            await hub.Clients.Group("all").SendAsync("ReceiveNotification", new
            {
                type,
                title,
                message,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        /// <summary>
        /// 发送通知到指定用户
        /// </summary>
        public static async Task SendToUser(IHubContext<NotificationHub> hub, int userId, string type, string title, string message)
        {
            await hub.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", new
            {
                type,
                title,
                message,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
    }
}
