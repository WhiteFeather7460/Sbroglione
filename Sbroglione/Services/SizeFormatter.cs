namespace Sbroglione.Services;

/// <summary>Formattazione di dimensioni in byte per la UI.</summary>
public static class SizeFormatter
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B"
    };
}
