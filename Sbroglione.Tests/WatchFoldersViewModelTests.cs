using System.Collections.Generic;

using Sbroglione.Models;
using Sbroglione.Services;
using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class WatchFoldersViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalStorePath;
    private readonly Func<string, string, string, Task<bool>>? _originalConfirm;
    private readonly Func<WatchRule, TimeSpan>? _originalInterval;
    private readonly Func<WatchRule, CancellationToken, Task>? _originalSync;

    // VM create dai singoli test tramite CreateVm(): disposte tutte a fine test, così
    // _statusHandler non resta iscritto a WatchFolderService.StatusChanged (evento statico,
    // vive per tutto il processo) oltre la durata del test che l'ha creato.
    private readonly List<WatchFoldersViewModel> _createdVms = new();

    public WatchFoldersViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fe-watchvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _originalStorePath = WatchRuleStore.CurrentPath;
        WatchRuleStore.CurrentPath = Path.Combine(_root, "watch-rules.json");
        _originalConfirm = ConfirmDialogHelper.Override;
        _originalInterval = WatchFolderService.IntervalOverride;
        _originalSync = WatchFolderService.SyncOverride;
        // Senza loop del dispatcher i Post andrebbero persi: esecuzione sincrona nei test.
        UiDispatch.Override = action => action();
    }

    public void Dispose()
    {
        UiDispatch.Override = null;
        // Dispose() è idempotente (-= su handler già rimosso è un no-op): sicuro anche
        // per i test che chiamano vm.Dispose() esplicitamente prima di questo cleanup.
        foreach (WatchFoldersViewModel vm in _createdVms)
            vm.Dispose();

        ConfirmDialogHelper.Override = _originalConfirm;
        WatchRuleStore.CurrentPath = _originalStorePath;
        WatchFolderService.StopAll();
        WatchFolderService.IntervalOverride = _originalInterval;
        WatchFolderService.SyncOverride = _originalSync;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private WatchFoldersViewModel CreateVm()
    {
        var vm = new WatchFoldersViewModel { ManageRunners = false };
        _createdVms.Add(vm);
        return vm;
    }

    private WatchFoldersViewModel CreateVmWithRunners()
    {
        var vm = new WatchFoldersViewModel { ManageRunners = true };
        _createdVms.Add(vm);
        return vm;
    }

    private static async Task<WatchRuleViewModel> AddCompleteRuleAsync(WatchFoldersViewModel vm)
    {
        vm.AddRule();
        WatchRuleViewModel rule = vm.Rules[^1];
        rule.SourcePath = "/tmp/src";
        rule.DestinationPath = "/tmp/dst";
        await vm.LastSaveTask!;
        return rule;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        long start = Environment.TickCount64;
        while (!condition() && Environment.TickCount64 - start < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), $"condizione non raggiunta entro {timeoutMs}ms");
    }

    private async Task<WatchRuleViewModel> AddValidRuleAsync(WatchFoldersViewModel vm)
    {
        vm.AddRule();
        WatchRuleViewModel rule = vm.Rules[^1];
        // Crea directory reali per il test (come in WatchFolderServiceTests)
        string source = Path.Combine(_root, "src-" + Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(_root, "dst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        rule.SourcePath = source;
        rule.DestinationPath = destination;
        await vm.LastSaveTask!;
        return rule;
    }

    [Fact]
    public async Task AddRule_AddsRowWithoutSaving()
    {
        var vm = CreateVm();
        await vm.RulesLoad;

        vm.AddRule();

        Assert.Single(vm.Rules);
        Assert.True(vm.HasRules);
        Assert.Null(vm.LastSaveTask); // regola vuota: verrebbe scartata dal Sanitize
    }

    [Fact]
    public async Task RuleChange_PersistsToStore()
    {
        var vm = CreateVm();
        await vm.RulesLoad;

        await AddCompleteRuleAsync(vm);

        var loaded = await WatchRuleStore.LoadAsync();
        WatchRule single = Assert.Single(loaded);
        Assert.Equal("/tmp/src", single.SourcePath);
        Assert.Equal("/tmp/dst", single.DestinationPath);
    }

    [Fact]
    public async Task RemoveRule_Confirmed_RemovesAndPersists()
    {
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(true);
        var vm = CreateVm();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddCompleteRuleAsync(vm);

        await vm.RemoveRuleAsync(rule);
        await vm.LastSaveTask!;

        Assert.Empty(vm.Rules);
        Assert.False(vm.HasRules);
        Assert.Empty(await WatchRuleStore.LoadAsync());
    }

    [Fact]
    public async Task RemoveRule_Declined_KeepsRule()
    {
        ConfirmDialogHelper.Override = (_, _, _) => Task.FromResult(false);
        var vm = CreateVm();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddCompleteRuleAsync(vm);

        await vm.RemoveRuleAsync(rule);

        Assert.Single(vm.Rules);
        Assert.Single(await WatchRuleStore.LoadAsync());
    }

    [Fact]
    public async Task Ctor_LoadsExistingRules()
    {
        await WatchRuleStore.SaveAsync(new[]
        {
            new WatchRule { SourcePath = "/tmp/src", DestinationPath = "/tmp/dst", Enabled = false }
        });

        var vm = CreateVm();
        await vm.RulesLoad;

        WatchRuleViewModel single = Assert.Single(vm.Rules);
        Assert.Equal("/tmp/src", single.SourcePath);
        Assert.Equal("Disattivata", single.StatusText);
    }

    [Fact]
    public async Task StatusChanged_UpdatesMatchingRow()
    {
        var vm = CreateVm();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddCompleteRuleAsync(vm);
        var lastRun = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);

        WatchFolderService.RaiseStatus(new WatchStatus(rule.Model.Id, false, lastRun, WatchFolderService.StatusCompleted));

        string expectedStatus = string.Format(LocalizationService.Tr("Str.WatchFolders.Status.CompletedFormat"), lastRun.ToLocalTime());
        Assert.Equal(expectedStatus, rule.StatusText);
        Assert.NotNull(rule.LastRunText);
        string lastRunPrefix = LocalizationService.Tr("Str.WatchFolders.LastRunFormat").Split("{0")[0];
        Assert.StartsWith(lastRunPrefix, rule.LastRunText);
        vm.Dispose();
    }

    [Fact]
    public async Task Dispose_StopsListening()
    {
        var vm = CreateVm();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddCompleteRuleAsync(vm);
        string? before = rule.StatusText;

        vm.Dispose();
        WatchFolderService.RaiseStatus(new WatchStatus(rule.Model.Id, true, null, WatchFolderService.StatusSyncing));

        Assert.Equal(before, rule.StatusText);
    }

    [Fact]
    public async Task OnRuleChanged_EnabledRuleEdit_StartsRunner()
    {
        // Test che OnRuleChanged con ManageRunners=true avvia il runner correttamente
        // (verifica che Stop e Start siano sequenziali senza race condition).
        var vm = CreateVmWithRunners();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddValidRuleAsync(vm);

        // Abilita la regola
        rule.Model.Enabled = true;
        vm.OnRuleChanged(rule);

        // Attendi che il runner si avvii (Task.Run fire-and-forget)
        await WaitUntilAsync(() => WatchFolderService.ActiveRuleIds.Contains(rule.Model.Id));

        // Verifica che il runner sia attivo
        Assert.Contains(rule.Model.Id, WatchFolderService.ActiveRuleIds);

        vm.Dispose();
    }

    /// <summary>
    /// OnRuleChanged scatta a ogni set di proprietà della riga: una raffica sulla stessa
    /// regola produceva riallineamenti concorrenti, con il rischio di due runner sulla
    /// stessa regola (uno registrato, uno zombie che continua a copiare anche dopo il
    /// disable). Deve restare esattamente un runner, e nessuno dopo il disable.
    /// </summary>
    [Fact]
    public async Task OnRuleChanged_BurstOnSameRule_LeavesOneRunner_AndNoneAfterDisable()
    {
        // Modalità Interval con tick brevissimo e sync finta: un runner zombie si
        // riconosce dal contatore che continua a salire dopo il disable.
        WatchFolderService.IntervalOverride = _ => TimeSpan.FromMilliseconds(20);
        int syncCount = 0;
        WatchFolderService.SyncOverride = (_, _) =>
        {
            Interlocked.Increment(ref syncCount);
            return Task.CompletedTask;
        };

        var vm = CreateVmWithRunners();
        await vm.RulesLoad;
        WatchRuleViewModel rule = await AddValidRuleAsync(vm);
        rule.Model.Mode = WatchMode.Interval;
        rule.Model.Enabled = true;

        for (int i = 0; i < 8; i++)
            vm.OnRuleChanged(rule);

        // La catena per regola garantisce che l'ultima operazione accodata sia anche
        // l'ultima eseguita: attenderla significa attendere tutta la raffica.
        await vm.LastRunnerOpTask!;

        Assert.Equal(rule.Model.Id, Assert.Single(WatchFolderService.ActiveRuleIds));
        await WaitUntilAsync(() => Volatile.Read(ref syncCount) >= 1);

        rule.Model.Enabled = false;
        vm.OnRuleChanged(rule);
        await vm.LastRunnerOpTask!;

        Assert.DoesNotContain(rule.Model.Id, WatchFolderService.ActiveRuleIds);

        await Task.Delay(100);
        int afterDisable = Volatile.Read(ref syncCount);
        await Task.Delay(400);
        Assert.Equal(afterDisable, Volatile.Read(ref syncCount));

        vm.Dispose();
    }
}
