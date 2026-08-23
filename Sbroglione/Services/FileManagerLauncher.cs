using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Sbroglione.Services;

/// <summary>
/// Apre percorsi nel file manager di sistema (menu contestuale righe occupazione disco).
/// Process.Start può fallire in sessioni headless o senza file manager grafico installato:
/// l'eccezione viene ignorata, è un'azione accessoria da menu, non un flusso critico.
/// </summary>
public static class FileManagerLauncher
{
    internal enum Platform { Windows, MacOs, Linux }

    public static void OpenFolder(string path)
    {
        try { Process.Start(BuildOpenFolderStartInfo(path, DetectPlatform())); }
        catch (Exception) { }
    }

    public static void RevealInFileManager(string path)
    {
        try { Process.Start(BuildRevealStartInfo(path, DetectPlatform())); }
        catch (Exception) { }
    }

    internal static Platform DetectPlatform() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Platform.Windows :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? Platform.MacOs :
        Platform.Linux;

    internal static ProcessStartInfo BuildOpenFolderStartInfo(string folderPath, Platform platform) => platform switch
    {
        Platform.Windows => new ProcessStartInfo("explorer.exe", $"\"{folderPath}\"") { UseShellExecute = false },
        Platform.MacOs => new ProcessStartInfo("open", $"\"{folderPath}\"") { UseShellExecute = false },
        _ => new ProcessStartInfo("xdg-open", $"\"{folderPath}\"") { UseShellExecute = false }
    };

    internal static ProcessStartInfo BuildRevealStartInfo(string filePath, Platform platform) => platform switch
    {
        Platform.Windows => new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = false },
        Platform.MacOs => new ProcessStartInfo("open", $"-R \"{filePath}\"") { UseShellExecute = false },
        // Nessun file manager Linux ha un flag "seleziona" universale: apre la cartella padre.
        _ => new ProcessStartInfo("xdg-open", $"\"{Path.GetDirectoryName(filePath)}\"") { UseShellExecute = false }
    };
}
