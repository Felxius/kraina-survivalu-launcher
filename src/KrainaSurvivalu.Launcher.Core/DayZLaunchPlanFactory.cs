using System.Globalization;

namespace KrainaSurvivalu.Launcher.Core;

public static class DayZLaunchPlanFactory
{
    public static ValidationResult ValidateSettingsAndServer(LauncherSettings settings, ServerDefinition server)
    {
        var paths = PathValidation.Validate(settings);
        if (!paths.IsValid) return paths;
        var name = PlayerNameValidation.Validate(settings.PlayerName);
        if (!name.IsValid) return name;
        try { ServerCatalog.Validate(server); }
        catch (InvalidDataException exception) { return new(false, exception.Message); }
        return new(true, "Ustawienia gry są poprawne.");
    }

    public static DayZLaunchPlan Create(LauncherSettings settings, ServerDefinition server, WorkshopPreparationResult preparation)
    {
        var validation = ValidateSettingsAndServer(settings, server);
        if (!validation.IsValid) throw new InvalidOperationException(validation.Message);
        if (!preparation.AllAdvertisedItemsReady || preparation.ServerMods is not
            { State: ServerModsQueryState.Available, Mods: not null } metadata || preparation.Workshop is null)
            throw new InvalidOperationException("Brak potwierdzonej, aktualnej listy gotowych modów.");
        if (metadata.Mods.Any(mod => !mod.HasWorkshopId))
            throw new InvalidOperationException("Serwer zgłasza mod bez ID Workshop. Administracja musi uzupełnić dane.");

        var required = metadata.Mods.GroupBy(mod => NumericId(mod.WorkshopId)).Select(group => group.First()).ToArray();
        var installed = new Dictionary<ulong, WorkshopItemSnapshot>();
        foreach (var item in preparation.Workshop.Items)
        {
            if (!new ServerWorkshopMod(item.Name, item.WorkshopId).HasWorkshopId || !item.IsReady)
                throw new InvalidOperationException("Steam nie potwierdził gotowości wszystkich modów.");
            var id = NumericId(item.WorkshopId);
            if (installed.TryGetValue(id, out var previous))
            {
                if (!string.Equals(previous.InstalledPath, item.InstalledPath, StringComparison.OrdinalIgnoreCase) || previous.State != item.State)
                    throw new InvalidOperationException("Steam zwrócił sprzeczne ścieżki lub stany moda.");
            }
            else installed.Add(id, item);
        }
        if (!required.Select(mod => NumericId(mod.WorkshopId)).ToHashSet().SetEquals(installed.Keys))
            throw new InvalidOperationException("Potwierdzone mody nie odpowiadają aktualnej liście serwera.");

        var modPaths = required.Select(mod => ValidateWorkshopPath(settings, installed[NumericId(mod.WorkshopId)])).ToArray();
        var args = new List<string>
        {
            // Same bootstrap switches as the installed official DayZ launcher (no BattlEye log pipe).
            "0", "1", "0", "-exe", "DayZ_x64.exe",
            "-connect=" + server.Host,
            "-port=" + server.GamePort.ToString(CultureInfo.InvariantCulture),
            DayZLaunchArguments.CreatePlayerNameArgument(settings.PlayerName)
        };
        if (modPaths.Length > 0) args.Add("-mod=" + string.Join(';', modPaths));
        // Values cannot contain quotes or end with a backslash: allow two wrapping quotes,
        // one separator per argument, and the terminator. Windows counts UTF-16 units, not bytes.
        var executable = Path.GetFullPath(Path.Combine(settings.DayZDirectory, "DayZ_BE.exe"));
        if (args.Sum(argument => (long)argument.Length + 3) + executable.Length + 3 > 32767)
            throw new InvalidOperationException("Lista ścieżek modów jest zbyt długa. Przenieś bibliotekę Steam do krótszej ścieżki.");
        var warning = metadata.Flags == 0 ? null :
            "Serwer zwraca nieopisane flagi listy modów. Użyto wszystkich odczytanych ID; kompletność listy nie jest potwierdzona.";
        return new(executable,
            Path.GetFullPath(settings.DayZDirectory), Path.GetFullPath(settings.SteamExecutable), args.AsReadOnly(), warning);
    }

    private static string ValidateWorkshopPath(LauncherSettings settings, WorkshopItemSnapshot item)
    {
        var path = item.InstalledPath!;
        if (!Path.IsPathFullyQualified(path) || path != path.Trim() || path.Any(char.IsControl) || path.Contains(';') || path.Contains('"'))
            throw new InvalidOperationException($"Mod „{item.Name}” ma nieobsługiwaną ścieżkę. Usuń średniki i cudzysłowy z nazwy biblioteki Steam.");
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var id = NumericId(item.WorkshopId).ToString(CultureInfo.InvariantCulture);
        var roots = new List<string> { Path.Combine(Path.GetDirectoryName(settings.SteamExecutable)!, "steamapps") };
        var gameParent = Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.DayZDirectory)));
        if (gameParent is not null && string.Equals(gameParent.Name, "common", StringComparison.OrdinalIgnoreCase) &&
            gameParent.Parent is { } steamapps && string.Equals(steamapps.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
            roots.Add(steamapps.FullName);
        if (!roots.Any(root => string.Equals(fullPath, Path.GetFullPath(Path.Combine(root, "workshop", "content", "221100", id)), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Ścieżka moda „{item.Name}” nie należy do właściwej biblioteki Workshop DayZ.");
        for (var current = new DirectoryInfo(fullPath); current is not null; current = current.Parent)
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Folder moda „{item.Name}” jest niedostępny albo jest dowiązaniem. Sprawdź go w Steam.");
        var addons = Path.Combine(fullPath, "addons");
        if (!Directory.Exists(addons) || (File.GetAttributes(addons) & FileAttributes.ReparsePoint) != 0 ||
            !Directory.EnumerateFiles(addons, "*.pbo", SearchOption.TopDirectoryOnly).Any(file =>
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0))
            throw new InvalidOperationException($"W folderze moda „{item.Name}” brakuje plików gry. Sprawdź pliki moda w Steam.");
        return fullPath;
    }

    private static ulong NumericId(string id) => ulong.Parse(id, CultureInfo.InvariantCulture);
}
