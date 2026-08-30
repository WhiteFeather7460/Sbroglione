namespace Sbroglione.Services;

/// <summary>
/// Seam di accesso al file system: su desktop avvolge <c>System.IO</c> senza cambiare
/// comportamento (<see cref="DefaultFileSystemAccessor"/>); su Android una futura
/// implementazione (Fase 3, fuori scope qui — vedi Global Constraints) risolverà i
/// content:// URI ottenuti via Storage Access Framework. Introdotto ora per isolare il
/// punto di estensione senza ancora spostarci tutte le chiamate esistenti.
/// </summary>
public interface IFileSystemAccessor
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string[] EnumerateFileNames(string directoryPath);
    string[] EnumerateDirectoryNames(string directoryPath);
}
