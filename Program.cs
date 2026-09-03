using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

var options = CliOptions.Parse(args);
if (options.Command == "stats")
{
    PaperStats.Print(options.JournalPath);
    return;
}

if (options.Command == "shadow-stats")
{
    ShadowStats.Print(options.ShadowJournalPath);
    return;
}

var config = new BotConfig(
    Bankroll: options.Bankroll,
    MaxPosition: options.MaxPosition,
    MinEdge: options.MinEdge,
    MaxSpread: options.MaxSpread,
    MinMoveBps: options.MinMoveBps,
    MaxMarketMinutes: options.MaxMarketMinutes,
    MinEntrySecondsAfterStart: options.MinEntrySecondsAfterStart,
    MaxEntrySecondsBeforeEnd: options.MaxEntrySecondsBeforeEnd,
    MaxEntrySecondsAfterStart: options.MaxEntrySecondsAfterStart,
    MinProbability: options.MinProbability,
    MinYesProbability: options.MinYesProbability,
    MinNoProbability: options.MinNoProbability,
    MinYesEdge: options.MinYesEdge,
    MinNoEdge: options.MinNoEdge,
    MaxYesAsk: options.MaxYesAsk,
    MaxNoAsk: options.MaxNoAsk,
    SideFilter: options.SideFilter,
    FirstSignalOnly: options.FirstSignalOnly
);

if (options.Command == "replay")
{
    SnapshotReplay.Print(options.SnapshotJournalPath, config);
    return;
}

if (options.Command == "analyze")
{
    SnapshotReplay.Analyze(options.SnapshotJournalPath, config);
    return;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(config.RequestTimeoutSeconds) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("polymarket-bot/0.1");

var polymarket = new PolymarketClient(config, http);
var spot = new SpotPriceClient(config, http);
var strategy = new PriceLagStrategy(config);
var momentum = new SpotMomentum(config.MomentumWindow);
var shadowTracker = new ShadowTracker(options.ShadowJournalPath);
var snapshotRecorder = new SnapshotRecorder(options.SnapshotJournalPath);
var firstSignalGate = new FirstSignalGate(config.FirstSignalOnly);

if (options.Command == "scan")
{
    await Runner.RunOnce(polymarket, spot, strategy, momentum, options.Limit, options.WarmupSeconds, paperTrader: null, shadowTracker, snapshotRecorder, firstSignalGate, options.Verbose);
    return;
}

var paperTrader = new PaperTrader(options.JournalPath);
while (true)
{
    await Runner.RunOnce(polymarket, spot, strategy, momentum, options.Limit, warmupSeconds: 0, paperTrader, shadowTracker, snapshotRecorder, firstSignalGate, options.Verbose);
    await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds));
}

record BotConfig(
    string GammaBaseUrl = "https://gamma-api.polymarket.com",
    string ClobBaseUrl = "https://clob.polymarket.com",
    string CoinbaseBaseUrl = "https://api.exchange.coinbase.com",
    string BinanceBaseUrl = "https://api.binance.com",
    double RequestTimeoutSeconds = 8,
    double Bankroll = 50,
    double MaxPosition = 2,
    double MinEdge = 0.03,
    double MaxSpread = 0.06,
    double MinTopSize = 5,
    double MinMoveBps = 1.0,
    double MaxMarketMinutes = 6.0,
    double MinEntrySecondsAfterStart = 10.0,
    double MaxEntrySecondsBeforeEnd = 15.0,
    double MaxEntrySecondsAfterStart = 0.0,
    double MinProbability = 0.60,
    double MinYesProbability = 0.60,
    double MinNoProbability = 0.60,
    double MinYesEdge = 0.03,
    double MinNoEdge = 0.03,
    double MaxYesAsk = 1.0,
    double MaxNoAsk = 1.0,
    string SideFilter = "ANY",
    bool FirstSignalOnly = false,
    int MomentumWindow = 8
);

