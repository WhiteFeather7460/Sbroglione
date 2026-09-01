using System;

namespace Sbroglione.Services;

/// <summary>
/// Radice di partenza per la navigazione locale. Su desktop coincide con la home utente;
/// su Android, dove <see cref="Environment.SpecialFolder.UserProfile"/> risolve alla
/// sandbox dell'app e non allo storage condiviso, l'head project imposta
/// <see cref="DefaultRootPathOverride"/> prima che la UI si costruisca — stesso pattern di
/// <c>App.StartBackgroundWatchHost</c>.
/// </summary>
public static class PlatformPaths
{
    public static Func<string>? DefaultRootPathOverride { get; set; }

    public static string DefaultRootPath =>
        DefaultRootPathOverride?.Invoke()
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
