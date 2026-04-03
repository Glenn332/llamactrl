using Microsoft.AspNetCore.SignalR;

namespace LlamaCtrl.Hubs;

public class LogHub : Hub
{
    public async Task JoinInstance(string instanceId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, instanceId);

    public async Task LeaveInstance(string instanceId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, instanceId);
}
