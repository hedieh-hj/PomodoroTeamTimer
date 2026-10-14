using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;

namespace PomodoroTeamTimer;

public sealed class RoomService
{
    private readonly RoomStore _store;
    private readonly IHubContext<RoomHub> _hub;
    private readonly object _sync = new();
    public RoomService(RoomStore store, IHubContext<RoomHub> hub) { _store = store; _hub = hub; }

    public async Task<RoomAccess> CreateAsync(CreateRoomRequest request)
    {
        Validate(request);
        FocusRoom room;
        lock (_sync)
        {
            string code;
            do code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); while (_store.Rooms.ContainsKey(code));
            var host = new Member { Name = CleanName(request.HostName) };
            room = new FocusRoom
            {
                Code = code, Title = request.Title.Trim(), FocusMinutes = request.FocusMinutes,
                BreakMinutes = request.BreakMinutes, TotalRounds = request.TotalRounds,
                RemainingSeconds = request.FocusMinutes * 60, Members = new() { host }
            };
            _store.Rooms[code] = room;
        }
        await _store.SaveAsync();
        return new RoomAccess(room.Code, room.Members[0].Id, room.HostToken);
    }

    public async Task<RoomAccess?> JoinAsync(string code, JoinRoomRequest request)
    {
        Member member;
        lock (_sync)
        {
            if (!_store.Rooms.TryGetValue(code, out var room) || room.Phase == TimerPhase.Completed) return null;
            member = !string.IsNullOrWhiteSpace(request.MemberId)
                ? room.Members.FirstOrDefault(x => x.Id == request.MemberId) ?? new Member()
                : new Member();
            member.Name = CleanName(request.Name);
            member.LastSeenUtc = DateTimeOffset.UtcNow;
            if (!room.Members.Contains(member)) room.Members.Add(member);
        }
        await _store.SaveAsync();
        await BroadcastAsync(code, "members");
        return new RoomAccess(code, member.Id, null);
    }

    public object? Snapshot(string code)
    {
        lock (_sync)
        {
            if (!_store.Rooms.TryGetValue(code, out var room)) return null;
            var remaining = room.IsRunning && room.EndsAtUtc.HasValue
                ? Math.Max(0, (int)Math.Ceiling((room.EndsAtUtc.Value - DateTimeOffset.UtcNow).TotalSeconds))
                : room.RemainingSeconds;
            return new
            {
                room.Code, room.Title, room.FocusMinutes, room.BreakMinutes, room.TotalRounds,
                room.CurrentRound, phase = room.Phase.ToString().ToLowerInvariant(), room.IsRunning,
                remainingSeconds = remaining, serverNowUtc = DateTimeOffset.UtcNow, room.EndsAtUtc,
                members = room.Members.Select(m => new { m.Id, m.Name, online = DateTimeOffset.UtcNow - m.LastSeenUtc < TimeSpan.FromSeconds(35), m.CompletedPomodoros })
            };
        }
    }

    public async Task<bool> CommandAsync(string code, string command, string hostToken)
    {
        string eventName = "state";
        lock (_sync)
        {
            if (!_store.Rooms.TryGetValue(code, out var room) || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(room.HostToken), System.Text.Encoding.UTF8.GetBytes(hostToken ?? ""))) return false;
            var now = DateTimeOffset.UtcNow;
            switch (command)
            {
                case "start":
                    if (room.Phase is TimerPhase.Ready) { room.Phase = TimerPhase.Focus; room.SessionStartedAtUtc = now; room.RemainingSeconds = room.FocusMinutes * 60; eventName = "focusStarted"; }
                    if (!room.IsRunning && room.Phase is not TimerPhase.Completed) { room.IsRunning = true; room.EndsAtUtc = now.AddSeconds(room.RemainingSeconds); }
                    break;
                case "pause":
                    if (room.IsRunning) { room.RemainingSeconds = Math.Max(0, (int)Math.Ceiling((room.EndsAtUtc!.Value - now).TotalSeconds)); room.IsRunning = false; room.EndsAtUtc = null; }
                    break;
                case "resume":
                    if (!room.IsRunning && room.Phase is TimerPhase.Focus or TimerPhase.Break) { room.IsRunning = true; room.EndsAtUtc = now.AddSeconds(room.RemainingSeconds); }
                    break;
                case "reset":
                    room.Phase = TimerPhase.Ready; room.IsRunning = false; room.EndsAtUtc = null; room.CurrentRound = 1; room.RemainingSeconds = room.FocusMinutes * 60; room.CompletedFocusSeconds = 0; room.SessionStartedAtUtc = null;
                    foreach (var member in room.Members) member.CompletedPomodoros = 0;
                    break;
                case "skip-break":
                    if (room.Phase != TimerPhase.Break) return false;
                    room.Phase = TimerPhase.Focus; room.CurrentRound++; room.RemainingSeconds = room.FocusMinutes * 60; room.EndsAtUtc = now.AddSeconds(room.RemainingSeconds); room.IsRunning = true; eventName = "focusStarted";
                    break;
                case "end": Complete(room, now); eventName = "sessionCompleted"; break;
                default: return false;
            }
        }
        await _store.SaveAsync();
        await BroadcastAsync(code, eventName);
        return true;
    }

    public async Task HeartbeatAsync(string code, string memberId)
    {
        lock (_sync)
            if (_store.Rooms.TryGetValue(code, out var room))
            {
                var member = room.Members.FirstOrDefault(x => x.Id == memberId);
                if (member is not null) member.LastSeenUtc = DateTimeOffset.UtcNow;
            }
        await BroadcastAsync(code, "members");
    }

    public async Task TickAsync()
    {
        var transitions = new List<(string code, string eventName)>();
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var room in _store.Rooms.Values.Where(r => r.IsRunning && r.EndsAtUtc <= now))
            {
                if (room.Phase == TimerPhase.Focus)
                {
                    room.CompletedFocusSeconds += room.FocusMinutes * 60;
                    foreach (var member in room.Members) member.CompletedPomodoros++;
                    room.Phase = TimerPhase.Break; room.RemainingSeconds = room.BreakMinutes * 60;
                    room.EndsAtUtc = now.AddSeconds(room.RemainingSeconds); transitions.Add((room.Code, "breakStarted"));
                }
                else if (room.Phase == TimerPhase.Break && room.CurrentRound < room.TotalRounds)
                {
                    room.CurrentRound++; room.Phase = TimerPhase.Focus; room.RemainingSeconds = room.FocusMinutes * 60;
                    room.EndsAtUtc = now.AddSeconds(room.RemainingSeconds); transitions.Add((room.Code, "focusStarted"));
                }
                else { Complete(room, now); transitions.Add((room.Code, "sessionCompleted")); }
            }
        }
        if (transitions.Count > 0) await _store.SaveAsync();
        foreach (var item in transitions) await BroadcastAsync(item.code, item.eventName);
    }

    public IReadOnlyList<SessionRecord> History() { lock (_sync) return _store.History.OrderByDescending(x => x.EndedAtUtc).Take(100).ToList(); }
    public DailyStats Stats()
    {
        lock (_sync)
        {
            var today = DateTimeOffset.Now.Date;
            var rows = _store.History.Where(x => x.EndedAtUtc.ToLocalTime().Date == today).ToList();
            return new(rows.Sum(x => x.CompletedRounds), rows.Sum(x => x.FocusSeconds), rows.Count);
        }
    }

    private void Complete(FocusRoom room, DateTimeOffset now)
    {
        if (room.Phase == TimerPhase.Completed) return;
        room.Phase = TimerPhase.Completed; room.IsRunning = false; room.EndsAtUtc = null; room.RemainingSeconds = 0;
        _store.History.Add(new SessionRecord { RoomCode = room.Code, Title = room.Title, StartedAtUtc = room.SessionStartedAtUtc ?? room.CreatedAtUtc, EndedAtUtc = now, CompletedRounds = room.Members.FirstOrDefault()?.CompletedPomodoros ?? 0, FocusSeconds = room.CompletedFocusSeconds, Members = room.Members.Select(x => x.Name).ToList() });
    }

    private async Task BroadcastAsync(string code, string eventName)
    {
        await _hub.Clients.Group(code).SendAsync("roomUpdated", Snapshot(code));
        if (eventName != "state" && eventName != "members") await _hub.Clients.Group(code).SendAsync(eventName);
    }

    private static string CleanName(string value) => string.IsNullOrWhiteSpace(value) ? "مهمان" : value.Trim()[..Math.Min(value.Trim().Length, 30)];
    private static void Validate(CreateRoomRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 100) throw new ArgumentException("عنوان جلسه معتبر نیست.");
        if (r.FocusMinutes is < 1 or > 180 || r.BreakMinutes is < 1 or > 60 || r.TotalRounds is < 1 or > 20) throw new ArgumentException("مقادیر زمان یا تعداد دور معتبر نیست.");
    }
}
