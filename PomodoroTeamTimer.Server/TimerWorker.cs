namespace PomodoroTeamTimer;

public sealed class TimerWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    public TimerWorker(IServiceProvider services) => _services = services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RoomService>().TickAsync();
        }
    }
}
