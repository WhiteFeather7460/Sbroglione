using System;
using System.Threading;

namespace Sbroglione.Services;

/// <summary>
/// Clamp monotono thread-safe per i valori cumulativi di avanzamento (byte copiati,
/// file processati, frazioni 0..1).
/// <para>
/// Nei servizi fan-out l'incremento del contatore e l'invocazione del callback non sono
/// atomici tra loro: due thread possono consegnare i cumulativi fuori ordine (prima 6, poi 5).
/// TryAdvance accetta un valore solo se è maggiore o uguale all'ultimo accettato, così
/// l'avanzamento pubblicato non torna mai indietro. Un'istanza per operazione.
/// </para>
/// </summary>
public sealed class MonotonicProgressGate
{
    private long _lastBits = BitConverter.DoubleToInt64Bits(double.NegativeInfinity);

    /// <summary>
    /// True se <paramref name="value"/> non è inferiore all'ultimo valore accettato
    /// (e in tal caso lo registra come nuovo massimo); false se è un cumulativo stantio.
    /// </summary>
    public bool TryAdvance(double value)
    {
        while (true)
        {
            long lastBits = Interlocked.Read(ref _lastBits);
            if (value < BitConverter.Int64BitsToDouble(lastBits))
                return false;
            if (Interlocked.CompareExchange(ref _lastBits, BitConverter.DoubleToInt64Bits(value), lastBits) == lastBits)
                return true;
        }
    }
}
