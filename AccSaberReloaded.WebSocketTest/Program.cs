using System.Text.Json;
using WebSocketSharp;

var options = CliOptions.Parse(args);
using var shutdown = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

Console.WriteLine("ScoreSaber websocket test");
Console.WriteLine($"URL: {options.Url}");
Console.WriteLine(options.UserId is null ? "User filter: <none>" : $"User filter: {options.UserId}");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

do
{
    try
    {
        await ReceiveScores(options, shutdown.Token);
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] Receive failed: {ex.GetType().Name}: {ex.Message}");
    }

    if (!options.Reconnect || shutdown.IsCancellationRequested)
    {
        break;
    }

    Console.WriteLine($"[{DateTimeOffset.Now:O}] Reconnecting in {options.ReconnectDelay.TotalSeconds:N0}s...");
    try
    {
        await Task.Delay(options.ReconnectDelay, shutdown.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
} while (!shutdown.IsCancellationRequested);

static async Task ReceiveScores(CliOptions options, CancellationToken cancellationToken)
{
    using var socket = new WebSocketSharp.WebSocket(options.Url);
    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    socket.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;

    socket.OnOpen += (_, _) =>
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] Connected.");
    };

    socket.OnMessage += (_, e) =>
    {
        if (e.IsBinary)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:O}] Binary message: {e.RawData.Length} bytes");
            return;
        }

        if (options.PrintRaw)
        {
            Console.WriteLine(e.Data);
        }

        PrintMessage(e.Data, options.UserId);
    };

    socket.OnError += (_, e) =>
    {
        var exception = e.Exception is null ? string.Empty : $" | {e.Exception.GetType().Name}: {e.Exception.Message}";
        Console.WriteLine($"[{DateTimeOffset.Now:O}] WebSocketSharp error: {e.Message}{exception}");
        closed.TrySetResult();
    };

    socket.OnClose += (_, e) =>
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] Closed: code={e.Code} reason={e.Reason} clean={e.WasClean}");
        closed.TrySetResult();
    };

    await using var _ = cancellationToken.Register(() =>
    {
        try
        {
            if (socket.IsAlive)
            {
                socket.Close(CloseStatusCode.Normal, "Test app stopping");
            }
        }
        finally
        {
            closed.TrySetCanceled(cancellationToken);
        }
    });

    Console.WriteLine($"[{DateTimeOffset.Now:O}] Connecting...");
    socket.Connect();
    using var keepAlive = StartKeepAlive(socket, options, closed);
    await closed.Task.WaitAsync(cancellationToken);
}

static Timer? StartKeepAlive(WebSocketSharp.WebSocket socket, CliOptions options, TaskCompletionSource closed)
{
    if (options.KeepAliveInterval <= TimeSpan.Zero)
    {
        return null;
    }

    return new Timer(_ =>
    {
        if (socket.ReadyState != WebSocketState.Open)
        {
            return;
        }

        try
        {
            if (!socket.Ping())
            {
                Console.WriteLine($"[{DateTimeOffset.Now:O}] Keepalive ping failed.");
                closed.TrySetResult();
            }
            else if (options.LogKeepAlive)
            {
                Console.WriteLine($"[{DateTimeOffset.Now:O}] Keepalive ping OK.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:O}] Keepalive ping error: {ex.GetType().Name}: {ex.Message}");
            closed.TrySetResult();
        }
    }, null, options.KeepAliveInterval, options.KeepAliveInterval);
}

static void PrintMessage(string message, string? userFilter)
{
    if (string.Equals(message, "Connected to the ScoreSaber WSS", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] ScoreSaber connection banner received.");
        return;
    }

    ScoreSaberCommand? command;
    try
    {
        command = JsonSerializer.Deserialize<ScoreSaberCommand>(message, JsonOptions.Default);
    }
    catch (JsonException ex)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] Invalid JSON: {ex.Message}");
        return;
    }

    if (command?.CommandData is null)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] JSON message without commandData.");
        return;
    }

    var data = command.CommandData;
    var userId = data.Score?.LeaderboardPlayerInfo?.Id;
    if (userFilter is not null && !string.Equals(userId, userFilter, StringComparison.Ordinal))
    {
        return;
    }

    var searchString = $"{data.Leaderboard?.SongHash}_{data.Leaderboard?.Difficulty?.GameMode}_{data.Leaderboard?.Difficulty?.Difficulty}".ToUpperInvariant();
    Console.WriteLine(
        $"[{DateTimeOffset.Now:O}] user={userId ?? "<unknown>"} | " +
        $"score={data.Score?.ModifiedScore?.ToString() ?? "<unknown>"} rank={data.Score?.Rank?.ToString() ?? "<unknown>"} pp={data.Score?.Pp?.ToString("N3") ?? "<unknown>"} | " +
        $"{searchString}");
}

