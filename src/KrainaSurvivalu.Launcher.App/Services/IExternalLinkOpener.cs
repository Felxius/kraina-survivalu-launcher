namespace KrainaSurvivalu.Launcher.App.Services;

/// <summary>Opens an approved community link in the user's default browser.</summary>
public interface IExternalLinkOpener
{
    void Open(string url);
}
