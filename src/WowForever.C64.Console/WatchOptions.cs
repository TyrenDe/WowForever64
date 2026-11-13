namespace WowForever.C64.ConsoleApp;

public sealed record WatchOptions
{
    public string ProcessName { get; init; } = "Wow";
    public int PollMilliseconds { get; init; } = 100;
    public int StaleSeconds { get; init; } = 3;
    public int PageSeconds { get; init; } = 8;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProcessName) || ProcessName.Length > 80
            || PollMilliseconds is < 50 or > 150 || StaleSeconds is < 1 or > 30 || PageSeconds is < 3 or > 120)
            throw new ArgumentException("watch settings require a processName, pollMilliseconds 50..150, staleSeconds 1..30, and pageSeconds 3..120.");
    }
}