internal sealed record ScoreSaberCommand
{
    public ScoreSaberCommandData? CommandData { get; init; }
}

internal sealed record ScoreSaberCommandData
{
    public ScoreSaberScore? Score { get; init; }
    public ScoreSaberLeaderboard? Leaderboard { get; init; }
}

internal sealed record ScoreSaberScore
{
    public double? Pp { get; init; }
    public int? ModifiedScore { get; init; }
    public int? Rank { get; init; }
    public ScoreSaberPlayerInfo? LeaderboardPlayerInfo { get; init; }
}

internal sealed record ScoreSaberPlayerInfo
{
    public string? Id { get; init; }
}

internal sealed record ScoreSaberLeaderboard
{
    public string? SongHash { get; init; }
    public ScoreSaberDifficulty? Difficulty { get; init; }
}

internal sealed record ScoreSaberDifficulty
{
    public int? Difficulty { get; init; }
    public string? GameMode { get; init; }
}

internal sealed record CliOptions
{
    private const string DefaultUrl = "wss://scoresaber.com/ws";

    public string Url { get; private init; } = DefaultUrl;
    public string? UserId { get; private init; }
    public bool PrintRaw { get; private init; }
    public bool Reconnect { get; private init; } = true;
    public TimeSpan ReconnectDelay { get; private init; } = TimeSpan.FromSeconds(5);
    public TimeSpan KeepAliveInterval { get; private init; } = TimeSpan.FromSeconds(45);
    public bool LogKeepAlive { get; private init; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url" when TryReadValue(args, ref i, out var value):
                    options = options with { Url = value };
                    break;
                case "--user" when TryReadValue(args, ref i, out var value):
                    options = options with { UserId = value };
                    break;
                case "--raw":
                    options = options with { PrintRaw = true };
                    break;
                case "--no-reconnect":
                    options = options with { Reconnect = false };
                    break;
                case "--reconnect-delay" when TryReadValue(args, ref i, out var value) && double.TryParse(value, out var seconds):
                    options = options with { ReconnectDelay = TimeSpan.FromSeconds(seconds) };
                    break;
                case "--keepalive-interval" when TryReadValue(args, ref i, out var value) && double.TryParse(value, out var seconds):
                    options = options with { KeepAliveInterval = TimeSpan.FromSeconds(seconds) };
                    break;
                case "--no-keepalive":
                    options = options with { KeepAliveInterval = TimeSpan.Zero };
                    break;
                case "--log-keepalive":
                    options = options with { LogKeepAlive = true };
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    Console.WriteLine($"Unknown or incomplete argument: {args[i]}");
                    PrintHelp();
                    Environment.Exit(1);
                    break;
            }
        }

        return options;
    }

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 < args.Length)
        {
            value = args[++index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project AccSaberReloaded.WebSocketTest -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --url <url>                  WebSocket URL. Defaults to ScoreSaber.");
        Console.WriteLine("  --user <steamId>             Only print scores for one user.");
        Console.WriteLine("  --raw                        Print raw JSON before parsed summary.");
        Console.WriteLine("  --no-reconnect               Exit after the first disconnect/error.");
        Console.WriteLine("  --reconnect-delay <seconds>  Delay before reconnecting. Default: 5.");
        Console.WriteLine("  --keepalive-interval <sec>   Send websocket ping this often. Default: 45.");
        Console.WriteLine("  --no-keepalive               Disable websocket ping keepalive.");
        Console.WriteLine("  --log-keepalive              Print successful keepalive pings.");
    }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
