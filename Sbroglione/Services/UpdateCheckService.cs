using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Sbroglione.Models;

namespace Sbroglione.Services;

/// <summary>
/// Controlla la GitHub Releases API del repo per una versione più recente di quella
/// dell'assembly corrente. Nessun DI container nel progetto: <see cref="Client"/>,
/// <see cref="CurrentVersionOverride"/> e <see cref="PlatformAssetSuffixOverride"/> sono
/// seam di test statici (stesso pattern di <see cref="UiDispatch.Override"/>).
/// </summary>
public static class UpdateCheckService
{
    private const string ReleasesUrl = "https://api.github.com/repos/WhiteFeather7460/Sbroglione/releases/latest";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Client HTTP usato per la richiesta; sovrascrivibile nei test con un handler finto.</summary>
    public static HttpClient Client { get; set; } = new();

    /// <summary>Se impostata, sostituisce la versione letta dall'assembly corrente (seam di test).</summary>
    public static Version? CurrentVersionOverride { get; set; }

    /// <summary>
    /// Se impostata, sostituisce il suffisso asset dedotto dall'OS corrente (".exe"/".AppImage"/null)
    /// — seam di test per non dipendere dalla piattaforma reale su cui girano i test.
    /// </summary>
    public static string? PlatformAssetSuffixOverride { get; set; } = ComputeDefaultPlatformAssetSuffix();

    private static string? ComputeDefaultPlatformAssetSuffix()
    {
        if (OperatingSystem.IsWindows())
            return ".exe";
        if (OperatingSystem.IsLinux())
            return ".AppImage";
        return null;
    }

    /// <summary>Interroga GitHub e confronta con la versione corrente. Non lancia mai: gli errori tornano come <see cref="UpdateCheckStatus.Error"/>.</summary>
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.UserAgent.ParseAdd("Sbroglione-Updater");

            using HttpResponseMessage response = await Client.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            GitHubReleaseDto? dto = JsonSerializer.Deserialize<GitHubReleaseDto>(json, JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.TagName))
                return new UpdateCheckResult(UpdateCheckStatus.Error, null, "Risposta GitHub senza tag_name");

            if (!TryParseVersion(dto.TagName, out Version? remoteVersion))
                return new UpdateCheckResult(UpdateCheckStatus.Error, null, $"Tag versione non valido: {dto.TagName}");

            Version current = CurrentVersionOverride
                ?? Assembly.GetEntryAssembly()?.GetName().Version
                ?? new Version(1, 0, 0, 0);

            if (remoteVersion! <= current)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null);

            string? suffix = PlatformAssetSuffixOverride;
            GitHubAssetDto? asset = suffix is null
                ? null
                : dto.Assets?.FirstOrDefault(a => a.Name is not null && a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

            var info = new UpdateInfo(remoteVersion, dto.HtmlUrl ?? string.Empty, asset?.BrowserDownloadUrl, asset?.Name);
            return new UpdateCheckResult(UpdateCheckStatus.Available, info, null);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Error, null, ex.Message);
        }
    }

    private static bool TryParseVersion(string tag, out Version? version)
    {
        string trimmed = tag.Length > 0 && (tag[0] == 'v' || tag[0] == 'V') ? tag[1..] : tag;
        return Version.TryParse(trimmed, out version);
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAssetDto[]? Assets { get; set; }
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
