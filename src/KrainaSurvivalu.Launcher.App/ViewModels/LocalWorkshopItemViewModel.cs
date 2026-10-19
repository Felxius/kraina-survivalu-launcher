using KrainaSurvivalu.Launcher.Core;

namespace KrainaSurvivalu.Launcher.App.ViewModels;

public sealed record LocalWorkshopItemViewModel(WorkshopItemSnapshot Snapshot)
{
    public string WorkshopId => Snapshot.WorkshopId;
    public string Name => Snapshot.Name;
    public string StatusText => Snapshot.StatusText;
    public string InstalledPath => Snapshot.InstalledPath ?? "Ścieżka instalacji niepotwierdzona";
    public bool IsReady => Snapshot.IsReady;
    public double DownloadPercent => Snapshot.BytesTotal > 0
        ? Math.Clamp(100.0 * Snapshot.BytesDownloaded / Snapshot.BytesTotal, 0, 100) : 0;
    public string DownloadText => Snapshot.BytesTotal > 0
        ? $"{FormatBytes(Snapshot.BytesDownloaded)} / {FormatBytes(Snapshot.BytesTotal)}" : "Rozmiar nie został jeszcze podany przez Steam";
    internal static string FormatBytes(ulong bytes) => bytes >= 1_073_741_824
        ? $"{bytes / 1_073_741_824.0:0.0} GB" : $"{bytes / 1_048_576.0:0.0} MB";
}
