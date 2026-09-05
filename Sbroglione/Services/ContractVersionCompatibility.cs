using System;
using System.Linq;

namespace Sbroglione.Services;

/// <summary>
/// Confronta la <c>ContractVersion</c> dichiarata da un plugin con quella supportata dall'host,
/// per compatibilità solo sul major SemVer (nessuna modifica breaking a <c>ITabPlugin</c> senza
/// bump di major).
/// </summary>
public static class ContractVersionCompatibility
{
    public static bool IsCompatible(string manifestContractVersion, int supportedMajor) =>
        Version.TryParse(NormalizeForVersionParse(manifestContractVersion), out Version? parsed)
        && parsed.Major == supportedMajor;

    /// <summary><see cref="Version.TryParse"/> richiede almeno major.minor: un solo numero non basta.</summary>
    private static string NormalizeForVersionParse(string value) =>
        value.Count(c => c == '.') >= 1 ? value : value + ".0";
}
