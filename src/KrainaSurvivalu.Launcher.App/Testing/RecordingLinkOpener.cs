using System.Collections.ObjectModel;
using KrainaSurvivalu.Launcher.App.Services;

namespace KrainaSurvivalu.Launcher.App.Testing;

/// <summary>Developer smoke-test destination. Records strings and never starts a process or a browser.</summary>
public sealed class RecordingLinkOpener : IExternalLinkOpener
{
    private readonly List<string> _openedUrls = [];
    private readonly List<string> _attemptedUrls = [];
    public ReadOnlyCollection<string> OpenedUrls => _openedUrls.AsReadOnly();
    public ReadOnlyCollection<string> AttemptedUrls => _attemptedUrls.AsReadOnly();
    public bool FailNextOpen { get; set; }

    public void Open(string url)
    {
        _attemptedUrls.Add(url);
        if (FailNextOpen)
        {
            FailNextOpen = false;
            throw new InvalidOperationException("Kontrolowana awaria testowego otwierania linku. Przeglądarka nie została uruchomiona.");
        }
        _openedUrls.Add(url);
    }
}
