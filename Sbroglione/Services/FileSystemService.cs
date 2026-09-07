using System;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Facciata pubblica delle operazioni sul file system. Implementazione divisa per
/// responsabilità in <see cref="FileSystemPathQueries"/>, <see cref="FileSystemListingQueries"/>,
/// <see cref="FileSystemMutations"/> ed <see cref="FileSystemErrors"/>: questa classe resta
/// l'unico punto pubblico chiamato da ViewModels/Views, per non propagare il dettaglio
/// dello split ai chiamanti.
/// </summary>
public static class FileSystemService
{
    /// <summary>
    /// Accessor sostituibile nei test (stesso pattern di
    /// <see cref="UpdateCheckService.CurrentVersionOverride"/>). Produzione: <see cref="DefaultFileSystemAccessor"/>.
    /// </summary>
    public static IFileSystemAccessor Accessor { get; set; } = new DefaultFileSystemAccessor();

    /// <summary>
    /// Ritorna il tipo di elemento corrispondente al percorso.
    /// </summary>
    public static PathType GetPathType(string? path) => FileSystemPathQueries.GetPathType(path);

    /// <summary>
    /// Ritorna il percorso della cartella superiore, o il percorso stesso se si è già alla radice.
    /// </summary>
    public static string? GetParentPath(string? path) => FileSystemPathQueries.GetParentPath(path);

    /// <summary>
    /// Variante asincrona di <see cref="GetPathType"/> (il controllo di esistenza su
    /// percorsi di rete irraggiungibili può bloccare per diversi secondi).
    /// </summary>
    public static Task<PathType> GetPathTypeAsync(string? path) =>
        Task.Run(() => GetPathType(path));

    /// <summary>
    /// Elenco asincrono del contenuto diretto di <paramref name="path"/>, con errore esplicito.
    /// </summary>
    public static Task<DirectoryListingResult> ListDirectoryAsync(string path, bool directoriesOnly) =>
        FileSystemListingQueries.ListDirectoryAsync(path, directoriesOnly);

    /// <summary>
    /// Elenco asincrono ricorsivo dei file sotto <paramref name="path"/>, con errore esplicito.
    /// </summary>
    public static Task<DirectoryListingResult> ListFilesRecursiveAsync(string path) =>
        FileSystemListingQueries.ListFilesRecursiveAsync(path);

    /// <summary>
    /// Crea la sottocartella <paramref name="name"/> dentro <paramref name="parentPath"/>. Null = successo.
    /// </summary>
    public static Task<ListingError?> CreateDirectoryAsync(string parentPath, string name) =>
        FileSystemMutations.CreateDirectoryAsync(parentPath, name);

    /// <summary>
    /// Rinomina il file o la cartella in <paramref name="path"/> in <paramref name="newName"/>,
    /// restando nella stessa cartella padre. Null = successo.
    /// </summary>
    public static Task<ListingError?> RenameAsync(string path, string newName) =>
        FileSystemMutations.RenameAsync(path, newName);

    /// <summary>
    /// Elimina il file o la cartella (ricorsivamente) in <paramref name="path"/>. Null = successo.
    /// Nessun cestino: cancellazione diretta, la conferma va chiesta prima dal chiamante.
    /// </summary>
    public static Task<ListingError?> DeleteAsync(string path) =>
        FileSystemMutations.DeleteAsync(path);

    /// <summary>
    /// True se il percorso è in forma UNC (<c>\\server\condivisione</c>).
    /// </summary>
    public static bool IsUncPath(string? path) => FileSystemPathQueries.IsUncPath(path);

    /// <summary>
    /// Estrae la radice UNC (<c>\\server\condivisione</c>, senza sottocartelle) da un
    /// percorso UNC. Null se <paramref name="path"/> non è UNC o non ha almeno server+condivisione
    /// (es. <c>\\server</c> da solo).
    /// </summary>
    public static string? GetUncRoot(string? path) => FileSystemPathQueries.GetUncRoot(path);

    /// <summary>Solo per i test: sostituisce <see cref="CheckUncRootAccessAsync"/>. Ripristinare a null in Dispose.</summary>
    internal static Func<string, Task<UncAccessResult>>? CheckUncRootAccessOverride { get; set; }

    /// <summary>
    /// Prova ad accedere alla radice UNC (che esiste sempre come cartella, a differenza di un
    /// eventuale sottopercorso di destinazione non ancora creato): distingue un accesso negato
    /// (credenziali mancanti/scadute) da qualunque altro problema, cosa che <c>Directory.Exists</c>
    /// non permette (ritorna false in entrambi i casi).
    /// </summary>
    public static Task<UncAccessResult> CheckUncRootAccessAsync(string uncRoot) =>
        CheckUncRootAccessOverride is { } fake ? fake(uncRoot) : FileSystemPathQueries.CheckUncRootAccessAsync(uncRoot);

    /// <summary>
    /// Traduce un'eccezione di I/O in un <see cref="ListingError"/> presentabile.
    /// </summary>
    public static ListingError CreateListingError(Exception exception) => FileSystemErrors.Create(exception);
}
