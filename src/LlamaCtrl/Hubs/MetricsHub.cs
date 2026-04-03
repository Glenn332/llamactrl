using Microsoft.AspNetCore.SignalR;

namespace LlamaCtrl.Hubs;

public class MetricsHub : Hub
{
    public async Task JoinMetrics()
        => await Groups.AddToGroupAsync(Context.ConnectionId, "metrics");

    public async Task LeaveMetrics()
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "metrics");
}
