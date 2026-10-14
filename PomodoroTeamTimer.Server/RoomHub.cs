using Microsoft.AspNetCore.SignalR;

namespace PomodoroTeamTimer;

public sealed class RoomHub : Hub
{
    private readonly RoomService _rooms;
    public RoomHub(RoomService rooms) => _rooms = rooms;

    public async Task Subscribe(string code, string memberId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        await _rooms.HeartbeatAsync(code, memberId);
        await Clients.Caller.SendAsync("roomUpdated", _rooms.Snapshot(code));
    }

    public Task Heartbeat(string code, string memberId) => _rooms.HeartbeatAsync(code, memberId);
}
