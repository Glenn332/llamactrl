using Microsoft.AspNetCore.SignalR;

namespace LlamaCtrl.Hubs;

public class DownloadHub : Hub
{
    public static string ToGroupName(string downloadId) => downloadId.Replace('/', '_');

    public async Task JoinDownload(string downloadId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, ToGroupName(downloadId));

    public async Task LeaveDownload(string downloadId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, ToGroupName(downloadId));
}
