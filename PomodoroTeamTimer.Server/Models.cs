namespace PomodoroTeamTimer;

public enum TimerPhase { Ready, Focus, Break, Completed }

public sealed class Member
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    public int CompletedPomodoros { get; set; }
}

public sealed class FocusRoom
{
    public string Code { get; set; } = "";
    public string HostToken { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public int FocusMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int TotalRounds { get; set; }
    public int CurrentRound { get; set; } = 1;
    public TimerPhase Phase { get; set; } = TimerPhase.Ready;
    public bool IsRunning { get; set; }
    public DateTimeOffset? EndsAtUtc { get; set; }
    public int RemainingSeconds { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SessionStartedAtUtc { get; set; }
    public int CompletedFocusSeconds { get; set; }
    public List<Member> Members { get; set; } = new();
}

public sealed class SessionRecord
{
    public string RoomCode { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset EndedAtUtc { get; set; }
    public int CompletedRounds { get; set; }
    public int FocusSeconds { get; set; }
    public List<string> Members { get; set; } = new();
}

public record CreateRoomRequest(string Title, int FocusMinutes, int BreakMinutes, int TotalRounds, string HostName);
public record JoinRoomRequest(string Name, string? MemberId);
public record HostCommandRequest(string HostToken);
public record RoomAccess(string Code, string MemberId, string? HostToken);
public record DailyStats(int CompletedPomodoros, int FocusSeconds, int Sessions);
