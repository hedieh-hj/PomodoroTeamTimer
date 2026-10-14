using PomodoroTeamTimer;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://0.0.0.0:5080");
builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddSingleton<RoomStore>();
builder.Services.AddSingleton<RoomService>();
builder.Services.AddHostedService<TimerWorker>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/rooms", async (CreateRoomRequest request, RoomService rooms) =>
{
    try { return Results.Ok(await rooms.CreateAsync(request)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
});
app.MapPost("/api/rooms/{code}/join", async (string code, JoinRoomRequest request, RoomService rooms) =>
{
    try
    {
        var access = await rooms.JoinAsync(code.Trim(), request);
        return access is null ? Results.NotFound(new { message = "اتاق پیدا نشد یا جلسه پایان یافته است." }) : Results.Ok(access);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
});
app.MapGet("/api/rooms/{code}", (string code, RoomService rooms) => rooms.Snapshot(code) is { } state ? Results.Ok(state) : Results.NotFound());
app.MapPost("/api/rooms/{code}/{command}", async (string code, string command, HostCommandRequest request, RoomService rooms) =>
    await rooms.CommandAsync(code, command, request.HostToken) ? Results.Ok(rooms.Snapshot(code)) : Results.Unauthorized());
app.MapGet("/api/history", (RoomService rooms) => rooms.History());
app.MapGet("/api/stats/today", (RoomService rooms) => rooms.Stats());
app.MapHub<RoomHub>("/hubs/room");
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
