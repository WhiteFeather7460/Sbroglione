using System;

namespace Sbroglione.Models;

/// <summary>Voce (file o cartella) di un elenco remoto. Percorsi con separatore '/'.</summary>
public sealed record RemoteItem(string Name, string FullPath, bool IsDirectory, long Size, DateTime Modified);
