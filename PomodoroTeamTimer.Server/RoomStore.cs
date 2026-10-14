using System.Text.Json;

namespace PomodoroTeamTimer;

public sealed class RoomStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public Dictionary<string, FocusRoom> Rooms { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SessionRecord> History { get; } = new();

    public RoomStore(IWebHostEnvironment environment)
    {
        var dataDir = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "pomodoro-data.json");
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var state = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(_path), _json);
            if (state is null) return;
            foreach (var room in state.Rooms) Rooms[room.Code] = room;
            History.AddRange(state.History);
        }
        catch { /* A corrupt file must not prevent the LAN server from starting. */ }
    }

    public async Task SaveAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var temp = _path + ".tmp";
            var json = JsonSerializer.Serialize(new PersistedState(Rooms.Values.ToList(), History), _json);
            await File.WriteAllTextAsync(temp, json);
            File.Move(temp, _path, true);
        }
        finally { _fileLock.Release(); }
    }

    private sealed record PersistedState(List<FocusRoom> Rooms, List<SessionRecord> History);
}
