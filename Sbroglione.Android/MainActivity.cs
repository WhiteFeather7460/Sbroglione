using System;

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;

using Sbroglione.Services;

namespace Sbroglione.Android;

[Activity(
    Label = "Sbroglione",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    /// <summary>Request code della richiesta runtime di POST_NOTIFICATIONS; il risultato non viene osservato.</summary>
    private const int PostNotificationsRequestCode = 2001;

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Registra i seam prima che Avalonia arrivi a OnFrameworkInitializationCompleted
        // (CustomizeAppBuilder gira prima, nello stesso OnCreate): è lì che App decide se
        // c'è un host di background da avviare per i runner watch-folder, quale sia lo stato
        // dell'accesso allo storage e da quale radice partire.
        App.StartBackgroundWatchHost = StartWatchFolderForegroundService;
        App.StorageAccessGranted = () => StoragePermission.IsGranted;
        App.RequestStorageAccess = () => StoragePermission.RequestFromSettings(this);
        PlatformPaths.DefaultRootPathOverride = () =>
            global::Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? "/storage/emulated/0";

        // Registrazione mancata dallo scaffold iniziale: su desktop avviene in Program.cs, ma
        // il registro di IconProvider è per-processo e AvaloniaMainActivity non passa mai da
        // lì, quindi ogni icona fa-* (usata ovunque nella UI) lanciava KeyNotFoundException al
        // primo layout, mandando in crash l'app all'avvio.
        IconProvider.Current.Register<FontAwesomeIconProvider>();

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestPostNotificationsIfNeeded();
    }

    /// <summary>
    /// L'utente può concedere/revocare "All files access" solo dalle Impostazioni di sistema,
    /// fuori dall'app: l'unico modo affidabile di accorgersene è ricontrollare quando l'Activity
    /// torna in primo piano. <see cref="App.OnStorageAccessChanged"/> aggiorna la UI se lo stato
    /// osservato è cambiato rispetto all'ultimo noto.
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();
        App.OnStorageAccessChanged?.Invoke(StoragePermission.IsGranted);
    }

    /// <summary>
    /// Avvia il foreground service che ospita i runner watch-folder. Chiamato da
    /// <c>App.OnFrameworkInitializationCompleted</c> tramite
    /// <see cref="App.StartBackgroundWatchHost"/>, quindi mentre l'Activity è in primo piano:
    /// da Android 12 avviare un foreground service da background lancerebbe
    /// <c>ForegroundServiceStartNotAllowedException</c>.
    /// </summary>
    private void StartWatchFolderForegroundService()
    {
        var intent = new Intent(this, typeof(WatchFolderForegroundService));
        StartForegroundService(intent);
    }

    /// <summary>
    /// Da Android 13 POST_NOTIFICATIONS è un permesso runtime. Negarlo non impedisce al
    /// foreground service di girare: rende solo invisibile la sua notifica, quindi la
    /// richiesta è best effort e il risultato non viene atteso né osservato.
    /// </summary>
    private void RequestPostNotificationsIfNeeded()
    {
        // Guardia con OperatingSystem e non con Build.VERSION.SdkInt: solo la prima è
        // riconosciuta dall'analyzer di compatibilità piattaforma (CA1416).
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return;

        const string permission = global::Android.Manifest.Permission.PostNotifications;
        if (CheckSelfPermission(permission) == Permission.Granted)
            return;

        RequestPermissions([permission], PostNotificationsRequestCode);
    }
}
