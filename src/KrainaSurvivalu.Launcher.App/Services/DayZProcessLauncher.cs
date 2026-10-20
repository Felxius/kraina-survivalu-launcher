using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using KrainaSurvivalu.Launcher.Core;
using Microsoft.Win32.SafeHandles;

namespace KrainaSurvivalu.Launcher.App.Services;

/// <summary>
/// Starts only the installed BattlEye bootstrap. It does not inject into the game, change its
/// files, elevate privileges, terminate Steam/DayZ, or equate a created process with a connection.
/// </summary>
public sealed class DayZProcessLauncher : IGameProcessLauncher
{
    private static readonly string[] GameProcessNames = ["DayZ_x64", "DayZ_BE", "DayZDiag_x64"];

    public ValidationResult CheckAvailability(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!OperatingSystem.IsWindows()) return new(false, "Uruchamianie DayZ jest dostępne wyłącznie w Windows.");
        var paths = PathValidation.Validate(settings);
        if (!paths.IsValid) return paths;
        try
        {
            foreach (var name in GameProcessNames)
            {
                var processes = Process.GetProcessesByName(name);
                try
                {
                    if (processes.Any(process => !process.HasExited))
                        return new(false, "DayZ lub BattlEye jest już uruchomione. Zamknij grę przed ponownym wybraniem GRAJ.");
                }
                finally { foreach (var process in processes) process.Dispose(); }
            }

            var steamProcesses = Process.GetProcessesByName("steam");
            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                using var currentIdentity = WindowsIdentity.GetCurrent();
                var currentElevation = ReadElevation(currentProcess.Id);
                var expectedSteam = Path.GetFullPath(settings.SteamExecutable);
                var inaccessible = false;
                foreach (var steam in steamProcesses)
                {
                    try
                    {
                        if (steam.HasExited || steam.SessionId != currentProcess.SessionId) continue;
                        var actualPath = steam.MainModule?.FileName;
                        if (actualPath is null || !string.Equals(Path.GetFullPath(actualPath), expectedSteam,
                                StringComparison.OrdinalIgnoreCase)) continue;
                        using var token = OpenToken(steam.Id);
                        using var steamIdentity = new WindowsIdentity(token.DangerousGetHandle());
                        if (currentIdentity.User != steamIdentity.User || ReadElevation(token) != currentElevation)
                            return new(false, "Steam i launcher muszą działać na tym samym koncie Windows i z takim samym poziomem uprawnień. Uruchom oba normalnie, bez opcji administratora.");
                        return new(true, "Steam działa. Po synchronizacji zostanie uruchomione DayZ z BattlEye.");
                    }
                    catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
                    { inaccessible = true; }
                }
                return new(false, inaccessible
                    ? "Nie można potwierdzić uruchomionego Steam. Uruchom Steam i launcher na tym samym koncie Windows, z takim samym poziomem uprawnień."
                    : "Uruchom wybraną instalację Steam i zaloguj się na konto posiadające DayZ, a następnie ponownie wybierz GRAJ.");
            }
            finally { foreach (var steam in steamProcesses) steam.Dispose(); }
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(false, "Nie udało się sprawdzić procesów Steam i DayZ. " + error.Message);
        }
    }

    public Task<GameLaunchResult> LaunchAsync(DayZLaunchPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var start = CreateStartInfo(plan);
            var availability = CheckAvailability(new LauncherSettings
            {
                DayZDirectory = start.WorkingDirectory,
                SteamExecutable = plan.SteamExecutable
            });
            if (!availability.IsValid) return Task.FromResult(new GameLaunchResult(false, availability.Message));
            cancellationToken.ThrowIfCancellationRequested();
            // Once created, the user's game is never terminated by cancellation or launcher shutdown.
            using var process = Process.Start(start);
            if (process is null)
                return Task.FromResult(new GameLaunchResult(false, "Windows nie potwierdził uruchomienia BattlEye."));
            return Task.FromResult(new GameLaunchResult(true,
                "Uruchomiono DayZ przez BattlEye. O połączeniu z serwerem poinformuje gra.", process.Id));
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 740)
        {
            return Task.FromResult(new GameLaunchResult(false,
                "Windows wymaga podwyższonych uprawnień dla pliku gry. Launcher nie podnosi uprawnień automatycznie. Sprawdź ustawienia zgodności DayZ_BE.exe oraz DayZ_x64.exe."));
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException)
        {
            return Task.FromResult(new GameLaunchResult(false, "Nie udało się uruchomić DayZ z BattlEye. " + error.Message));
        }
    }

    /// <summary>Also used by the offline smoke test: constructing this object starts no process.</summary>
    internal static ProcessStartInfo CreateStartInfo(DayZLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.WorkingDirectory) || !Path.IsPathFullyQualified(plan.WorkingDirectory) ||
            string.IsNullOrWhiteSpace(plan.ExecutablePath) || !Path.IsPathFullyQualified(plan.ExecutablePath))
            throw new ArgumentException("Plan uruchomienia musi zawierać pełne ścieżki do gry.");
        var directory = Path.GetFullPath(plan.WorkingDirectory);
        var executable = Path.GetFullPath(plan.ExecutablePath);
        if (!string.Equals(executable, Path.Combine(directory, "DayZ_BE.exe"), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Dozwolonym punktem startowym gry jest wyłącznie DayZ_BE.exe z wybranego folderu DayZ.");
        if (plan.Arguments is null || plan.Arguments.Count < 5 ||
            !plan.Arguments.Take(5).SequenceEqual(["0", "1", "0", "-exe", "DayZ_x64.exe"], StringComparer.Ordinal) ||
            plan.Arguments.Any(argument => argument is null || argument.Any(char.IsControl)))
            throw new ArgumentException("Plan nie zawiera prawidłowych parametrów BattlEye.");
        if (plan.Arguments.Skip(5).Any(argument => argument.Equals("-exe", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("-exe=", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("-noBattlEye", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("-filePatching", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Plan zawiera niedozwolony parametr startowy.");
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        foreach (var argument in plan.Arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static SafeAccessTokenHandle OpenToken(int processId)
    {
        using var process = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process.IsInvalid || !OpenProcessToken(process, 0x0008, out var token)) // TOKEN_QUERY
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return token;
    }

    private static uint ReadElevation(int processId)
    {
        using var token = OpenToken(processId);
        return ReadElevation(token);
    }

    private static uint ReadElevation(SafeAccessTokenHandle token)
    {
        if (!GetTokenInformation(token, 20, out var elevation, sizeof(uint), out _)) // TokenElevation
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return elevation;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle processHandle, uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle tokenHandle, int tokenInformationClass,
        out uint tokenInformation, uint tokenInformationLength, out uint returnLength);
}
