namespace Sbroglione.Services;

/// <summary>
/// Weak checksum a due livelli per il rolling hash stile rsync: a = somma dei byte,
/// b = somma pesata per posizione (peso 1 al primo byte della finestra, crescente).
/// Non è il rolling checksum "ufficiale" di rsync (pesi in ordine inverso), ma è
/// auto-consistente: AddByte costruisce la finestra iniziale, Roll la fa scorrere
/// di un byte con aggiornamento O(1). Va bene per il nostro scopo (trovare
/// candidati locali, confermati poi da SHA-256): non deve essere bit-compatibile
/// con l'algoritmo originale.
/// </summary>
public sealed class RollingChecksum
{
    private const long Mod = 1 << 16;

    private long _a;
    private long _b;
    private int _length;

    /// <summary>Valore corrente: 16 bit bassi = a, 16 bit alti = b.</summary>
    public uint Value => (uint)((_b << 16) | _a);

    /// <summary>Aggiunge un byte in coda alla finestra (usato per costruirla dall'inizio).</summary>
    public void AddByte(byte value)
    {
        _length++;
        _a = (_a + value) % Mod;
        _b = (_b + (long)_length * value) % Mod;
    }

    /// <summary>
    /// Fa scorrere la finestra di un byte: rimuove <paramref name="outgoing"/> dall'inizio,
    /// aggiunge <paramref name="incoming"/> in coda. La lunghezza della finestra non cambia.
    /// </summary>
    public void Roll(byte outgoing, byte incoming)
    {
        long oldA = _a;
        _a = Mod2(_a - outgoing + incoming);
        _b = Mod2(_b - oldA + (long)_length * incoming);
    }

    public void Reset()
    {
        _a = 0;
        _b = 0;
        _length = 0;
    }

    private static long Mod2(long value)
    {
        long m = value % Mod;
        return m < 0 ? m + Mod : m;
    }
}
