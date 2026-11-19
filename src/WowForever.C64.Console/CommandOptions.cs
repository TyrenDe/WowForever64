using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WowForever.C64.Core;

namespace WowForever.C64.ConsoleApp;

public sealed record CommandOptions(string Command, Uri? Endpoint, int TimeoutSeconds, string Message, string? Password)
{
    public string? SnapshotPath { get; init; }
    public WatchOptions Watch { get; init; } = new();
    public VicConfiguration Vic { get; init; } = new();
    public string? CharsetPath { get; init; }
    public string? OutputPath { get; init; }
    public int Page { get; init; }
    public NoticeKind? Context { get; init; }

    public static CommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
            return new CommandOptions("help", null, 5, "", null);
        var command = args[0];
        if (command is not ("preview" or "status" or "send" or "watch" or "charset"))
            throw new ArgumentException("Command must be preview, status, send, watch, or charset. Use --help for examples.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
        {
            if (args[i] is not ("--host" or "--config" or "--timeout-seconds" or "--message" or "--snapshot" or "--output" or "--charset" or "--page"
                or "--context" or "--diagnostics"))
                throw new ArgumentException($"Unknown option: {args[i]}");
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {args[i]}.");
            if (!values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException($"Duplicate option: {args[i]}");
        }

        var path = values.GetValueOrDefault("--config") ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var config = JsonSerializer.Deserialize<FileConfiguration>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new ArgumentException("Configuration must be a JSON object.");

        var host = values.GetValueOrDefault("--host") ?? Environment.GetEnvironmentVariable("C64_HOST") ?? config.Host;
        var timeout = config.TimeoutSeconds;
        if (values.TryGetValue("--timeout-seconds", out var timeoutValue)
            && !int.TryParse(timeoutValue, NumberStyles.None, CultureInfo.InvariantCulture, out timeout))
            throw new ArgumentException("Timeout must be an integer between 1 and 120 seconds.");
        if (timeout is < 1 or > 120)
            throw new ArgumentException("Timeout must be between 1 and 120 seconds.");

        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(host))
            endpoint = ParseEndpoint(host);
        if (command is not ("preview" or "charset") && endpoint is null)
            throw new ArgumentException("Specify the C64 address with --host, C64_HOST, or a --config file.");

        var message = values.GetValueOrDefault("--message") ?? config.Message
            ?? throw new ArgumentException("Message cannot be null.");
        if (values.ContainsKey("--snapshot") && command is not ("preview" or "send"))
            throw new ArgumentException("--snapshot is supported by preview and send.");
        var watch = config.Watch ?? throw new ArgumentException("watch configuration cannot be null.");
        if (values.TryGetValue("--diagnostics", out var diagnostics))
        {
            if (command != "watch" || !bool.TryParse(diagnostics, out var enabled))
                throw new ArgumentException("--diagnostics true or false is supported by watch.");
            watch = watch with { Diagnostics = enabled };
        }
        watch.Validate();
        NoticeKind? context = null;
        if (values.TryGetValue("--context", out var notice))
        {
            if (command is not ("preview" or "send"))
                throw new ArgumentException("--context is supported by preview and send.");
            context = notice switch
            {
                "level" => NoticeKind.LevelUp,
                "death" => NoticeKind.Death,
                "quest" => NoticeKind.Quest,
                "zone" => NoticeKind.Zone,
                _ => throw new ArgumentException("--context requires level, death, quest, or zone.")
            };
        }
        var display = config.Display ?? throw new ArgumentException("display configuration cannot be null.");
        var vic = new VicConfiguration { ScreenAddress = display.ScreenAddress, CharsetAddress = display.CharsetAddress };
        vic.Validate();
        var charsetPath = values.GetValueOrDefault("--charset");
        if (charsetPath is null && display.CharsetFile is not null)
            charsetPath = Path.GetFullPath(display.CharsetFile, Path.GetDirectoryName(Path.GetFullPath(path))!);
        var output = values.GetValueOrDefault("--output");
        if (command == "charset" && output is null) throw new ArgumentException("charset requires --output <file.bin or file.png>.");
        if (output is not null && command is not ("preview" or "charset")) throw new ArgumentException("--output is supported by preview and charset.");
        var page = 0;
        if (values.TryGetValue("--page", out var pageText)
            && (!int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 0 || command is not ("preview" or "send")))
            throw new ArgumentException("--page requires a nonnegative, zero-based page for preview/send.");
        return new CommandOptions(command, endpoint, timeout, message, Environment.GetEnvironmentVariable("C64_PASSWORD"))
        {
            SnapshotPath = values.GetValueOrDefault("--snapshot"), Watch = watch, Vic = vic,
            CharsetPath = charsetPath, OutputPath = output, Page = page, Context = context
        };
    }

    private static Uri ParseEndpoint(string host)
    {
        var input = host.Trim();
        if (!input.Contains("://", StringComparison.Ordinal))
            input = "http://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Host must be an IP/hostname or HTTP(S) origin, optionally with a port; no path or credentials.");
        return uri;
    }

    private sealed class FileConfiguration
    {
        public string? Host { get; init; }
        public int TimeoutSeconds { get; init; } = 5;
        public string? Message { get; init; } = "HELLO FROM THE WINDOWS PC!";
        public WatchOptions? Watch { get; init; } = new();
        public FileDisplaySettings? Display { get; init; } = new();
    }

    private sealed class FileDisplaySettings
    {
        public int ScreenAddress { get; init; } = 0x0400;
        public int CharsetAddress { get; init; } = 0x2000;
        public string? CharsetFile { get; init; }
    }
}
