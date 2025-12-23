using Microsoft.AspNetCore.SignalR;

namespace SmugglerServer.ToolConnector;

public class SmugglerSignal : Hub
{
    public async Task SendMessage(string user, string id)
    {
        await Clients.All.SendAsync(user, id);
    }

    public override Task OnConnectedAsync()
    {
        // 웹 매니저가 접속 될때의 처리
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        // 웹 매니저가 해제 될때의 처리
        return base.OnDisconnectedAsync(exception);
    }
}