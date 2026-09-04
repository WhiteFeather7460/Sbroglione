using System;
using System.Threading.Tasks;

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

using Sbroglione.Services;

namespace Sbroglione.Android;

/// <summary>
/// Ospita i runner watch-folder (<see cref="WatchFolderService"/>) in un foreground service
/// Android, così sopravvivono alla chiusura dell'Activity e alle restrizioni di Doze /
/// battery optimization — su desktop i runner vivono nel processo dell'app, qui il processo
/// dell'Activity non è un posto sicuro dove tenerli.
///
/// L'attributo <c>[Service]</c> genera la voce <c>&lt;service&gt;</c> nel manifest a build
/// time (toolchain .NET for Android): non serve dichiararlo a mano in AndroidManifest.xml,
/// che porta solo i permessi.
///
/// Verifica reale (notifica visibile, sopravvivenza a Doze, sync effettiva in background)
/// possibile solo su device: rientra nella verifica manuale finale del porting.
/// </summary>
[Service(
    Name = "com.whitefeather.sbroglione.WatchFolderForegroundService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class WatchFolderForegroundService : Service
{
    /// <summary>
    /// Id del canale di notifica. Stabile: cambiarlo lascerebbe orfano il canale già creato
    /// sui device dove il service ha girato almeno una volta (i canali si eliminano solo
    /// disinstallando l'app), e l'utente perderebbe le preferenze impostate su di esso.
    /// </summary>
    private const string NotificationChannelId = "sbroglione.watchsync";

    /// <summary>Id della notifica persistente. Deve essere diverso da zero: 0 fa fallire startForeground.</summary>
    private const int NotificationId = 1001;

    /// <summary>
    /// I runner partono una sola volta per istanza del service: <c>OnStartCommand</c> può
    /// essere richiamato più volte (riavvio sticky, nuovo <c>startForegroundService</c> a
    /// service già vivo) e <see cref="WatchFolderService.Start"/>, pur essendo idempotente,
    /// riavvierebbe ogni runner buttando via il debounce in corso.
    /// </summary>
    private bool _runnersStarted;

    /// <summary>
    /// Protegge l'avvio/arresto reale dei runner, non la sola lettura/scrittura di
    /// <c>_runnersStarted</c>. <c>OnStartCommand</c> e <c>OnDestroy</c> girano entrambi sul
    /// main looper thread di Android, quindi non corrono mai fra loro — ma
    /// <c>OnStartCommand</c> accoda l'avvio vero e proprio (<see cref="WatchFolderService.StartAllEnabledRules"/>)
    /// su un thread di pool via <c>Task.Run</c>, mentre <c>OnDestroy</c> chiama
    /// <see cref="WatchFolderService.StopAll"/> in modo sincrono sul main thread: se il service
    /// viene fermato subito dopo essere stato avviato, lo stop può eseguire prima che il task
    /// accodato parta, e quel task avvierebbe i runner (watcher/timer) di un service già in fase
    /// di distruzione, senza notifica né host a possederli. Il lock serializza lo start reale
    /// (dentro il Task.Run, con un ricontrollo di <c>_runnersStarted</c>) e lo stop di
    /// <c>OnDestroy</c>, così i due non possono più interlacciarsi.
    /// </summary>
    private readonly object _runnersLock = new();

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // startForeground va chiamato entro pochi secondi dallo startForegroundService, prima
        // di qualunque lavoro: farlo dopo l'avvio dei runner rischierebbe un ANR/crash di
        // sistema (ForegroundServiceDidNotStartInTimeException).
        EnsureNotificationChannel();
        Notification notification = BuildNotification();

        // Il tipo esplicito esiste da Android 10; sotto, startForeground non lo accetta.
        // Guardia con OperatingSystem e non con Build.VERSION.SdkInt: solo la prima è
        // riconosciuta dall'analyzer di compatibilità piattaforma (CA1416).
        //
        // StartForeground può lanciare (ForegroundServiceDidNotStartInTimeException, o un
        // rifiuto di sistema sotto restrizioni batteria): senza try/catch abbatterebbe il
        // processo. Se fallisce, il service si ferma da solo invece di restare in uno stato
        // a metà (avviato ma senza notifica, che il sistema tratterebbe come ANR).
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
            else
                StartForeground(NotificationId, notification);
        }
        catch (Exception)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        bool shouldScheduleStart = false;
        lock (_runnersLock)
        {
            if (!_runnersStarted)
            {
                _runnersStarted = true;
                shouldScheduleStart = true;
            }
        }

        if (shouldScheduleStart)
        {
            // Fuori dal main thread: StartAllEnabledRules legge il file delle regole e crea
            // i FileSystemWatcher, e il main thread qui è quello della UI dell'app.
            //
            // La chiamata vera e propria vive dentro il lock, con un ricontrollo di
            // _runnersStarted appena prima: se OnDestroy ferma il service (e azzera il flag)
            // prima che questo task di pool riesca a partire, la regola qui sotto non avvia
            // più nulla — senza il ricontrollo resusciteremmo watcher/timer per un service già
            // in fase di distruzione, senza notifica né host a possederli. Lo stesso lock è
            // preso da OnDestroy attorno a StopAll, quindi le due operazioni non possono più
            // interlacciarsi.
            _ = Task.Run(() =>
            {
                try
                {
                    lock (_runnersLock)
                    {
                        if (!_runnersStarted)
                            return;

                        int started = WatchFolderService.StartAllEnabledRules();

                        // Riavvio sticky del sistema con zero regole abilitate: nessun runner
                        // avviato, quindi niente da sincronizzare. Fermarsi invece di restare
                        // vivi indefinitamente con una notifica persistente e inutile.
                        //
                        // started == 0 può però essere una lettura stantia: StartAllEnabledRules
                        // ricarica le regole da disco, e se un runner è stato appena avviato da
                        // fuori (es. WatchFoldersViewModel.ApplyRunnerState sul thread UI) prima
                        // che il salvataggio async delle regole sia arrivato su disco, questa
                        // Load() vede zero regole abilitate mentre un runner live esiste già.
                        // ActiveRuleIds riflette lo stato dei runner in memoria, non il file:
                        // se non è vuoto, un runner è comunque vivo e non va fermato.
                        if (started == 0 && WatchFolderService.ActiveRuleIds.Count == 0)
                            StopSelfResult(startId);
                    }
                }
                catch (Exception)
                {
                    // StartAllEnabledRules non lancia; difesa in profondità: un'eccezione su
                    // un thread di pool abbatterebbe il processo, notifica inclusa.
                }
            });
        }

        // Sticky: se il sistema uccide il service per pressione di memoria lo ricrea, e i
        // runner ripartono da soli. L'intent non porta stato, quindi non serve ridarlo.
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        // Su desktop i runner muoiono col processo; qui il service può essere fermato mentre
        // il processo resta vivo, quindi lo stop dev'essere esplicito: senza, resterebbero
        // watcher e timer a copiare file con la notifica ormai sparita.
        //
        // Sotto lo stesso _runnersLock del Task.Run in OnStartCommand: previene la race in cui
        // quel task di pool avvia i runner subito dopo che questo stop li ha appena fermati.
        lock (_runnersLock)
        {
            try
            {
                WatchFolderService.StopAll();
            }
            catch (Exception)
            {
                // Lo shutdown non deve mai lanciare fuori da OnDestroy.
            }

            _runnersStarted = false;
        }

        base.OnDestroy();
    }

    /// <summary>
    /// Crea il canale di notifica (obbligatorio da Android 8; minSdk del progetto è 26,
    /// quindi non serve una variante pre-canale). Ripetere la creazione è un no-op lato
    /// sistema e non azzera le preferenze utente sul canale.
    /// Importanza <c>Low</c>: la notifica è un requisito tecnico del foreground service,
    /// non un avviso — niente suono né heads-up.
    /// </summary>
    private void EnsureNotificationChannel()
    {
        if (GetSystemService(NotificationService) is not NotificationManager manager)
            return;

        var channel = new NotificationChannel(
            NotificationChannelId,
            LocalizedString("Str.WatchSync.ForegroundNotificationTitle", "Auto sync active"),
            NotificationImportance.Low);
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        // Tap sulla notifica: riporta all'Activity esistente invece di crearne una seconda.
        var launchIntent = new Intent(this, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        // Immutable: obbligatorio da Android 12 per un PendingIntent senza extra da riempire.
        var contentIntent = PendingIntent.GetActivity(
            this,
            requestCode: 0,
            launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new Notification.Builder(this, NotificationChannelId)
            .SetContentTitle(LocalizedString("Str.WatchSync.ForegroundNotificationTitle", "Auto sync active"))
            .SetContentText(LocalizedString(
                "Str.WatchSync.ForegroundNotificationText",
                "Sbroglione is keeping your watched folders in sync."))
            .SetSmallIcon(Resource.Drawable.icon)
            .SetContentIntent(contentIntent)
            .SetOngoing(true)
            .Build();
    }

    /// <summary>
    /// Traduce una chiave, con fallback letterale. Se il sistema ricrea il service da solo
    /// (riavvio sticky) il processo può non aver mai avviato la UI, quindi
    /// <see cref="LocalizationService"/> potrebbe non essere ancora inizializzato: in quel
    /// caso <see cref="LocalizationService.Tr"/> restituisce la chiave stessa, che non è
    /// testo da mostrare.
    /// </summary>
    private static string LocalizedString(string key, string fallback)
    {
        try
        {
            string value = LocalizationService.Tr(key);
            return value == key ? fallback : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
