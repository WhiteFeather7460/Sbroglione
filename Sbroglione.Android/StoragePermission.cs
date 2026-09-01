using Android.App;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Provider;

namespace Sbroglione.Android;

/// <summary>
/// Stato e richiesta del permesso "All files access". Nessun binding: la sola API richiesta è
/// <c>Environment.IsExternalStorageManager</c> (letta a ogni chiamata, mai cache — l'utente può
/// revocare il permesso dalle Impostazioni in qualsiasi momento) più l'intent di sistema che apre
/// la pagina dedicata all'app.
/// </summary>
public static class StoragePermission
{
    public static bool IsGranted => Environment.IsExternalStorageManager;

    /// <summary>
    /// Apre la pagina di sistema "Consenti accesso a tutti i file" per questa app. L'esito non è
    /// osservabile da qui: l'Activity deve ricontrollare <see cref="IsGranted"/> a <c>OnResume</c>.
    /// </summary>
    public static void RequestFromSettings(Activity activity)
    {
        var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
        intent.SetData(Uri.Parse($"package:{activity.PackageName}"));
        activity.StartActivity(intent);
    }
}
