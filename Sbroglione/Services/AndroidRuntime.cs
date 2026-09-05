using System;

namespace Sbroglione.Services;

/// <summary>
/// True quando il processo corrente NON gira sul runtime Android — usato per nascondere in
/// XAML, via <c>{x:Static}</c>, UI desktop-only senza equivalente Android (es. la modalità
/// <c>OnChange</c> del watch-folder, inaffidabile su storage emulato FUSE).
/// </summary>
public static class AndroidRuntime
{
    public static bool IsAndroid { get; } = OperatingSystem.IsAndroid();

    public static bool IsNotAndroid { get; } = !OperatingSystem.IsAndroid();
}
