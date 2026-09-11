namespace Sbroglione.Models;

/// <summary>Istruzione prodotta da <see cref="Services.DeltaCopyScanner"/> per ricostruire il file.</summary>
public abstract record DeltaCopyInstruction;

/// <summary>Copia un blocco di <paramref name="Length"/> byte dal vecchio file di destinazione, a partire da <paramref name="DestOffset"/>.</summary>
public sealed record CopyBlockInstruction(long DestOffset, int Length) : DeltaCopyInstruction;

/// <summary>Scrive byte letti direttamente dalla sorgente (nessun blocco corrispondente trovato in destinazione).</summary>
public sealed record LiteralInstruction(byte[] Data) : DeltaCopyInstruction;

/// <summary>Un blocco della destinazione esistente indicizzato per la ricerca di corrispondenze.</summary>
public sealed record SignatureBlock(long Offset, int Length, byte[] StrongHash);
