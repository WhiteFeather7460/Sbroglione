using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class WatchFolderServiceTests : IDisposable
{
    private readonly string _root;
    private readonly TimeSpan _originalDebounce;
    private readonly Func<WatchRule, TimeSpan>? _originalInterval;
    private readonly Func<WatchRule, CancellationToken, Task>? _originalSync;
    private int _syncCount;

    public WatchFolderServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _originalDebounce = WatchFolderService.DebounceDelay;
        _originalInterval = WatchFolderService.IntervalOverride;
        _originalSync = WatchFolderService.SyncOverride;

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
        WatchFolderService.IntervalOverride = _originalInterval;
        WatchFolderService.SyncOverride = _originalSync;
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
                Assert.Contains(statuses, s => s.RuleId == rule.Id && s.Message.StartsWith("Sorgente non trovata", StringComparison.Ordinal));
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

    [Fact]
    public async Task RunNowAsync_WithoutOverride_CopiesFiles()
    {
        WatchFolderService.SyncOverride = null; // sync reale
        WatchRule rule = CreateRule();
        await File.WriteAllTextAsync(Path.Combine(rule.SourcePath, "doc.txt"), "contenuto");

        await WatchFolderService.RunNowAsync(rule);

        Assert.Equal("contenuto", await File.ReadAllTextAsync(Path.Combine(rule.DestinationPath, "doc.txt")));
    }
}
