namespace KrainaSurvivalu.Launcher.Core;

public static class PathValidation
{
    public static ValidationResult Validate(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var contentError = ValidateContent(settings);
        if (contentError is not null)
            return new(false, contentError);
        if (string.IsNullOrWhiteSpace(settings.DayZDirectory))
            return new(false, "Wskaż folder instalacji DayZ.");
        if (!Directory.Exists(settings.DayZDirectory))
            return new(false, "Folder instalacji DayZ nie istnieje lub jest niedostępny.");
        if (!File.Exists(Path.Combine(settings.DayZDirectory, "DayZ_x64.exe")) ||
            !File.Exists(Path.Combine(settings.DayZDirectory, "DayZ_BE.exe")))
            return new(false, "W wybranym folderze brakuje DayZ_x64.exe lub DayZ_BE.exe. Wskaż główny folder gry.");
        if (string.IsNullOrWhiteSpace(settings.SteamExecutable))
            return new(false, "Wskaż plik steam.exe.");
        if (!string.Equals(Path.GetFileName(settings.SteamExecutable), "steam.exe", StringComparison.OrdinalIgnoreCase))
            return new(false, "Ścieżka Steam musi wskazywać plik steam.exe.");
        if (!File.Exists(settings.SteamExecutable))
            return new(false, "Plik steam.exe nie istnieje lub jest niedostępny.");
        return new(true, "Ścieżki do DayZ i Steam są poprawne.");
    }

    internal static string? ValidateContent(LauncherSettings settings)
    {
        if (settings.DayZDirectory is null || settings.SteamExecutable is null)
            return "Ścieżki w ustawieniach nie mogą mieć wartości null.";
        foreach (var value in new[] { settings.DayZDirectory, settings.SteamExecutable })
        {
            if (value.Length > 32767 || value.Any(char.IsControl) || value != value.Trim())
                return "Ścieżka zawiera niedozwolone znaki albo spacje na początku lub końcu.";
            if (value.Length > 0)
            {
                try
                {
                    if (!Path.IsPathFullyQualified(value))
                        return "Ustawienia muszą zawierać pełne ścieżki do plików i folderów.";
                    _ = Path.GetFullPath(value);
                    if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                        return "Ścieżka zawiera niedozwolone znaki.";
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return "Ścieżka ma nieprawidłowy format.";
                }
            }
        }
        return null;
    }
}
