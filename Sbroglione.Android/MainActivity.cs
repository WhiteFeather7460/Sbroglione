using System;

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

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
        // Registra il seam prima che Avalonia arrivi a OnFrameworkInitializationCompleted
        // (CustomizeAppBuilder gira prima, nello stesso OnCreate): è lì che App decide se
        // c'è un host di background da avviare per i runner watch-folder.
        App.StartBackgroundWatchHost = StartWatchFolderForegroundService;

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestPostNotificationsIfNeeded();
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
