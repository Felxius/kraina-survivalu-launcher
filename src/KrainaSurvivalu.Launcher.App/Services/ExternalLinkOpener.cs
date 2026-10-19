using System.Diagnostics;

namespace KrainaSurvivalu.Launcher.App.Services;

public sealed class ExternalLinkOpener : IExternalLinkOpener
{
    public void Open(string url)
    {
        var uri = ValidateUrl(url);
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }

    /// <summary>Validates a browser destination without opening a browser or making a request.</summary>
    public static Uri ValidateUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (url.Length > 2048 || url != url.Trim() || url.Any(char.IsControl) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Link musi być pełnym adresem HTTPS bez danych logowania i znaków sterujących.", nameof(url));

        return uri;
    }
}
