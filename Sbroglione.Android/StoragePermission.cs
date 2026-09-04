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
    /// <summary>
    /// Sotto API 30 <c>MANAGE_EXTERNAL_STORAGE</c> non esiste (e lo scoped storage non si applica nel
    /// modo che interessa a quest'app): lo storage è considerato già concesso. Il guard usa
    /// <c>System.OperatingSystem.IsAndroidVersionAtLeast</c> e non <c>Build.VERSION.SdkInt</c> perché solo il
    /// primo è riconosciuto dall'analizzatore CA1416.
    /// </summary>
    public static bool IsGranted =>
        !System.OperatingSystem.IsAndroidVersionAtLeast(30) || Environment.IsExternalStorageManager;

    /// <summary>
    /// Apre la pagina di sistema "Consenti accesso a tutti i file" per questa app. L'esito non è
    /// osservabile da qui: l'Activity deve ricontrollare <see cref="IsGranted"/> a <c>OnResume</c>.
    /// No-op sotto API 30 (non c'è nulla da richiedere).
    /// </summary>
    public static void RequestFromSettings(Activity activity)
    {
        if (!System.OperatingSystem.IsAndroidVersionAtLeast(30))
            return;

        var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
        intent.SetData(Uri.Parse($"package:{activity.PackageName}"));

        // Fallback costruito qui e non dentro il catch: CA1416 non propaga la guardia sopra dentro i
        // blocchi catch, quindi la lettura del campo di sistema deve stare nel flusso lineare.
        var fallback = new Intent(Settings.ActionManageAllFilesAccessPermission);

        try
        {
            activity.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            try
            {
                activity.StartActivity(fallback);
            }
            catch (ActivityNotFoundException)
            {
                // Nessuna pagina di sistema disponibile su questa ROM: l'utente dovrà concedere il
                // permesso manualmente dalle Impostazioni. Nessun'altra azione possibile da qui.
            }
        }
    }
}
