using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class WatchFolderServiceTests : IDisposable
{
    private readonly string _root;
    private readonly TimeSpan _originalDebounce;
    private readonly TimeSpan _originalMaxDebounceWindow;
    private readonly Func<WatchRule, TimeSpan>? _originalInterval;
    private readonly Func<WatchRule, CancellationToken, Task>? _originalSync;
    private readonly Func<string, FileSystemWatcher>? _originalWatcherFactory;
    private int _syncCount;

    public WatchFolderServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _originalDebounce = WatchFolderService.DebounceDelay;
        _originalMaxDebounceWindow = WatchFolderService.MaxDebounceWindow;
        _originalInterval = WatchFolderService.IntervalOverride;
        _originalSync = WatchFolderService.SyncOverride;
        _originalWatcherFactory = WatchFolderService.WatcherFactory;

        WatchFolderService.DebounceDelay = TimeSpan.FromMilliseconds(200);
        WatchFolderService.SyncOverride = (_, _) =>
        {
            Interlocked.Increment(ref _syncCount);
            return Task.CompletedTask;
        };
    }

    public void Dispose()
    {
        WatchFolderService.StopAll();
        WatchFolderService.DebounceDelay = _originalDebounce;
        WatchFolderService.MaxDebounceWindow = _originalMaxDebounceWindow;
        WatchFolderService.IntervalOverride = _originalInterval;
        WatchFolderService.SyncOverride = _originalSync;
        WatchFolderService.WatcherFactory = _originalWatcherFactory;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private WatchRule CreateRule(WatchMode mode = WatchMode.OnChange)
    {
        string source = Path.Combine(_root, "src");
        string destination = Path.Combine(_root, "dst");
        Directory.CreateDirectory(source);
        return new WatchRule { SourcePath = source, DestinationPath = destination, Mode = mode };
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        long start = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - start < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), "condizione non raggiunta entro il timeout");
    }

    [Fact]
    public async Task Start_FileCreated_TriggersOneSyncAfterDebounce()
    {
        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);

        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "a.txt"), "ciao");

        await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 1);
        await Task.Delay(600); // finestra di quiete: nessuna sync ulteriore
        Assert.Equal(1, Volatile.Read(ref _syncCount));
    }

    [Fact]
    public async Task Start_BurstOfEvents_CoalescesIntoOneSync()
    {
        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);

        for (int i = 0; i < 5; i++)
            await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, $"f{i}.txt"), "x");

        await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 1);
        await Task.Delay(600);
        Assert.Equal(1, Volatile.Read(ref _syncCount));
    }

    [Fact]
    public async Task EventsDuringSync_RunSecondSyncAfterwards()
    {
        var firstSyncStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSync = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        WatchFolderService.SyncOverride = async (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 1)
            {
                firstSyncStarted.TrySetResult();
                await releaseFirstSync.Task;
            }
        };

        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);
        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "a.txt"), "1");
        await firstSyncStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Evento mentre la prima sync è in corso → deve accodare una seconda sync.
        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "b.txt"), "2");
        await Task.Delay(300); // lascia arrivare l'evento al watcher
        releaseFirstSync.TrySetResult();

        await WaitUntilAsync(() => Volatile.Read(ref count) >= 2);
    }

    /// <summary>
    /// Non-sordità: dopo che un ciclo completo si è concluso il runner deve tornare in
    /// ascolto. Un debounce che perde il segnale lascerebbe la regola morta in silenzio.
    /// </summary>
    [Fact]
    public async Task SecondBurstAfterFirstCycle_TriggersNewSync()
    {
        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);

        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "a.txt"), "1");
        await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 1);
        await Task.Delay(600); // il primo ciclo è concluso: runner di nuovo in attesa

        for (int i = 0; i < 3; i++)
            await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, $"b{i}.txt"), "2");

        await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 2);
    }

    /// <summary>Un sottoscrittore che lancia non deve uccidere il loop del runner.</summary>
    [Fact]
    public async Task ThrowingStatusSubscriber_DoesNotKillRunner()
    {
        Action<WatchStatus> handler = _ => throw new InvalidOperationException("boom");
        WatchFolderService.StatusChanged += handler;
        try
        {
            WatchRule rule = CreateRule();
            WatchFolderService.Start(rule);

            await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "a.txt"), "1");
            await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 1);
            await Task.Delay(600);

            await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "b.txt"), "2");
            await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 2);
        }
        finally
        {
            WatchFolderService.StatusChanged -= handler;
        }
    }

    [Fact]
    public async Task SyncThrows_ReportsErrorStatusWithoutPropagating()
    {
        WatchFolderService.SyncOverride = (_, _) => throw new InvalidOperationException("disco pieno");
        var statuses = new List<WatchStatus>();
        Action<WatchStatus> handler = status => { lock (statuses) statuses.Add(status); };
        WatchFolderService.StatusChanged += handler;
        try
        {
            WatchRule rule = CreateRule();

            await WatchFolderService.RunNowAsync(rule);

            lock (statuses)
                Assert.Contains(statuses, s => !s.IsRunning && s.MessageKind == WatchFolderService.StatusError && s.MessageDetail == "disco pieno");
        }
        finally
        {
            WatchFolderService.StatusChanged -= handler;
        }
    }

    [Fact]
    public async Task Stop_PreventsFurtherSyncs()
    {
        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);
        WatchFolderService.Stop(rule.Id);

        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "a.txt"), "ciao");

        await Task.Delay(600);
        Assert.Equal(0, Volatile.Read(ref _syncCount));
        Assert.Empty(WatchFolderService.ActiveRuleIds);
    }

    [Fact]
    public void Start_SameRuleTwice_KeepsSingleRunner()
    {
        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);
        WatchFolderService.Start(rule);

        Assert.Equal(rule.Id, Assert.Single(WatchFolderService.ActiveRuleIds));
    }

    /// <summary>
    /// Start concorrenti sulla stessa regola: la sequenza stop → controlli → registrazione →
    /// avvio deve essere atomica. Interlacciata, il chiamante più lento nei controlli
    /// registra il proprio runner sopra quello già avviato dall'altro, che resta vivo e non
    /// più fermabile: dopo lo Stop continuerebbe a sincronizzare (zombie).
    /// </summary>
    [Fact]
    public async Task ConcurrentStarts_SameRule_LeaveNoRunnerAliveAfterStop()
    {
        WatchFolderService.IntervalOverride = _ => TimeSpan.FromMilliseconds(20);
        WatchRule rule = CreateRule(WatchMode.Interval);

        using var start = new Barrier(8);
        Task[] starters = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                start.SignalAndWait();
                WatchFolderService.Start(rule);
            }))
            .ToArray();
        await Task.WhenAll(starters);

        Assert.Equal(rule.Id, Assert.Single(WatchFolderService.ActiveRuleIds));
        await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 1);

        WatchFolderService.Stop(rule.Id);
        Assert.Empty(WatchFolderService.ActiveRuleIds);

        // Un runner sopravvissuto continuerebbe a girare sull'intervallo da 20 ms.
        await Task.Delay(100);
        int afterStop = Volatile.Read(ref _syncCount);
        await Task.Delay(400);
        Assert.Equal(afterStop, Volatile.Read(ref _syncCount));
    }

    [Fact]
    public void Start_MissingSource_DoesNotStartAndReportsError()
    {
        var statuses = new List<WatchStatus>();
        Action<WatchStatus> handler = status => { lock (statuses) statuses.Add(status); };
        WatchFolderService.StatusChanged += handler;
        try
        {
            var rule = new WatchRule
            {
                SourcePath = Path.Combine(_root, "manca"),
                DestinationPath = Path.Combine(_root, "dst")
            };
            WatchFolderService.Start(rule);

            Assert.Empty(WatchFolderService.ActiveRuleIds);
            lock (statuses)
                Assert.Contains(statuses, s => s.RuleId == rule.Id && s.MessageKind == WatchFolderService.StatusSourceNotFound);
        }
        finally
        {
            WatchFolderService.StatusChanged -= handler;
        }
    }

    [Fact]
    public async Task RunNowAsync_WithoutRunner_ExecutesOneShot()
    {
        WatchRule rule = CreateRule();

        await WatchFolderService.RunNowAsync(rule);

        Assert.Equal(1, Volatile.Read(ref _syncCount));
    }

    [Fact]
    public async Task IntervalMode_RunsRepeatedly()
    {
        WatchFolderService.IntervalOverride = _ => TimeSpan.FromMilliseconds(50);
        WatchRule rule = CreateRule(WatchMode.Interval);
        WatchFolderService.Start(rule);

        await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 2);
        WatchFolderService.Stop(rule.Id);
    }

    /// <summary>
    /// Avvio del watcher fallito (limiti inotify, percorso ostile): Start non deve
    /// propagare l'eccezione né lasciare un runner zombie registrato.
    /// </summary>
    [Fact]
    public void Start_WatcherCreationFails_DoesNotThrowAndReportsError()
    {
        WatchFolderService.WatcherFactory = _ => throw new IOException("limite inotify raggiunto");
        var statuses = new List<WatchStatus>();
        Action<WatchStatus> handler = status => { lock (statuses) statuses.Add(status); };
        WatchFolderService.StatusChanged += handler;
        try
        {
            WatchRule rule = CreateRule();

            WatchFolderService.Start(rule); // non deve lanciare

            Assert.Empty(WatchFolderService.ActiveRuleIds);
            lock (statuses)
                Assert.Contains(statuses, s => s.RuleId == rule.Id && !s.IsRunning && s.MessageKind == WatchFolderService.StatusStartFailed
                    && s.MessageDetail != null && s.MessageDetail.Contains("limite inotify raggiunto", StringComparison.Ordinal));
        }
        finally
        {
            WatchFolderService.StatusChanged -= handler;
        }
    }

    /// <summary>
    /// Destinazione dentro la sorgente: la copia rialimenterebbe il watcher all'infinito.
    /// Né il runner né la sync one-shot devono partire.
    /// </summary>
    [Fact]
    public async Task Start_DestinationInsideSource_RefusesAndReportsError()
    {
        var statuses = new List<WatchStatus>();
        Action<WatchStatus> handler = status => { lock (statuses) statuses.Add(status); };
        WatchFolderService.StatusChanged += handler;
        try
        {
            WatchRule rule = CreateRule();
            rule.DestinationPath = Path.Combine(rule.SourcePath, "backup");

            WatchFolderService.Start(rule);

            Assert.Empty(WatchFolderService.ActiveRuleIds);

            await WatchFolderService.RunNowAsync(rule);

            Assert.Equal(0, Volatile.Read(ref _syncCount));
            lock (statuses)
                Assert.Equal(2, statuses.Count(s => s.RuleId == rule.Id && s.MessageKind == WatchFolderService.StatusSelfFeeding));
        }
        finally
        {
            WatchFolderService.StatusChanged -= handler;
        }
    }

    /// <summary>
    /// Debounce scorrevole con tetto: su una cartella sempre in movimento la finestra
    /// di quiete non scade mai, ma la sync deve partire comunque entro MaxDebounceWindow.
    /// </summary>
    [Fact]
    public async Task ContinuousEvents_SyncRunsWithinMaxDebounceWindow()
    {
        WatchFolderService.DebounceDelay = TimeSpan.FromSeconds(1);
        WatchFolderService.MaxDebounceWindow = TimeSpan.FromMilliseconds(500);

        WatchRule rule = CreateRule();
        WatchFolderService.Start(rule);

        using var writerCts = new CancellationTokenSource();
        Task writer = Task.Run(async () =>
        {
            for (int i = 0; !writerCts.IsCancellationRequested; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, $"f{i}.txt"), "x", writerCts.Token);
                await Task.Delay(50, writerCts.Token);
            }
        });

        try
        {
            // Senza tetto il debounce (1s di quiete) non scadrebbe mai: nessuna sync.
            await WaitUntilAsync(() => Volatile.Read(ref _syncCount) >= 1, timeoutMs: 2500);
        }
        finally
        {
            writerCts.Cancel();
            try { await writer; } catch (OperationCanceledException) { /* atteso */ }
        }
    }

    [Fact]
    public async Task RunNowAsync_WithoutOverride_CopiesFiles()
    {
        WatchFolderService.SyncOverride = null; // sync reale
        WatchRule rule = CreateRule();
        Directory.CreateDirectory(rule.DestinationPath); // la sync rifiuta una destinazione inesistente
        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "doc.txt"), "contenuto");

        await WatchFolderService.RunNowAsync(rule);

        Assert.Equal("contenuto", await File.ReadAllTextAsync(Path.Combine(rule.DestinationPath, "doc.txt")));
    }
}