record CliOptions(
    string Command,
    int Limit,
    double Bankroll,
    double MaxPosition,
    double MinEdge,
    double MaxSpread,
    double MinMoveBps,
    double MaxMarketMinutes,
    double MinEntrySecondsAfterStart,
    double MaxEntrySecondsBeforeEnd,
    double MaxEntrySecondsAfterStart,
    double MinProbability,
    double MinYesProbability,
    double MinNoProbability,
    double MinYesEdge,
    double MinNoEdge,
    double MaxYesAsk,
    double MaxNoAsk,
    string SideFilter,
    double IntervalSeconds,
    double WarmupSeconds,
    string JournalPath,
    string ShadowJournalPath,
    string SnapshotJournalPath,
    bool FirstSignalOnly,
    bool Verbose
)
{
    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            Environment.Exit(args.Length == 0 ? 1 : 0);
        }

        var command = args[0].ToLowerInvariant();
        if (command is not ("scan" or "watch" or "stats" or "shadow-stats" or "replay" or "analyze"))
        {
            throw new ArgumentException("Command must be 'scan', 'watch', 'stats', 'shadow-stats', 'replay', or 'analyze'.");
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";
            map[key] = value;
        }

        var minProbability = Double(map, "min-probability", 0.60);
        var minEdge = Double(map, "min-edge", 0.03);
        var sideFilter = SideFilterValue(map.TryGetValue("side-filter", out var requestedSide) ? requestedSide : "ANY");

        return new CliOptions(
            Command: command,
            Limit: Int(map, "limit", 50),
            Bankroll: Double(map, "bankroll", 50),
            MaxPosition: Double(map, "max-position", 2),
            MinEdge: minEdge,
            MaxSpread: Double(map, "max-spread", 0.06),
            MinMoveBps: Double(map, "min-move-bps", 1.0),
            MaxMarketMinutes: Double(map, "max-market-minutes", 6.0),
            MinEntrySecondsAfterStart: Double(map, "min-entry-seconds-after-start", 10.0),
            MaxEntrySecondsBeforeEnd: Double(map, "max-entry-seconds-before-end", 15.0),
            MaxEntrySecondsAfterStart: Double(map, "max-entry-seconds-after-start", 0.0),
            MinProbability: minProbability,
            MinYesProbability: Double(map, "min-yes-probability", minProbability),
            MinNoProbability: Double(map, "min-no-probability", minProbability),
            MinYesEdge: Double(map, "min-yes-edge", minEdge),
            MinNoEdge: Double(map, "min-no-edge", minEdge),
            MaxYesAsk: Double(map, "max-yes-ask", 1.0),
            MaxNoAsk: Double(map, "max-no-ask", 1.0),
            SideFilter: sideFilter,
            IntervalSeconds: Double(map, "interval", 10),
            WarmupSeconds: Double(map, "warmup", 3),
            JournalPath: map.TryGetValue("journal", out var journal) ? journal : Path.Combine("data", "paper_trades.csv"),
            ShadowJournalPath: map.TryGetValue("shadow-journal", out var shadowJournal)
                ? shadowJournal
                : DefaultShadowJournal(map.TryGetValue("journal", out var journalForShadow) ? journalForShadow : Path.Combine("data", "paper_trades.csv")),
            SnapshotJournalPath: map.TryGetValue("snapshot-journal", out var snapshotJournal)
                ? snapshotJournal
                : DefaultSnapshotJournal(map.TryGetValue("journal", out var journalForSnapshots) ? journalForSnapshots : Path.Combine("data", "paper_trades.csv")),
            FirstSignalOnly: Bool(map, "first-signal-only", false),
            Verbose: Bool(map, "verbose", false)
        );
    }

    static int Int(Dictionary<string, string> map, string key, int fallback) =>
        map.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    static double Double(Dictionary<string, string> map, string key, double fallback) =>
        map.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    static bool Bool(Dictionary<string, string> map, string key, bool fallback) =>
        map.TryGetValue(key, out var value)
            ? value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("1", StringComparison.OrdinalIgnoreCase)
            : fallback;

    static string SideFilterValue(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized is "ANY" or "YES" or "NO")
        {
            return normalized;
        }

        throw new ArgumentException("--side-filter must be ANY, YES, or NO.");
    }

    static string DefaultShadowJournal(string journalPath)
    {
        var directory = Path.GetDirectoryName(journalPath);
        var name = Path.GetFileNameWithoutExtension(journalPath);
        var extension = Path.GetExtension(journalPath);
        return Path.Combine(string.IsNullOrWhiteSpace(directory) ? "." : directory, $"{name}_shadow{extension}");
    }

    static string DefaultSnapshotJournal(string journalPath)
    {
        var directory = Path.GetDirectoryName(journalPath);
        var name = Path.GetFileNameWithoutExtension(journalPath);
        return Path.Combine(string.IsNullOrWhiteSpace(directory) ? "." : directory, $"{name}_snapshots.csv");
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
        Polymarket crypto bot v0

        Usage:
          dotnet run -- scan --limit 50
          dotnet run -- watch --limit 50 --interval 10
          dotnet run -- stats
          dotnet run -- shadow-stats
          dotnet run -- replay --snapshot-journal data/paper_trades_v11_snapshots.csv
          dotnet run -- analyze --snapshot-journal data/paper_trades_v11_snapshots.csv

        Options:
          --bankroll 50
          --max-position 2
          --min-edge 0.03
          --min-yes-edge 0.03
          --min-no-edge 0.03
          --max-yes-ask 1.00
          --max-no-ask 1.00
          --max-spread 0.06
          --min-move-bps 1
          --max-market-minutes 6
          --min-entry-seconds-after-start 10
          --max-entry-seconds-before-end 15
          --max-entry-seconds-after-start 0
          --min-probability 0.60
          --min-yes-probability 0.60
          --min-no-probability 0.60
          --side-filter ANY
          --first-signal-only
          --warmup 3
          --journal data/paper_trades.csv
          --shadow-journal data/paper_trades_shadow.csv
          --snapshot-journal data/paper_trades_snapshots.csv
          --verbose
        """);
    }
}

record MarketCandidate(
    string EventTitle,
    string MarketTitle,
    string? MarketSlug,
    string Asset,
    string YesTokenId,
    string? NoTokenId,
    string? EndDate,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc
);

record TopOfBook(string TokenId, double? Bid, double BidSize, double? Ask, double AskSize, double? Spread);

record SpotSample(string Asset, double Price, DateTimeOffset Timestamp);

record Signal(
    MarketCandidate Market,
    string Side,
    string TokenId,
    double Ask,
    double? Bid,
    double EstimatedProbability,
    double Edge,
    double Spread,
    double SizeUsd,
    string Reason
);

record PriceEstimate(double ProbabilityUp, double DistanceBps, double RecentMomentumBps);

record ShadowSignal(
    MarketCandidate Market,
    string Side,
    string TokenId,
    double Ask,
    double? Bid,
    double EstimatedProbability,
    double Edge,
    double Spread,
    double SizeUsd,
    string RejectionReason,
    string Reason
);

record SignalDecision(Signal? Signal, string Reason, ShadowSignal? Shadow)
{
    public static SignalDecision Accepted(Signal signal) => new(signal, "accepted", null);
    public static SignalDecision Rejected(string reason, ShadowSignal? shadow = null) => new(null, reason, shadow);
}

record PaperPosition(
    string Slug,
    string Asset,
    string Side,
    double EntryAsk,
    double? EntryBid,
    double EstimatedProbability,
    double Edge,
    double Spread,
    double SizeUsd,
    double Shares,
    double OpenSpot,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    DateTimeOffset EntryTimeUtc,
    string Reason
);

record ShadowPosition(
    string Slug,
    string Asset,
    string Side,
    double EntryAsk,
    double? EntryBid,
    double EstimatedProbability,
    double Edge,
    double Spread,
    double SizeUsd,
    double Shares,
    double OpenSpot,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    DateTimeOffset EntryTimeUtc,
    string RejectionReason,
    string Reason
);

sealed class PaperTrader
{
    readonly string journalPath;
    readonly Dictionary<string, PaperPosition> openPositions = new(StringComparer.OrdinalIgnoreCase);

    public PaperTrader(string journalPath)
    {
        this.journalPath = journalPath;
        EnsureJournal();
        LoadOpenPositions();
    }

    public IReadOnlyCollection<string> OpenAssets => openPositions.Values.Select(position => position.Asset).Distinct().ToList();

    public void OnSignal(Signal signal, double? currentSpot, double? openSpot)
    {
        var slug = signal.Market.MarketSlug;
        if (string.IsNullOrWhiteSpace(slug)
            || currentSpot is null
            || openSpot is null
            || signal.Market.WindowStartUtc is null
            || signal.Market.WindowEndUtc is null)
        {
            return;
        }

        if (openPositions.TryGetValue(slug, out var existing))
        {
            Console.WriteLine($"[PAPER HOLD] Already open: {existing.Side} {existing.Asset} {slug}");
            return;
        }

        var shares = signal.SizeUsd / signal.Ask;
        var position = new PaperPosition(
            Slug: slug,
            Asset: signal.Market.Asset,
            Side: signal.Side,
            EntryAsk: signal.Ask,
            EntryBid: signal.Bid,
            EstimatedProbability: signal.EstimatedProbability,
            Edge: signal.Edge,
            Spread: signal.Spread,
            SizeUsd: signal.SizeUsd,
            Shares: shares,
            OpenSpot: openSpot.Value,
            WindowStartUtc: signal.Market.WindowStartUtc.Value,
            WindowEndUtc: signal.Market.WindowEndUtc.Value,
            EntryTimeUtc: DateTimeOffset.UtcNow,
            Reason: signal.Reason
        );

        openPositions[slug] = position;
        Append(position, eventType: "OPEN", currentSpot: currentSpot.Value, outcome: "", pnl: null);
        Console.WriteLine($"[PAPER OPEN] {position.Side} {position.Asset} {position.Shares:0.####} shares at {position.EntryAsk:0.000}");
    }

    public void SettleDue(SpotMomentum momentum)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var position in openPositions.Values.ToList())
        {
            if (now < position.WindowEndUtc.AddSeconds(10))
            {
                continue;
            }

            var currentSpot = momentum.LastPrice(position.Asset);
            if (currentSpot is null)
            {
                continue;
            }

            var upWon = currentSpot.Value >= position.OpenSpot;
            var outcome = upWon ? "YES" : "NO";
            var won = string.Equals(position.Side, outcome, StringComparison.OrdinalIgnoreCase);
            var payout = won ? position.Shares : 0.0;
            var pnl = payout - position.SizeUsd;

            Append(position, eventType: "CLOSE", currentSpot: currentSpot.Value, outcome: outcome, pnl: pnl);
            openPositions.Remove(position.Slug);
            Console.WriteLine($"[PAPER CLOSE] {position.Side} {position.Asset} outcome={outcome} pnl=${pnl:0.00}");
        }
    }

    void EnsureJournal()
    {
        var directory = Path.GetDirectoryName(journalPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(journalPath))
        {
            return;
        }

        File.AppendAllText(
            journalPath,
            "event_type,timestamp_utc,slug,asset,side,entry_ask,entry_bid,estimated_probability,edge,spread,size_usd,shares,open_spot,current_spot,window_start_utc,window_end_utc,outcome,pnl,reason" + Environment.NewLine
        );
    }

    void LoadOpenPositions()
    {
        var lines = File.ReadAllLines(journalPath);
        if (lines.Length <= 1)
        {
            return;
        }

        var header = PaperStats.SplitCsv(lines[0]);
        var columns = header.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index);
        foreach (var line in lines.Skip(1))
        {
            var row = PaperStats.SplitCsv(line);
            if (row.Count < header.Count || !columns.ContainsKey("event_type") || !columns.ContainsKey("slug"))
            {
                continue;
            }

            var eventType = row[columns["event_type"]];
            var slug = row[columns["slug"]];
            if (eventType == "CLOSE")
            {
                openPositions.Remove(slug);
                continue;
            }

            if (eventType != "OPEN")
            {
                continue;
            }

            var position = TryReadPosition(row, columns);
            if (position is not null)
            {
                openPositions[position.Slug] = position;
            }
        }

        if (openPositions.Count > 0)
        {
            Console.WriteLine($"Loaded {openPositions.Count} open paper position(s) from {journalPath}");
        }
    }

    static PaperPosition? TryReadPosition(IReadOnlyList<string> row, Dictionary<string, int> columns)
    {
        string Value(string name) => columns.TryGetValue(name, out var index) && index < row.Count ? row[index] : "";
        var slug = Value("slug");
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(Value("window_start_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var windowStart)
            || !DateTimeOffset.TryParse(Value("window_end_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var windowEnd)
            || !DateTimeOffset.TryParse(Value("timestamp_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var entryTime))
        {
            return null;
        }

        var entryAsk = PaperStats.Parse(Value("entry_ask"));
        var sizeUsd = PaperStats.Parse(Value("size_usd"));
        var shares = PaperStats.Parse(Value("shares"));
        var openSpot = PaperStats.Parse(Value("open_spot"));
        var edge = PaperStats.Parse(Value("edge"));
        var spread = PaperStats.Parse(Value("spread"));
        var probability = PaperStats.Parse(Value("estimated_probability"));
        if (entryAsk is null || sizeUsd is null || shares is null || openSpot is null || edge is null || spread is null || probability is null)
        {
            return null;
        }

        return new PaperPosition(
            Slug: slug,
            Asset: Value("asset"),
            Side: Value("side"),
            EntryAsk: entryAsk.Value,
            EntryBid: PaperStats.Parse(Value("entry_bid")),
            EstimatedProbability: probability.Value,
            Edge: edge.Value,
            Spread: spread.Value,
            SizeUsd: sizeUsd.Value,
            Shares: shares.Value,
            OpenSpot: openSpot.Value,
            WindowStartUtc: windowStart,
            WindowEndUtc: windowEnd,
            EntryTimeUtc: entryTime,
            Reason: Value("reason")
        );
    }

    void Append(PaperPosition position, string eventType, double currentSpot, string outcome, double? pnl)
    {
        var values = new[]
        {
            eventType,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            position.Slug,
            position.Asset,
            position.Side,
            Format(position.EntryAsk),
            Format(position.EntryBid),
            Format(position.EstimatedProbability),
            Format(position.Edge),
            Format(position.Spread),
            Format(position.SizeUsd),
            Format(position.Shares),
            Format(position.OpenSpot),
            Format(currentSpot),
            position.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture),
            position.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture),
            outcome,
            Format(pnl),
            position.Reason,
        };

        File.AppendAllText(journalPath, string.Join(",", values.Select(CsvEscape)) + Environment.NewLine);
    }

    static string Format(double? value) => value is null ? "" : value.Value.ToString("0.########", CultureInfo.InvariantCulture);

    static string CsvEscape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

sealed class ShadowTracker
{
    readonly string journalPath;
    readonly Dictionary<string, ShadowPosition> openPositions = new(StringComparer.OrdinalIgnoreCase);

    public ShadowTracker(string journalPath)
    {
        this.journalPath = journalPath;
        EnsureJournal();
        LoadOpenPositions();
    }

    public IReadOnlyCollection<string> OpenAssets => openPositions.Values.Select(position => position.Asset).Distinct().ToList();

    public void OnRejectedDecision(SignalDecision decision, double? currentSpot, double? openSpot)
    {
        var shadow = decision.Shadow;
        var slug = shadow?.Market.MarketSlug;
        if (shadow is null
            || string.IsNullOrWhiteSpace(slug)
            || currentSpot is null
            || openSpot is null
            || shadow.Market.WindowStartUtc is null
            || shadow.Market.WindowEndUtc is null)
        {
            return;
        }

        var key = Key(slug, shadow.Side);
        if (openPositions.ContainsKey(key))
        {
            return;
        }

        var shares = shadow.SizeUsd / shadow.Ask;
        var position = new ShadowPosition(
            Slug: slug,
            Asset: shadow.Market.Asset,
            Side: shadow.Side,
            EntryAsk: shadow.Ask,
            EntryBid: shadow.Bid,
            EstimatedProbability: shadow.EstimatedProbability,
            Edge: shadow.Edge,
            Spread: shadow.Spread,
            SizeUsd: shadow.SizeUsd,
            Shares: shares,
            OpenSpot: openSpot.Value,
            WindowStartUtc: shadow.Market.WindowStartUtc.Value,
            WindowEndUtc: shadow.Market.WindowEndUtc.Value,
            EntryTimeUtc: DateTimeOffset.UtcNow,
            RejectionReason: shadow.RejectionReason,
            Reason: shadow.Reason
        );

        openPositions[key] = position;
        Append(position, eventType: "OPEN", currentSpot: currentSpot.Value, outcome: "", pnl: null);
    }

    public void SettleDue(SpotMomentum momentum)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, position) in openPositions.ToList())
        {
            if (now < position.WindowEndUtc.AddSeconds(10))
            {
                continue;
            }

            var currentSpot = momentum.LastPrice(position.Asset);
            if (currentSpot is null)
            {
                continue;
            }

            var upWon = currentSpot.Value >= position.OpenSpot;
            var outcome = upWon ? "YES" : "NO";
            var won = string.Equals(position.Side, outcome, StringComparison.OrdinalIgnoreCase);
            var payout = won ? position.Shares : 0.0;
            var pnl = payout - position.SizeUsd;

            Append(position, eventType: "CLOSE", currentSpot: currentSpot.Value, outcome: outcome, pnl: pnl);
            openPositions.Remove(key);
        }
    }

    void EnsureJournal()
    {
        var directory = Path.GetDirectoryName(journalPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(journalPath))
        {
            return;
        }

        File.AppendAllText(
            journalPath,
            "event_type,timestamp_utc,slug,asset,side,entry_ask,entry_bid,estimated_probability,edge,spread,size_usd,shares,open_spot,current_spot,window_start_utc,window_end_utc,outcome,pnl,rejection_reason,reason" + Environment.NewLine
        );
    }

    void LoadOpenPositions()
    {
        var lines = File.ReadAllLines(journalPath);
        if (lines.Length <= 1)
        {
            return;
        }

        var header = PaperStats.SplitCsv(lines[0]);
        var columns = header.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index);
        foreach (var line in lines.Skip(1))
        {
            var row = PaperStats.SplitCsv(line);
            if (row.Count < header.Count || !columns.ContainsKey("event_type") || !columns.ContainsKey("slug") || !columns.ContainsKey("side"))
            {
                continue;
            }

            var eventType = row[columns["event_type"]];
            var key = Key(row[columns["slug"]], row[columns["side"]]);
            if (eventType == "CLOSE")
            {
                openPositions.Remove(key);
                continue;
            }

            if (eventType != "OPEN")
            {
                continue;
            }

            var position = TryReadPosition(row, columns);
            if (position is not null)
            {
                openPositions[Key(position.Slug, position.Side)] = position;
            }
        }

        if (openPositions.Count > 0)
        {
            Console.WriteLine($"Loaded {openPositions.Count} open shadow position(s) from {journalPath}");
        }
    }

    static ShadowPosition? TryReadPosition(IReadOnlyList<string> row, Dictionary<string, int> columns)
    {
        string Value(string name) => columns.TryGetValue(name, out var index) && index < row.Count ? row[index] : "";
        var slug = Value("slug");
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(Value("window_start_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var windowStart)
            || !DateTimeOffset.TryParse(Value("window_end_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var windowEnd)
            || !DateTimeOffset.TryParse(Value("timestamp_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var entryTime))
        {
            return null;
        }

        var entryAsk = PaperStats.Parse(Value("entry_ask"));
        var sizeUsd = PaperStats.Parse(Value("size_usd"));
        var shares = PaperStats.Parse(Value("shares"));
        var openSpot = PaperStats.Parse(Value("open_spot"));
        var edge = PaperStats.Parse(Value("edge"));
        var spread = PaperStats.Parse(Value("spread"));
        var probability = PaperStats.Parse(Value("estimated_probability"));
        if (entryAsk is null || sizeUsd is null || shares is null || openSpot is null || edge is null || spread is null || probability is null)
        {
            return null;
        }

        return new ShadowPosition(
            Slug: slug,
            Asset: Value("asset"),
            Side: Value("side"),
            EntryAsk: entryAsk.Value,
            EntryBid: PaperStats.Parse(Value("entry_bid")),
            EstimatedProbability: probability.Value,
            Edge: edge.Value,
            Spread: spread.Value,
            SizeUsd: sizeUsd.Value,
            Shares: shares.Value,
            OpenSpot: openSpot.Value,
            WindowStartUtc: windowStart,
            WindowEndUtc: windowEnd,
            EntryTimeUtc: entryTime,
            RejectionReason: Value("rejection_reason"),
            Reason: Value("reason")
        );
    }

    void Append(ShadowPosition position, string eventType, double currentSpot, string outcome, double? pnl)
    {
        var values = new[]
        {
            eventType,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            position.Slug,
            position.Asset,
            position.Side,
            Format(position.EntryAsk),
            Format(position.EntryBid),
            Format(position.EstimatedProbability),
            Format(position.Edge),
            Format(position.Spread),
            Format(position.SizeUsd),
            Format(position.Shares),
            Format(position.OpenSpot),
            Format(currentSpot),
            position.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture),
            position.WindowEndUtc.ToString("O", CultureInfo.InvariantCulture),
            outcome,
            Format(pnl),
            position.RejectionReason,
            position.Reason,
        };

        File.AppendAllText(journalPath, string.Join(",", values.Select(CsvEscape)) + Environment.NewLine);
    }

    static string Key(string slug, string side) => $"{slug}|{side}";

    static string Format(double? value) => value is null ? "" : value.Value.ToString("0.########", CultureInfo.InvariantCulture);

    static string CsvEscape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

sealed class SnapshotRecorder
{
    readonly string journalPath;

    public SnapshotRecorder(string journalPath)
    {
        this.journalPath = journalPath;
        EnsureJournal();
    }

    public void Record(
        MarketCandidate market,
        string side,
        TopOfBook? book,
        PriceEstimate? estimate,
        double? estimatedProbability,
        bool isDirectional,
        SignalDecision decision,
        double? currentSpot,
        double? openSpot
    )
    {
        var now = DateTimeOffset.UtcNow;
        var tokenId = side.Equals("YES", StringComparison.OrdinalIgnoreCase) ? market.YesTokenId : market.NoTokenId ?? "";
        var secondsSinceStart = market.WindowStartUtc is null ? (double?)null : (now - market.WindowStartUtc.Value).TotalSeconds;
        var secondsToEnd = market.WindowEndUtc is null ? (double?)null : (market.WindowEndUtc.Value - now).TotalSeconds;
        var decisionSide = decision.Signal?.Side ?? decision.Shadow?.Side ?? "";
        var isSelectedSide = side.Equals(decisionSide, StringComparison.OrdinalIgnoreCase);
        var decisionLabel = isSelectedSide
            ? decision.Signal is not null ? "SIGNAL" : decision.Shadow is not null ? "SHADOW" : "REJECT"
            : "REJECT";
        var reason = isSelectedSide || string.IsNullOrWhiteSpace(decisionSide)
            ? decision.Reason
            : "not selected side";

        var values = new[]
        {
            now.ToString("O", CultureInfo.InvariantCulture),
            market.MarketSlug ?? "",
            market.Asset,
            side,
            tokenId,
            market.WindowStartUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            market.WindowEndUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "",
            Format(secondsSinceStart),
            Format(secondsToEnd),
            Format(currentSpot),
            Format(openSpot),
            Format(estimate?.DistanceBps),
            Format(estimate?.RecentMomentumBps),
            Format(estimatedProbability),
            Format(book?.Bid),
            Format(book?.Ask),
            Format(book?.BidSize),
            Format(book?.AskSize),
            Format(book?.Spread),
            isDirectional ? "true" : "false",
            decisionLabel,
            reason,
        };

        File.AppendAllText(journalPath, string.Join(",", values.Select(CsvEscape)) + Environment.NewLine);
    }

    void EnsureJournal()
    {
        var directory = Path.GetDirectoryName(journalPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(journalPath))
        {
            return;
        }

        File.AppendAllText(
            journalPath,
            "timestamp_utc,slug,asset,side,token_id,window_start_utc,window_end_utc,seconds_since_start,seconds_to_end,current_spot,open_spot,distance_bps,recent_momentum_bps,estimated_probability,bid,ask,bid_size,ask_size,spread,is_directional,decision,reason" + Environment.NewLine
        );
    }

    static string Format(double? value) => value is null ? "" : value.Value.ToString("0.########", CultureInfo.InvariantCulture);

    static string CsvEscape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

sealed class FirstSignalGate
{
    readonly bool enabled;
    readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

    public FirstSignalGate(bool enabled)
    {
        this.enabled = enabled;
    }

    public bool Enabled => enabled;

    public bool HasSeen(string? slug) => enabled && !string.IsNullOrWhiteSpace(slug) && seen.Contains(slug);

    public void Mark(string? slug)
    {
        if (!enabled || string.IsNullOrWhiteSpace(slug))
        {
            return;
        }

        seen.Add(slug);
    }
}

static class PaperStats
{
    public static void Print(string journalPath)
    {
        if (!File.Exists(journalPath))
        {
            Console.WriteLine($"No journal found at {journalPath}");
            return;
        }

        var lines = File.ReadAllLines(journalPath);
        if (lines.Length <= 1)
        {
            Console.WriteLine("Journal is empty.");
            return;
        }

        var header = SplitCsv(lines[0]);
        var columns = header.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index);
        var opened = 0;
        var closed = 0;
        var wins = 0;
        var pnl = 0.0;
        var staked = 0.0;
        var closedStake = 0.0;

        foreach (var line in lines.Skip(1))
        {
            var row = SplitCsv(line);
            if (row.Count < header.Count)
            {
                continue;
            }

            var eventType = row[columns["event_type"]];
            if (eventType == "OPEN")
            {
                opened++;
                staked += Parse(row[columns["size_usd"]]) ?? 0;
                continue;
            }

            if (eventType != "CLOSE")
            {
                continue;
            }

            closed++;
            var rowPnl = Parse(row[columns["pnl"]]) ?? 0;
            closedStake += Parse(row[columns["size_usd"]]) ?? 0;
            pnl += rowPnl;
            if (rowPnl > 0)
            {
                wins++;
            }
        }

        var open = Math.Max(0, opened - closed);
        var winRate = closed == 0 ? 0 : (double)wins / closed;
        var roi = staked <= 0 ? 0 : pnl / staked;
        var closedRoi = closedStake <= 0 ? 0 : pnl / closedStake;

        Console.WriteLine($"Journal: {journalPath}");
        Console.WriteLine($"Opened: {opened} | Closed: {closed} | Still open: {open}");
        Console.WriteLine($"Wins: {wins} | Win rate: {winRate:P1}");
        Console.WriteLine($"Paper PnL: ${pnl:0.00} | ROI on closed stake: {closedRoi:P1} | ROI on opened stake: {roi:P1}");
    }

    public static double? Parse(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    public static List<string> SplitCsv(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i++;
                continue;
            }

            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (ch == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        values.Add(current.ToString());
        return values;
    }
}

static class ShadowStats
{
    static readonly double[] ProbabilityThresholds = { 0.52, 0.55, 0.58, 0.60 };
    static readonly double[] AskCeilings = { 0.60, 0.65, 0.70, 0.75, 0.80, 0.90 };
    static readonly string[] Sides = { "NO", "YES" };

    public static void Print(string journalPath)
    {
        if (!File.Exists(journalPath))
        {
            Console.WriteLine($"No shadow journal found at {journalPath}");
            return;
        }

        var lines = File.ReadAllLines(journalPath);
        if (lines.Length <= 1)
        {
            Console.WriteLine("Shadow journal is empty.");
            return;
        }

        var header = PaperStats.SplitCsv(lines[0]);
        var columns = header.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index);
        var rows = lines.Skip(1)
            .Select(PaperStats.SplitCsv)
            .Where(row => row.Count >= header.Count)
            .ToList();
        var closedRows = rows
            .Where(row => row[columns["event_type"]] == "CLOSE")
            .ToList();
        var openCount = rows.Count(row => row[columns["event_type"]] == "OPEN");

        Console.WriteLine($"Shadow journal: {journalPath}");
        Console.WriteLine($"Opened shadow candidates: {openCount} | Closed: {closedRows.Count} | Still open: {Math.Max(0, openCount - closedRows.Count)}");
        PrintGroup("All shadow", closedRows, columns);
        Console.WriteLine();

        Console.WriteLine("By side:");
        foreach (var side in Sides)
        {
            PrintGroup($"  {side}", FilterSide(closedRows, columns, side), columns);
        }
        Console.WriteLine();

        Console.WriteLine("Probability thresholds:");
        foreach (var threshold in ProbabilityThresholds)
        {
            var thresholdRows = closedRows
                .Where(row => (PaperStats.Parse(row[columns["estimated_probability"]]) ?? 0) >= threshold)
                .ToList();
            PrintGroup($"p >= {threshold:P0}", thresholdRows, columns);
        }
        Console.WriteLine();

        Console.WriteLine("Side probability thresholds:");
        foreach (var side in Sides)
        {
            foreach (var threshold in ProbabilityThresholds)
            {
                var sideRows = FilterSide(closedRows, columns, side)
                    .Where(row => (PaperStats.Parse(row[columns["estimated_probability"]]) ?? 0) >= threshold)
                    .ToList();
                PrintGroup($"  {side} p >= {threshold:P0}", sideRows, columns);
            }
        }
        Console.WriteLine();

        Console.WriteLine("Side ask ceilings:");
        foreach (var side in Sides)
        {
            foreach (var askCeiling in AskCeilings)
            {
                var sideRows = FilterSide(closedRows, columns, side)
                    .Where(row => (PaperStats.Parse(row[columns["entry_ask"]]) ?? 999) <= askCeiling)
                    .ToList();
                PrintGroup($"  {side} ask <= {askCeiling:0.00}", sideRows, columns);
            }
        }
        Console.WriteLine();

        var firstRows = FirstRowsByMarket(closedRows, columns);
        Console.WriteLine("First signal per market:");
        PrintGroup("  first all", firstRows, columns);
        foreach (var side in Sides)
        {
            PrintGroup($"  first {side}", FilterSide(firstRows, columns, side), columns);
        }
        Console.WriteLine();

        var byReason = closedRows
            .GroupBy(row => row[columns["rejection_reason"]])
            .OrderByDescending(group => group.Count())
            .Take(6);

        Console.WriteLine("Top rejection reasons:");
        foreach (var group in byReason)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }
    }

    static List<List<string>> FilterSide(IEnumerable<List<string>> rows, Dictionary<string, int> columns, string side) =>
        rows.Where(row => row[columns["side"]].Equals(side, StringComparison.OrdinalIgnoreCase)).ToList();

    static List<List<string>> FirstRowsByMarket(IEnumerable<List<string>> rows, Dictionary<string, int> columns) =>
        rows.GroupBy(row => row[columns["slug"]])
            .Select(group => group.First())
            .ToList();

    static void PrintGroup(string label, IReadOnlyList<List<string>> rows, Dictionary<string, int> columns)
    {
        if (rows.Count == 0)
        {
            Console.WriteLine($"{label}: no closed candidates");
            return;
        }

        var wins = 0;
        var pnl = 0.0;
        var stake = 0.0;
        foreach (var row in rows)
        {
            var rowPnl = PaperStats.Parse(row[columns["pnl"]]) ?? 0;
            pnl += rowPnl;
            stake += PaperStats.Parse(row[columns["size_usd"]]) ?? 0;
            if (rowPnl > 0)
            {
                wins++;
            }
        }

        var winRate = (double)wins / rows.Count;
        var roi = stake <= 0 ? 0 : pnl / stake;
        Console.WriteLine($"{label}: n={rows.Count} wins={wins} winrate={winRate:P1} pnl=${pnl:0.00} roi={roi:P1}");
    }
}

record SnapshotEntry(
    DateTimeOffset TimestampUtc,
    string Slug,
    string Asset,
    string Side,
    DateTimeOffset? WindowEndUtc,
    double? SecondsSinceStart,
    double? SecondsToEnd,
    double? CurrentSpot,
    double? OpenSpot,
    double? EstimatedProbability,
    double? Ask,
    double AskSize,
    double? Spread,
    bool IsDirectional
);

record ReplayPosition(SnapshotEntry Entry, string Outcome, double Pnl, double Stake);

record ReplayResult(
    BotConfig Config,
    IReadOnlyList<ReplayPosition> Positions,
    int Wins,
    double Pnl,
    double Stake
)
{
    public int Count => Positions.Count;
    public double WinRate => Count == 0 ? 0 : (double)Wins / Count;
    public double Roi => Stake <= 0 ? 0 : Pnl / Stake;
}

static class SnapshotReplay
{
    static readonly double[] ProbabilityGrid = { 0.52, 0.55, 0.58, 0.60, 0.62 };
    static readonly double[] AskGrid = { 0.55, 0.60, 0.65, 0.70, 0.75, 0.80 };
    static readonly double[] MaxEntryAgeGrid = { 45, 90, 150, 240 };
    static readonly double[] EdgeGrid = { -0.20, -0.10, 0.0 };

    public static void Print(string snapshotJournalPath, BotConfig config)
    {
        if (!File.Exists(snapshotJournalPath))
        {
            Console.WriteLine($"No snapshot journal found at {snapshotJournalPath}");
            return;
        }

        var snapshots = ReadSnapshots(snapshotJournalPath);
        if (snapshots.Count == 0)
        {
            Console.WriteLine("Snapshot journal is empty.");
            return;
        }

        var positions = ReplayPositions(snapshots, config);

        Console.WriteLine($"Snapshot replay: {snapshotJournalPath}");
        Console.WriteLine("Outcome source: approximate from last recorded spot per market, not official Polymarket settlement.");
        PrintPositions("All replay", positions);
        foreach (var side in new[] { "NO", "YES" })
        {
            PrintPositions($"  {side}", positions.Where(position => position.Entry.Side.Equals(side, StringComparison.OrdinalIgnoreCase)).ToList());
        }
    }

    public static void Analyze(string snapshotJournalPath, BotConfig baseConfig)
    {
        if (!File.Exists(snapshotJournalPath))
        {
            Console.WriteLine($"No snapshot journal found at {snapshotJournalPath}");
            return;
        }

        var snapshots = ReadSnapshots(snapshotJournalPath);
        if (snapshots.Count == 0)
        {
            Console.WriteLine("Snapshot journal is empty.");
            return;
        }

        var marketCount = snapshots.Select(row => row.Slug).Where(slug => !string.IsNullOrWhiteSpace(slug)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Console.WriteLine($"Snapshot analyze: {snapshotJournalPath}");
        Console.WriteLine($"Snapshots: {snapshots.Count} | Markets: {marketCount}");
        Console.WriteLine("Outcome source: approximate from last recorded spot per market, not official Polymarket settlement.");
        Console.WriteLine();

        var baseline = BuildResult(baseConfig, snapshots);
        PrintResult("Baseline config", baseline);
        Console.WriteLine();

        var results = BuildCandidateConfigs(baseConfig)
            .Select(config => BuildResult(config, snapshots))
            .Where(result => result.Count >= 5)
            .OrderByDescending(result => result.Roi)
            .ThenByDescending(result => result.Count)
            .ToList();

        PrintTop("Top all configs, min 5 trades", results, 12);
        Console.WriteLine();
        PrintTop("Top YES-only configs, min 5 trades", results.Where(result => result.Config.SideFilter == "YES").ToList(), 8);
        Console.WriteLine();
        PrintTop("Top NO-only configs, min 5 trades", results.Where(result => result.Config.SideFilter == "NO").ToList(), 8);
        Console.WriteLine();
        PrintTop("Top balanced ANY configs, min 8 trades", results.Where(result => result.Config.SideFilter == "ANY" && result.Count >= 8).ToList(), 8);
        Console.WriteLine();
        PrintTrainTest(snapshots, baseConfig);
    }

    static IEnumerable<BotConfig> BuildCandidateConfigs(BotConfig baseConfig)
    {
        foreach (var sideFilter in new[] { "YES", "NO", "ANY" })
        {
            foreach (var firstSignalOnly in new[] { true, false })
            {
                foreach (var maxEntryAge in MaxEntryAgeGrid)
                {
                    foreach (var edge in EdgeGrid)
                    {
                        if (sideFilter == "YES")
                        {
                            foreach (var yesProbability in ProbabilityGrid)
                            foreach (var yesAsk in AskGrid)
                            {
                                yield return baseConfig with
                                {
                                    SideFilter = "YES",
                                    FirstSignalOnly = firstSignalOnly,
                                    MaxEntrySecondsAfterStart = maxEntryAge,
                                    MinYesProbability = yesProbability,
                                    MinNoProbability = 0.99,
                                    MinYesEdge = edge,
                                    MinNoEdge = 0.99,
                                    MaxYesAsk = yesAsk,
                                    MaxNoAsk = 0.01,
                                };
                            }
                        }
                        else if (sideFilter == "NO")
                        {
                            foreach (var noProbability in ProbabilityGrid)
                            foreach (var noAsk in AskGrid)
                            {
                                yield return baseConfig with
                                {
                                    SideFilter = "NO",
                                    FirstSignalOnly = firstSignalOnly,
                                    MaxEntrySecondsAfterStart = maxEntryAge,
                                    MinYesProbability = 0.99,
                                    MinNoProbability = noProbability,
                                    MinYesEdge = 0.99,
                                    MinNoEdge = edge,
                                    MaxYesAsk = 0.01,
                                    MaxNoAsk = noAsk,
                                };
                            }
                        }
                        else
                        {
                            foreach (var yesProbability in new[] { 0.58, 0.60, 0.62 })
                            foreach (var noProbability in new[] { 0.55, 0.58, 0.60 })
                            foreach (var yesAsk in new[] { 0.65, 0.70, 0.75 })
                            foreach (var noAsk in new[] { 0.60, 0.65, 0.70 })
                            {
                                yield return baseConfig with
                                {
                                    SideFilter = "ANY",
                                    FirstSignalOnly = firstSignalOnly,
                                    MaxEntrySecondsAfterStart = maxEntryAge,
                                    MinYesProbability = yesProbability,
                                    MinNoProbability = noProbability,
                                    MinYesEdge = edge,
                                    MinNoEdge = edge,
                                    MaxYesAsk = yesAsk,
                                    MaxNoAsk = noAsk,
                                };
                            }
                        }
                    }
                }
            }
        }
    }

    static ReplayResult BuildResult(BotConfig config, IReadOnlyList<SnapshotEntry> snapshots)
    {
        var positions = ReplayPositions(snapshots, config);
        var wins = positions.Count(position => position.Pnl > 0);
        var pnl = positions.Sum(position => position.Pnl);
        var stake = positions.Sum(position => position.Stake);
        return new ReplayResult(config, positions, wins, pnl, stake);
    }

    static List<ReplayPosition> ReplayPositions(IReadOnlyList<SnapshotEntry> snapshots, BotConfig config)
    {
        var outcomes = BuildApproxOutcomes(snapshots);
        var positions = new List<ReplayPosition>();
        var enteredSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var consumedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in snapshots.OrderBy(row => row.TimestampUtc))
        {
            if (string.IsNullOrWhiteSpace(row.Slug) || enteredSlugs.Contains(row.Slug))
            {
                continue;
            }

            if (!PassesReplay(row, config, out var actionable))
            {
                if (config.FirstSignalOnly && actionable)
                {
                    consumedSlugs.Add(row.Slug);
                }

                continue;
            }

            if (config.FirstSignalOnly && consumedSlugs.Contains(row.Slug))
            {
                continue;
            }

            if (!outcomes.TryGetValue(row.Slug, out var outcome))
            {
                continue;
            }

            var stake = Math.Min(config.MaxPosition, config.Bankroll * 0.04);
            var shares = stake / row.Ask!.Value;
            var won = row.Side.Equals(outcome, StringComparison.OrdinalIgnoreCase);
            var pnl = (won ? shares : 0.0) - stake;
            positions.Add(new ReplayPosition(row, outcome, pnl, stake));
            enteredSlugs.Add(row.Slug);

            if (config.FirstSignalOnly)
            {
                consumedSlugs.Add(row.Slug);
            }
        }

        return positions;
    }

    static bool PassesReplay(SnapshotEntry row, BotConfig config, out bool actionable)
    {
        actionable = false;
        if (!row.IsDirectional || row.Ask is null || row.Spread is null)
        {
            return false;
        }

        actionable = true;

        if (!SideEnabled(config, row.Side))
        {
            return false;
        }

        if (row.SecondsSinceStart is null || row.SecondsSinceStart < config.MinEntrySecondsAfterStart)
        {
            return false;
        }

        if (config.MaxEntrySecondsAfterStart > 0 && row.SecondsSinceStart > config.MaxEntrySecondsAfterStart)
        {
            return false;
        }

        if (row.SecondsToEnd is null || row.SecondsToEnd < config.MaxEntrySecondsBeforeEnd)
        {
            return false;
        }

        if (row.EstimatedProbability is null || row.EstimatedProbability < MinProbabilityForSide(config, row.Side))
        {
            return false;
        }

        var ask = row.Ask.Value;
        var spread = row.Spread.Value;
        var estimatedProbability = row.EstimatedProbability.Value;

        if (ask <= 0 || ask > MaxAskForSide(config, row.Side))
        {
            return false;
        }

        if (row.AskSize < config.MinTopSize || spread > config.MaxSpread)
        {
            return false;
        }

        var edge = estimatedProbability - ask;
        return edge >= MinEdgeForSide(config, row.Side);
    }

    static Dictionary<string, string> BuildApproxOutcomes(IEnumerable<SnapshotEntry> snapshots)
    {
        var outcomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in snapshots.Where(row => !string.IsNullOrWhiteSpace(row.Slug)).GroupBy(row => row.Slug))
        {
            var ordered = group.OrderBy(row => row.TimestampUtc).ToList();
            var openSpot = ordered.Select(row => row.OpenSpot).FirstOrDefault(value => value is not null);
            var closeSpot = ordered.Select(row => row.CurrentSpot).LastOrDefault(value => value is not null);
            if (openSpot is null || closeSpot is null)
            {
                continue;
            }

            outcomes[group.Key] = closeSpot.Value >= openSpot.Value ? "YES" : "NO";
        }

        return outcomes;
    }

    static List<SnapshotEntry> ReadSnapshots(string snapshotJournalPath)
    {
        var lines = File.ReadAllLines(snapshotJournalPath);
        if (lines.Length <= 1)
        {
            return new List<SnapshotEntry>();
        }

        var header = PaperStats.SplitCsv(lines[0]);
        var columns = header.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index);
        var rows = new List<SnapshotEntry>();
        foreach (var line in lines.Skip(1))
        {
            var row = PaperStats.SplitCsv(line);
            if (row.Count < header.Count)
            {
                continue;
            }

            string Value(string name) => columns.TryGetValue(name, out var index) && index < row.Count ? row[index] : "";
            if (!DateTimeOffset.TryParse(Value("timestamp_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
            {
                continue;
            }

            DateTimeOffset? windowEnd = DateTimeOffset.TryParse(Value("window_end_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedEnd)
                ? parsedEnd
                : null;

            rows.Add(new SnapshotEntry(
                TimestampUtc: timestamp,
                Slug: Value("slug"),
                Asset: Value("asset"),
                Side: Value("side"),
                WindowEndUtc: windowEnd,
                SecondsSinceStart: PaperStats.Parse(Value("seconds_since_start")),
                SecondsToEnd: PaperStats.Parse(Value("seconds_to_end")),
                CurrentSpot: PaperStats.Parse(Value("current_spot")),
                OpenSpot: PaperStats.Parse(Value("open_spot")),
                EstimatedProbability: PaperStats.Parse(Value("estimated_probability")),
                Ask: PaperStats.Parse(Value("ask")),
                AskSize: PaperStats.Parse(Value("ask_size")) ?? 0,
                Spread: PaperStats.Parse(Value("spread")),
                IsDirectional: Value("is_directional").Equals("true", StringComparison.OrdinalIgnoreCase)
            ));
        }

        return rows;
    }

    static void PrintPositions(string label, IReadOnlyList<ReplayPosition> positions)
    {
        if (positions.Count == 0)
        {
            Console.WriteLine($"{label}: no replay trades");
            return;
        }

        var wins = positions.Count(position => position.Pnl > 0);
        var pnl = positions.Sum(position => position.Pnl);
        var stake = positions.Sum(position => position.Stake);
        Console.WriteLine($"{label}: n={positions.Count} wins={wins} winrate={(double)wins / positions.Count:P1} pnl=${pnl:0.00} roi={(stake <= 0 ? 0 : pnl / stake):P1}");
    }

    static void PrintResult(string label, ReplayResult result)
    {
        Console.WriteLine($"{label}: n={result.Count} wins={result.Wins} winrate={result.WinRate:P1} pnl=${result.Pnl:0.00} roi={result.Roi:P1} | {Describe(result.Config)}");
    }

    static void PrintTop(string label, IReadOnlyList<ReplayResult> results, int limit)
    {
        Console.WriteLine(label + ":");
        if (results.Count == 0)
        {
            Console.WriteLine("  no configs");
            return;
        }

        foreach (var result in results.Take(limit))
        {
            Console.WriteLine($"  n={result.Count,3} wins={result.Wins,3} wr={result.WinRate,7:P1} pnl=${result.Pnl,7:0.00} roi={result.Roi,7:P1} | {Describe(result.Config)}");
        }
    }

    static void PrintTrainTest(IReadOnlyList<SnapshotEntry> snapshots, BotConfig baseConfig)
    {
        var orderedSlugs = snapshots
            .Where(row => !string.IsNullOrWhiteSpace(row.Slug))
            .GroupBy(row => row.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Slug = group.Key, FirstSeen = group.Min(row => row.TimestampUtc) })
            .OrderBy(item => item.FirstSeen)
            .ToList();

        if (orderedSlugs.Count < 10)
        {
            Console.WriteLine("Train/test validation: not enough markets.");
            return;
        }

        var split = orderedSlugs.Count / 2;
        var trainSlugs = orderedSlugs.Take(split).Select(item => item.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var testSlugs = orderedSlugs.Skip(split).Select(item => item.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trainSnapshots = snapshots.Where(row => trainSlugs.Contains(row.Slug)).ToList();
        var testSnapshots = snapshots.Where(row => testSlugs.Contains(row.Slug)).ToList();

        var configs = BuildCandidateConfigs(baseConfig).ToList();
        var trainResults = configs
            .Select(config => BuildResult(config, trainSnapshots))
            .Where(result => result.Count >= 4)
            .OrderByDescending(result => result.Roi)
            .ThenByDescending(result => result.Count)
            .Take(10)
            .ToList();

        Console.WriteLine("Chronological train/test validation:");
        Console.WriteLine($"  train markets={trainSlugs.Count} | test markets={testSlugs.Count}");
        if (trainResults.Count == 0)
        {
            Console.WriteLine("  no train configs");
            return;
        }

        foreach (var train in trainResults)
        {
            var test = BuildResult(train.Config, testSnapshots);
            Console.WriteLine(
                $"  TRAIN n={train.Count,3} roi={train.Roi,7:P1} pnl=${train.Pnl,7:0.00} "
                + $"| TEST n={test.Count,3} roi={test.Roi,7:P1} pnl=${test.Pnl,7:0.00} "
                + $"| {Describe(train.Config)}"
            );
        }
    }

    static string Describe(BotConfig config) =>
        $"side={config.SideFilter} first={config.FirstSignalOnly} maxAge={config.MaxEntrySecondsAfterStart:0}s "
        + $"pY={config.MinYesProbability:0.00} pN={config.MinNoProbability:0.00} "
        + $"askY<={config.MaxYesAsk:0.00} askN<={config.MaxNoAsk:0.00} "
        + $"edgeY>={config.MinYesEdge:0.00} edgeN>={config.MinNoEdge:0.00}";

    static bool SideEnabled(BotConfig config, string side) =>
        config.SideFilter.Equals("ANY", StringComparison.OrdinalIgnoreCase)
        || config.SideFilter.Equals(side, StringComparison.OrdinalIgnoreCase);

    static double MinProbabilityForSide(BotConfig config, string side) =>
        side.Equals("YES", StringComparison.OrdinalIgnoreCase) ? config.MinYesProbability : config.MinNoProbability;

    static double MinEdgeForSide(BotConfig config, string side) =>
        side.Equals("YES", StringComparison.OrdinalIgnoreCase) ? config.MinYesEdge : config.MinNoEdge;

    static double MaxAskForSide(BotConfig config, string side) =>
        side.Equals("YES", StringComparison.OrdinalIgnoreCase) ? config.MaxYesAsk : config.MaxNoAsk;
}

static class Runner
{
    public static async Task RunOnce(
        PolymarketClient polymarket,
        SpotPriceClient spot,
        PriceLagStrategy strategy,
        SpotMomentum momentum,
        int limit,
        double warmupSeconds,
        PaperTrader? paperTrader,
        ShadowTracker shadowTracker,
        SnapshotRecorder snapshotRecorder,
        FirstSignalGate firstSignalGate,
        bool verbose
    )
    {
        if (verbose)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] Cycle start");
        }

        List<MarketCandidate> candidates;
        try
        {
            candidates = await polymarket.FindCryptoCandidates(limit);
        }
        catch (Exception exc)
        {
            Console.Error.WriteLine($"Polymarket fetch failed: {exc.Message}");
            return;
        }

        var assets = candidates
            .Select(c => c.Asset)
            .Concat(paperTrader?.OpenAssets ?? Array.Empty<string>())
            .Concat(shadowTracker.OpenAssets)
            .Distinct()
            .Order()
            .ToList();
        await CollectSpotSamples(spot, momentum, assets);
        if (warmupSeconds > 0 && assets.Any(asset => momentum.Count(asset) < 2))
        {
            Console.WriteLine($"Collecting second spot sample in {warmupSeconds:0.0}s...");
            await Task.Delay(TimeSpan.FromSeconds(warmupSeconds));
            await CollectSpotSamples(spot, momentum, assets);
        }

        paperTrader?.SettleDue(momentum);
        shadowTracker.SettleDue(momentum);

        if (candidates.Count == 0)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] No crypto short-window candidates found.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] Scanned {candidates.Count} candidate markets.");

        var printed = 0;
        var rejectionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var yesBook = await SafeBook(polymarket, candidate.YesTokenId, verbose);
            var noBook = candidate.NoTokenId is null ? null : await SafeBook(polymarket, candidate.NoTokenId, verbose);
            var openPrice = candidate.WindowStartUtc is null
                ? null
                : await SafeOpenPrice(spot, candidate);
            var currentSpot = momentum.LastPrice(candidate.Asset);
            var estimate = strategy.Estimate(candidate.Asset, momentum, openPrice);
            var decision = strategy.EvaluateDecision(candidate, yesBook, noBook, momentum, openPrice);
            var actionableSide = decision.Signal?.Side ?? decision.Shadow?.Side;
            if (!string.IsNullOrWhiteSpace(actionableSide))
            {
                if (firstSignalGate.HasSeen(candidate.MarketSlug))
                {
                    decision = BlockByFirstSignal(decision);
                }
                else
                {
                    firstSignalGate.Mark(candidate.MarketSlug);
                }
            }

            snapshotRecorder.Record(
                candidate,
                "YES",
                yesBook,
                estimate,
                estimate?.ProbabilityUp,
                strategy.IsDirectionalSide(estimate, "YES"),
                decision,
                currentSpot,
                openPrice
            );
            snapshotRecorder.Record(
                candidate,
                "NO",
                noBook,
                estimate,
                estimate is null ? null : 1.0 - estimate.ProbabilityUp,
                strategy.IsDirectionalSide(estimate, "NO"),
                decision,
                currentSpot,
                openPrice
            );

            var signal = decision.Signal;
            if (signal is null)
            {
                rejectionCounts[decision.Reason] = rejectionCounts.TryGetValue(decision.Reason, out var count) ? count + 1 : 1;
                if (verbose)
                {
                    Console.WriteLine($"[SKIP] {candidate.MarketSlug ?? candidate.MarketTitle}: {decision.Reason}");
                }
                shadowTracker.OnRejectedDecision(decision, currentSpot, openPrice);
                continue;
            }

            printed++;
            PrintSignal(signal);
            paperTrader?.OnSignal(signal, currentSpot, openPrice);
        }

        if (printed == 0)
        {
            Console.WriteLine("No paper signal passed the risk filters yet.");
            if (rejectionCounts.Count > 0)
            {
                Console.WriteLine("Reject reasons: " + string.Join("; ", rejectionCounts.Select(kv => $"{kv.Key} x{kv.Value}")));
            }
        }

        Console.Out.Flush();
    }

    static SignalDecision BlockByFirstSignal(SignalDecision decision)
    {
        const string rejection = "not first actionable signal for market";
        if (decision.Signal is not null)
        {
            var signal = decision.Signal;
            return SignalDecision.Rejected(
                rejection,
                new ShadowSignal(
                    Market: signal.Market,
                    Side: signal.Side,
                    TokenId: signal.TokenId,
                    Ask: signal.Ask,
                    Bid: signal.Bid,
                    EstimatedProbability: signal.EstimatedProbability,
                    Edge: signal.Edge,
                    Spread: signal.Spread,
                    SizeUsd: signal.SizeUsd,
                    RejectionReason: rejection,
                    Reason: signal.Reason
                )
            );
        }

        if (decision.Shadow is not null)
        {
            return SignalDecision.Rejected(rejection, decision.Shadow with { RejectionReason = rejection });
        }

        return SignalDecision.Rejected(rejection);
    }

    static async Task CollectSpotSamples(SpotPriceClient spot, SpotMomentum momentum, IReadOnlyCollection<string> assets)
    {
        foreach (var asset in assets)
        {
            try
            {
                var sample = await spot.GetPrice(asset);
                if (sample is not null)
                {
                    momentum.Add(sample);
                }
            }
            catch (Exception exc)
            {
                Console.Error.WriteLine($"Spot fetch failed for {asset}: {exc.Message}");
            }
        }
    }

    static async Task<TopOfBook?> SafeBook(PolymarketClient polymarket, string tokenId, bool verbose)
    {
        try
        {
            return await polymarket.OrderBook(tokenId);
        }
        catch (Exception exc)
        {
            if (verbose)
            {
                Console.Error.WriteLine($"Order book fetch failed for token {Short(tokenId)}: {exc.Message}");
            }
            return null;
        }
    }

    static async Task<double?> SafeOpenPrice(SpotPriceClient spot, MarketCandidate candidate)
    {
        if (candidate.WindowStartUtc is null)
        {
            return null;
        }

        try
        {
            return await spot.GetReferenceOpenPrice(candidate.Asset, candidate.WindowStartUtc.Value);
        }
        catch (Exception exc)
        {
            Console.Error.WriteLine($"Open price fetch failed for {candidate.Asset} {candidate.MarketSlug}: {exc.Message}");
            return null;
        }
    }

    static void PrintSignal(Signal signal)
    {
        var title = string.IsNullOrWhiteSpace(signal.Market.MarketTitle)
            ? signal.Market.EventTitle
            : signal.Market.MarketTitle;

        Console.WriteLine();
        Console.WriteLine($"[PAPER] {signal.Side} {signal.Market.Asset} | size ${signal.SizeUsd:0.00}");
        Console.WriteLine($"Market: {title}");
        Console.WriteLine(
            $"Ask/Bid: {signal.Ask:0.000}/{(signal.Bid?.ToString("0.000", CultureInfo.InvariantCulture) ?? "n/a")} " +
            $"| spread {signal.Spread:0.000} | edge {signal.Edge:P1}"
        );
        Console.WriteLine($"Estimated p: {signal.EstimatedProbability:P1}");
        Console.WriteLine($"Reason: {signal.Reason}");
        Console.WriteLine($"Slug: {signal.Market.MarketSlug ?? "no-slug"}");
    }

    static string Short(string value) => value.Length <= 10 ? value : $"{value[..10]}...";
}

sealed class PolymarketClient
{
    readonly BotConfig config;
    readonly HttpClient http;

    public PolymarketClient(BotConfig config, HttpClient http)
    {
        this.config = config;
        this.http = http;
    }

    static readonly Dictionary<string, string[]> CryptoWords = new()
    {
        ["BTC"] = new[] { "btc", "bitcoin" },
        ["ETH"] = new[] { "eth", "ethereum", "ether" },
        ["SOL"] = new[] { "sol", "solana" },
        ["XRP"] = new[] { "xrp", "ripple" },
        ["BNB"] = new[] { "bnb", "binance" },
    };

    static readonly string[] ShortWindowHints =
    {
        "up or down",
        "higher or lower",
        "above",
        "below",
        "5m",
        "5 min",
        "15m",
        "15 min",
        "hour",
    };

    public async Task<List<MarketCandidate>> FindCryptoCandidates(int limit)
    {
        var candidates = new List<MarketCandidate>();

        using var root = await GetJson(
            $"{config.GammaBaseUrl}/events?active=true&closed=false&order=volume_24hr&ascending=false&limit={limit}"
        );

        foreach (var ev in RootArray(root.RootElement))
        {
            AddCandidatesFromEvent(candidates, ev);
        }

        foreach (var slug in await CryptoPageEventSlugs(limit))
        {
            try
            {
                using var ev = await GetJson($"{config.GammaBaseUrl}/events/slug/{Uri.EscapeDataString(slug)}");
                AddCandidatesFromEvent(candidates, ev.RootElement);
            }
            catch (HttpRequestException exc) when (exc.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // The crypto page can briefly contain stale rolling-market slugs.
            }
        }

        return candidates
            .GroupBy(candidate => candidate.YesTokenId)
            .Select(group => group.First())
            .Take(limit)
            .ToList();
    }

    public async Task<TopOfBook?> OrderBook(string tokenId)
    {
        using var root = await GetJson($"{config.ClobBaseUrl}/book?token_id={Uri.EscapeDataString(tokenId)}");
        var data = root.RootElement;
        var bids = Levels(data, "bids");
        var asks = Levels(data, "asks");
        var bestBid = bids.Count == 0 ? null : bids.MaxBy(level => level.Price);
        var bestAsk = asks.Count == 0 ? null : asks.MinBy(level => level.Price);
        var spread = bestBid is not null && bestAsk is not null ? bestAsk.Price - bestBid.Price : (double?)null;

        return new TopOfBook(
            TokenId: tokenId,
            Bid: bestBid?.Price,
            BidSize: bestBid?.Size ?? 0,
            Ask: bestAsk?.Price,
            AskSize: bestAsk?.Size ?? 0,
            Spread: spread
        );
    }

    MarketCandidate? CandidateFromMarket(string eventTitle, JsonElement market)
    {
        var title = StringProp(market, "question") ?? StringProp(market, "title") ?? "";
        var combined = $"{eventTitle} {title}".ToLowerInvariant();
        var asset = DetectAsset(combined);
        if (asset is null || !combined.Contains("up or down", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var window = ParseUpDownWindow(title);
        if (window is null)
        {
            return null;
        }

        var durationMinutes = (window.Value.EndUtc - window.Value.StartUtc).TotalMinutes;
        if (durationMinutes > config.MaxMarketMinutes)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < window.Value.StartUtc.AddSeconds(config.MinEntrySecondsAfterStart)
            || now > window.Value.EndUtc.AddSeconds(-config.MaxEntrySecondsBeforeEnd))
        {
            return null;
        }

        var tokenIds = JsonishArray(market, "clobTokenIds").Select(ValueString).Where(s => s.Length > 0).ToList();
        if (tokenIds.Count == 0)
        {
            return null;
        }

        var outcomes = JsonishArray(market, "outcomes").Select(v => ValueString(v).ToLowerInvariant()).ToList();
        var yesIndex = OutcomeIndex(outcomes, new[] { "yes", "up", "higher", "above" });
        var noIndex = OutcomeIndex(outcomes, new[] { "no", "down", "lower", "below" });

        var yesToken = yesIndex is not null && yesIndex < tokenIds.Count ? tokenIds[yesIndex.Value] : tokenIds[0];
        var noToken = noIndex is not null && noIndex < tokenIds.Count ? tokenIds[noIndex.Value] : null;

        return new MarketCandidate(
            EventTitle: eventTitle,
            MarketTitle: title,
            MarketSlug: StringProp(market, "slug"),
            Asset: asset,
            YesTokenId: yesToken,
            NoTokenId: noToken,
            EndDate: StringProp(market, "endDate") ?? StringProp(market, "end_date_iso") ?? StringProp(market, "endDateIso"),
            WindowStartUtc: window.Value.StartUtc,
            WindowEndUtc: window.Value.EndUtc
        );
    }

    static (DateTimeOffset StartUtc, DateTimeOffset EndUtc)? ParseUpDownWindow(string title)
    {
        var match = Regex.Match(
            title,
            @"-\s*(?<month>[A-Za-z]+)\s+(?<day>\d{1,2}),\s*(?<start>\d{1,2}(?::\d{2})?\s*[AP]M)\s*-\s*(?<end>\d{1,2}(?::\d{2})?\s*[AP]M)\s+ET",
            RegexOptions.IgnoreCase
        );
        if (!match.Success)
        {
            return null;
        }

        var year = DateTimeOffset.UtcNow.Year;
        var startLocal = ParseEasternLocal(match.Groups["month"].Value, match.Groups["day"].Value, year, match.Groups["start"].Value);
        var endLocal = ParseEasternLocal(match.Groups["month"].Value, match.Groups["day"].Value, year, match.Groups["end"].Value);
        if (startLocal is null || endLocal is null)
        {
            return null;
        }

        if (endLocal.Value <= startLocal.Value)
        {
            endLocal = endLocal.Value.AddDays(1);
        }

        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal.Value, eastern);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal.Value, eastern);
        return (new DateTimeOffset(startUtc), new DateTimeOffset(endUtc));
    }

    static DateTime? ParseEasternLocal(string month, string day, int year, string time)
    {
        var normalized = Regex.Replace(time, @"\s+", "").ToUpperInvariant();
        var formats = new[] { "MMMM d yyyy h:mmtt", "MMMM d yyyy htt", "MMM d yyyy h:mmtt", "MMM d yyyy htt" };
        return DateTime.TryParseExact(
            $"{month} {day} {year} {normalized}",
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed
        )
            ? parsed
            : null;
    }

    void AddCandidatesFromEvent(List<MarketCandidate> candidates, JsonElement ev)
    {
        var eventTitle = StringProp(ev, "title") ?? StringProp(ev, "question") ?? "";
        if (!ev.TryGetProperty("markets", out var markets) || markets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var market in markets.EnumerateArray())
        {
            var candidate = CandidateFromMarket(eventTitle, market);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }
    }

    async Task<List<string>> CryptoPageEventSlugs(int limit)
    {
        var html = await http.GetStringAsync("https://polymarket.com/crypto");
        return Regex.Matches(html, "/event/([^\\\"?#<\\s]+)")
            .Select(match => match.Groups[1].Value.Split('/')[0])
            .Where(slug => slug.Contains("updown", StringComparison.OrdinalIgnoreCase)
                || slug.Contains("up-or-down", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(limit, 50))
            .ToList();
    }

    string? DetectAsset(string text)
    {
        foreach (var (asset, words) in CryptoWords)
        {
            if (words.Any(word => ContainsWord(text, word)))
            {
                return asset;
            }
        }

        return null;
    }

    async Task<JsonDocument> GetJson(string url)
    {
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    static IReadOnlyList<JsonElement> RootArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToList();
        }

        return root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().ToList()
            : Array.Empty<JsonElement>();
    }

    static List<Level> Levels(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var levels) || levels.ValueKind != JsonValueKind.Array)
        {
            return new List<Level>();
        }

        return levels.EnumerateArray()
            .Select(level => new Level(DoubleProp(level, "price"), DoubleProp(level, "size")))
            .Where(level => level.Price is not null && level.Size is not null)
            .Select(level => new Level(level.Price!.Value, level.Size!.Value))
            .ToList();
    }

    static List<JsonElement> JsonishArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return new List<JsonElement>();
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().ToList();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<JsonElement>();
            }

            try
            {
                using var parsed = JsonDocument.Parse(raw);
                return parsed.RootElement.ValueKind == JsonValueKind.Array
                    ? parsed.RootElement.EnumerateArray().Select(x => x.Clone()).ToList()
                    : new List<JsonElement>();
            }
            catch (JsonException)
            {
                return new List<JsonElement>();
            }
        }

        return new List<JsonElement>();
    }

    static int? OutcomeIndex(IReadOnlyList<string> outcomes, string[] names)
    {
        for (var i = 0; i < outcomes.Count; i++)
        {
            if (names.Any(outcomes[i].Contains))
            {
                return i;
            }
        }

        return null;
    }

    static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var after = index + word.Length;
            var afterOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    static string? StringProp(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static double? DoubleProp(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var parsed) => parsed,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    static string ValueString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();

    sealed record Level(double? Price, double? Size);
}

sealed class SpotPriceClient
{
    readonly BotConfig config;
    readonly HttpClient http;

    public SpotPriceClient(BotConfig config, HttpClient http)
    {
        this.config = config;
        this.http = http;
    }

    static readonly Dictionary<string, string> Products = new()
    {
        ["BTC"] = "BTC-USD",
        ["ETH"] = "ETH-USD",
        ["SOL"] = "SOL-USD",
        ["XRP"] = "XRP-USD",
        ["BNB"] = "BNBUSDT",
    };

    public async Task<SpotSample?> GetPrice(string asset)
    {
        if (!Products.TryGetValue(asset, out var product))
        {
            return null;
        }

        var url = asset == "BNB"
            ? $"{config.BinanceBaseUrl}/api/v3/ticker/price?symbol={product}"
            : $"{config.CoinbaseBaseUrl}/products/{product}/ticker";

        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var root = await JsonDocument.ParseAsync(stream);
        var price = PolymarketJson.DoubleProp(root.RootElement, "price");
        return price is null ? null : new SpotSample(asset, price.Value, DateTimeOffset.UtcNow);
    }

    public async Task<double?> GetReferenceOpenPrice(string asset, DateTimeOffset startUtc)
    {
        if (!Products.TryGetValue(asset, out var product) || asset == "BNB")
        {
            return null;
        }

        var start = Uri.EscapeDataString(startUtc.AddMinutes(-1).UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        var end = Uri.EscapeDataString(startUtc.AddMinutes(2).UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        var url = $"{config.CoinbaseBaseUrl}/products/{product}/candles?start={start}&end={end}&granularity=60";

        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var root = await JsonDocument.ParseAsync(stream);
        if (root.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var target = startUtc.ToUnixTimeSeconds();
        double? bestOpen = null;
        var bestDistance = long.MaxValue;
        foreach (var candle in root.RootElement.EnumerateArray())
        {
            if (candle.ValueKind != JsonValueKind.Array || candle.GetArrayLength() < 4)
            {
                continue;
            }

            var ts = candle[0].GetInt64();
            var distance = Math.Abs(ts - target);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestOpen = candle[3].GetDouble();
        }

        return bestOpen;
    }
}

sealed class SpotMomentum
{
    readonly int window;
    readonly Dictionary<string, Queue<SpotSample>> samples = new();

    public SpotMomentum(int window)
    {
        this.window = window;
    }

    public void Add(SpotSample sample)
    {
        if (!samples.TryGetValue(sample.Asset, out var queue))
        {
            queue = new Queue<SpotSample>();
            samples[sample.Asset] = queue;
        }

        queue.Enqueue(sample);
        while (queue.Count > window)
        {
            queue.Dequeue();
        }
    }

    public int Count(string asset) => samples.TryGetValue(asset, out var queue) ? queue.Count : 0;

    public double? MomentumBps(string asset)
    {
        if (!samples.TryGetValue(asset, out var queue) || queue.Count < 2)
        {
            return null;
        }

        var first = queue.Peek().Price;
        var last = queue.Last().Price;
        return first <= 0 ? null : ((last / first) - 1.0) * 10_000;
    }

    public double? LastPrice(string asset)
    {
        return samples.TryGetValue(asset, out var queue) && queue.Count > 0 ? queue.Last().Price : null;
    }

    public double? ProbabilityUp(string asset)
    {
        var bps = MomentumBps(asset);
        if (bps is null)
        {
            return null;
        }

        var probability = 0.5 + Math.Clamp(bps.Value / 100.0, -0.35, 0.35);
        return Math.Clamp(probability, 0.05, 0.95);
    }
}

sealed class PriceLagStrategy
{
    readonly BotConfig config;

    public PriceLagStrategy(BotConfig config)
    {
        this.config = config;
    }

    public SignalDecision EvaluateDecision(
        MarketCandidate market,
        TopOfBook? yesBook,
        TopOfBook? noBook,
        SpotMomentum momentum,
        double? openPrice
    )
    {
        var estimate = Estimate(market.Asset, momentum, openPrice);
        if (estimate is null)
        {
            return SignalDecision.Rejected("missing spot/open estimate");
        }

        var secondsSinceStart = market.WindowStartUtc is null ? (double?)null : (DateTimeOffset.UtcNow - market.WindowStartUtc.Value).TotalSeconds;
        if (config.MaxEntrySecondsAfterStart > 0 && secondsSinceStart is not null && secondsSinceStart > config.MaxEntrySecondsAfterStart)
        {
            return SignalDecision.Rejected($"entry too late ({secondsSinceStart:0.##}s after start)");
        }

        if (estimate.DistanceBps >= config.MinMoveBps)
        {
            if (!SideEnabled("YES"))
            {
                return SignalDecision.Rejected("YES disabled by side filter");
            }

            if (yesBook is null)
            {
                return SignalDecision.Rejected("missing YES orderbook");
            }

            var reason = $"{market.Asset} is {estimate.DistanceBps:0.##} bps above open; directional YES probability {estimate.ProbabilityUp:P1}";
            var minYesProbability = MinProbabilityForSide("YES");
            if (estimate.ProbabilityUp < minYesProbability)
            {
                var rejection = $"YES probability too low ({estimate.ProbabilityUp:P1} < {minYesProbability:P1})";
                return SignalDecision.Rejected(
                    rejection,
                    BuildShadowSignal(market, "YES", market.YesTokenId, yesBook, estimate.ProbabilityUp, rejection, reason)
                );
            }

            return SignalForTokenDecision(
                market,
                "YES",
                market.YesTokenId,
                yesBook,
                estimate.ProbabilityUp,
                reason
            );
        }

        if (estimate.DistanceBps <= -config.MinMoveBps)
        {
            if (!SideEnabled("NO"))
            {
                return SignalDecision.Rejected("NO disabled by side filter");
            }

            if (market.NoTokenId is null)
            {
                return SignalDecision.Rejected("missing NO token");
            }

            if (noBook is null)
            {
                return SignalDecision.Rejected("missing NO orderbook");
            }

            var noProbability = 1.0 - estimate.ProbabilityUp;
            var reason = $"{market.Asset} is {Math.Abs(estimate.DistanceBps):0.##} bps below open; directional NO probability {noProbability:P1}";
            var minNoProbability = MinProbabilityForSide("NO");
            if (noProbability < minNoProbability)
            {
                var rejection = $"NO probability too low ({noProbability:P1} < {minNoProbability:P1})";
                return SignalDecision.Rejected(
                    rejection,
                    BuildShadowSignal(market, "NO", market.NoTokenId, noBook, noProbability, rejection, reason)
                );
            }

            return SignalForTokenDecision(
                market,
                "NO",
                market.NoTokenId,
                noBook,
                noProbability,
                reason
            );
        }

        return SignalDecision.Rejected($"move too small ({estimate.DistanceBps:0.##} bps)");
    }

    public PriceEstimate? Estimate(string asset, SpotMomentum momentum, double? openPrice)
    {
        var lastPrice = momentum.LastPrice(asset);
        if (lastPrice is null || openPrice is null || openPrice <= 0)
        {
            return null;
        }

        var distanceBps = ((lastPrice.Value / openPrice.Value) - 1.0) * 10_000;
        var recentMomentumBps = momentum.MomentumBps(asset) ?? 0;
        var score = distanceBps + recentMomentumBps * 0.35;

        var probability = 0.5 + Math.Clamp(score / 80.0, -0.45, 0.45);
        return new PriceEstimate(Math.Clamp(probability, 0.05, 0.95), distanceBps, recentMomentumBps);
    }

    public bool IsDirectionalSide(PriceEstimate? estimate, string side)
    {
        if (estimate is null)
        {
            return false;
        }

        return side.Equals("YES", StringComparison.OrdinalIgnoreCase)
            ? estimate.DistanceBps >= config.MinMoveBps
            : estimate.DistanceBps <= -config.MinMoveBps;
    }

    SignalDecision SignalForTokenDecision(
        MarketCandidate market,
        string side,
        string tokenId,
        TopOfBook book,
        double estimatedProbability,
        string reason
    )
    {
        if (book.Ask is null or <= 0)
        {
            return SignalDecision.Rejected($"{side} missing ask");
        }

        var maxAsk = MaxAskForSide(side);
        if (book.Ask.Value > maxAsk)
        {
            var rejection = $"{side} ask too high ({book.Ask.Value:0.###} > {maxAsk:0.###})";
            return SignalDecision.Rejected(
                rejection,
                BuildShadowSignal(market, side, tokenId, book, estimatedProbability, rejection, reason)
            );
        }

        if (book.AskSize < config.MinTopSize)
        {
            var rejection = $"{side} ask size too small ({book.AskSize:0.##})";
            return SignalDecision.Rejected(
                rejection,
                BuildShadowSignal(market, side, tokenId, book, estimatedProbability, rejection, reason)
            );
        }

        if (book.Spread is null)
        {
            return SignalDecision.Rejected($"{side} missing spread");
        }

        if (book.Spread > config.MaxSpread)
        {
            var rejection = $"{side} spread too wide ({book.Spread:0.###})";
            return SignalDecision.Rejected(
                rejection,
                BuildShadowSignal(market, side, tokenId, book, estimatedProbability, rejection, reason)
            );
        }

        var edge = estimatedProbability - book.Ask.Value;
        var minEdge = MinEdgeForSide(side);
        if (edge < minEdge)
        {
            var rejection = $"{side} edge too small ({edge:P1} < {minEdge:P1})";
            return SignalDecision.Rejected(
                rejection,
                BuildShadowSignal(market, side, tokenId, book, estimatedProbability, rejection, reason)
            );
        }

        var sizeUsd = Math.Min(Math.Min(config.MaxPosition, config.Bankroll * 0.04), book.AskSize * book.Ask.Value);
        if (sizeUsd <= 0)
        {
            return SignalDecision.Rejected($"{side} size is zero");
        }

        return SignalDecision.Accepted(new Signal(
            Market: market,
            Side: side,
            TokenId: tokenId,
            Ask: book.Ask.Value,
            Bid: book.Bid,
            EstimatedProbability: estimatedProbability,
            Edge: edge,
            Spread: book.Spread.Value,
            SizeUsd: Math.Round(sizeUsd, 2),
            Reason: reason
        ));
    }

    bool SideEnabled(string side) =>
        config.SideFilter.Equals("ANY", StringComparison.OrdinalIgnoreCase)
        || config.SideFilter.Equals(side, StringComparison.OrdinalIgnoreCase);

    double MinProbabilityForSide(string side) =>
        side.Equals("YES", StringComparison.OrdinalIgnoreCase) ? config.MinYesProbability : config.MinNoProbability;

    double MinEdgeForSide(string side) =>
        side.Equals("YES", StringComparison.OrdinalIgnoreCase) ? config.MinYesEdge : config.MinNoEdge;

    double MaxAskForSide(string side) =>
        side.Equals("YES", StringComparison.OrdinalIgnoreCase) ? config.MaxYesAsk : config.MaxNoAsk;

    ShadowSignal? BuildShadowSignal(
        MarketCandidate market,
        string side,
        string tokenId,
        TopOfBook book,
        double estimatedProbability,
        string rejectionReason,
        string reason
    )
    {
        if (book.Ask is null or <= 0 || book.Spread is null)
        {
            return null;
        }

        var availableUsd = Math.Max(0, book.AskSize * book.Ask.Value);
        var sizeUsd = Math.Min(Math.Min(config.MaxPosition, config.Bankroll * 0.04), availableUsd);
        if (sizeUsd <= 0)
        {
            return null;
        }

        var edge = estimatedProbability - book.Ask.Value;
        return new ShadowSignal(
            Market: market,
            Side: side,
            TokenId: tokenId,
            Ask: book.Ask.Value,
            Bid: book.Bid,
            EstimatedProbability: estimatedProbability,
            Edge: edge,
            Spread: book.Spread.Value,
            SizeUsd: Math.Round(sizeUsd, 2),
            RejectionReason: rejectionReason,
            Reason: reason
        );
    }
}

static class PolymarketJson
{
    public static double? DoubleProp(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var parsed) => parsed,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
