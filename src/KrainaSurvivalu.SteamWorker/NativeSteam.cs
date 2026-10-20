using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace KrainaSurvivalu.SteamWorker;

internal sealed class NativeSteam : IDisposable
{
    private const uint Subscribed = 1, Installed = 4, NeedsUpdate = 8, Downloading = 16, Pending = 32;
    private const int CallCompleted = 703, SubscribeResult = 1313, DownloadResult = 3406;
    private readonly IntPtr _library;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly List<string> _contentRoots;
    private readonly VoidDelegate _shutdown;
    private readonly PipeVoidDelegate _runFrame, _freeCallback;
    private readonly NextCallbackDelegate _nextCallback;
    private readonly ApiCallResultDelegate _getCallResult;
    private readonly ItemStateDelegate _itemState;
    private readonly SubscribeDelegate _subscribe;
    private readonly DownloadDelegate _download;
    private readonly InstallInfoDelegate _installInfo;
    private readonly DownloadInfoDelegate _downloadInfo;
    private readonly PointerBoolDelegate _loggedOn;
    private readonly IntPtr _ugc, _user;
    private readonly int _pipe;
    private bool _initialized;
    private bool _disposed;

    internal NativeSteam(string dayzDirectory, string steamExecutable)
    {
        _contentRoots = ContentRoots(dayzDirectory, steamExecutable);
        var nativePath = Path.GetFullPath(Path.Combine(dayzDirectory, "steam_api64.dll"));
        // Prevent replacement while checking signature and loading the exact installed file.
        using var lockFile = new FileStream(nativePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        NativeTrust.RequireValveSignature(nativePath);
        _library = NativeLibrary.Load(nativePath);
        try
        {
            _shutdown = Export<VoidDelegate>("SteamAPI_Shutdown");
            _runFrame = Export<PipeVoidDelegate>("SteamAPI_ManualDispatch_RunFrame");
            _freeCallback = Export<PipeVoidDelegate>("SteamAPI_ManualDispatch_FreeLastCallback");
            _nextCallback = Export<NextCallbackDelegate>("SteamAPI_ManualDispatch_GetNextCallback");
            _getCallResult = Export<ApiCallResultDelegate>("SteamAPI_ManualDispatch_GetAPICallResult");
            _itemState = Export<ItemStateDelegate>("SteamAPI_ISteamUGC_GetItemState");
            _subscribe = Export<SubscribeDelegate>("SteamAPI_ISteamUGC_SubscribeItem");
            _download = Export<DownloadDelegate>("SteamAPI_ISteamUGC_DownloadItem");
            _installInfo = Export<InstallInfoDelegate>("SteamAPI_ISteamUGC_GetItemInstallInfo");
            _downloadInfo = Export<DownloadInfoDelegate>("SteamAPI_ISteamUGC_GetItemDownloadInfo");
            _loggedOn = Export<PointerBoolDelegate>("SteamAPI_ISteamUser_BLoggedOn");
            if (!Export<BoolDelegate>("SteamAPI_IsSteamRunning")())
                throw new InvalidOperationException("Uruchom Steam na tym samym koncie Windows co launcher.");
            if (!Export<BoolDelegate>("SteamAPI_Init")())
                throw new InvalidOperationException("Nie udało się połączyć ze Steam w kontekście DayZ. Sprawdź zalogowanie, licencję DayZ i jednakowe uprawnienia obu aplikacji.");
            _initialized = true;
            _ugc = Interface("SteamAPI_SteamUGC_v", 24, 14);
            _user = Interface("SteamAPI_SteamUser_v", 24, 20);
            var utils = Interface("SteamAPI_SteamUtils_v", 12, 9);
            var apps = Interface("SteamAPI_SteamApps_v", 10, 8);
            if (Export<AppIdDelegate>("SteamAPI_ISteamUtils_GetAppID")(utils) != Program.DayZAppId ||
                !Export<SubscribedAppDelegate>("SteamAPI_ISteamApps_BIsSubscribedApp")(apps, Program.DayZAppId) ||
                !_loggedOn(_user))
                throw new InvalidOperationException("Steam musi być zalogowany na konto posiadające DayZ. Nie potwierdzono poprawnego kontekstu gry.");
            _pipe = Export<IntDelegate>("SteamAPI_GetHSteamPipe")();
            if (_pipe == 0) throw new InvalidOperationException("Steam nie udostępnił połączenia klienta.");
            Export<VoidDelegate>("SteamAPI_ManualDispatch_Init")();
            // Windows x64 Steam callback ABI is pack 8. Fail before dereferencing if changed.
            if (Marshal.SizeOf<CallbackMessage>() != 24 || Marshal.SizeOf<ApiCallCompleted>() != 16 ||
                Marshal.SizeOf<SubscribedFileResult>() != 16 || Marshal.SizeOf<ItemDownloadResult>() != 24)
                throw new InvalidOperationException("Nieobsługiwany układ danych biblioteki Steam.");
        }
        catch
        {
            if (_initialized) _shutdown?.Invoke();
            NativeLibrary.Free(_library);
            throw;
        }
    }

    internal WorkerEvent Inspect(ulong id)
    {
        EnsureOwner();
        var state = _itemState(_ugc, id);
        // GetItemInstallInfo marks an item as used. Inspection instead only reads the state and
        // known Steam library folders; an existing directory alone NEVER means up-to-date.
        var folder = (state & Installed) != 0 ? FindExistingFolder(id) : null;
        if (!_downloadInfo(_ugc, id, out var downloaded, out var total))
            downloaded = total = 0;
        return new WorkerEvent
        {
            Type = "item", Id = id, State = state, InstalledPath = folder,
            BytesDownloaded = downloaded, BytesTotal = total,
            Message = IsReady(state) && folder is not null ? "Gotowy w Steam." : "Mod wymaga synchronizacji lub sprawdzenia instalacji."
        };
    }

    internal void Synchronize(ulong id, Action<WorkerEvent> emit)
    {
        EnsureOwner();
        var state = _itemState(_ugc, id);
        if ((state & Subscribed) == 0)
        {
            emit(new WorkerEvent { Type = "progress", Id = id, State = state, Message = "Subskrybowanie modu w Steam…" });
            var handle = _subscribe(_ugc, id);
            if (handle == 0) throw new InvalidOperationException($"Steam odrzucił subskrypcję modu {id}.");
            var subscribed = false;
            WaitFor(TimeSpan.FromSeconds(60), message =>
            {
                if (message.Callback != CallCompleted || message.ParameterSize != Marshal.SizeOf<ApiCallCompleted>()) return;
                var complete = Marshal.PtrToStructure<ApiCallCompleted>(message.Parameter);
                if (complete.Handle != handle) return;
                if (complete.Callback != SubscribeResult || complete.Size != Marshal.SizeOf<SubscribedFileResult>())
                    throw new InvalidDataException("Steam zwrócił nieoczekiwany wynik subskrypcji.");
                var buffer = Marshal.AllocHGlobal((int)complete.Size);
                try
                {
                    if (!_getCallResult(_pipe, handle, buffer, (int)complete.Size, SubscribeResult, out var failed) || failed)
                        throw new IOException($"Steam nie potwierdził subskrypcji modu {id}.");
                    var result = Marshal.PtrToStructure<SubscribedFileResult>(buffer);
                    if (result.Id != id || result.Result != 1)
                        throw new IOException($"Subskrypcja modu {id} nie powiodła się (Steam: {result.Result}).");
                    subscribed = true;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }, () => subscribed);
        }

        state = _itemState(_ugc, id);
        // A completed subscription may already have triggered Steam's automatic background job.
        // Explicit DownloadItem(false) safely schedules/update-checks only this item, without
        // high-priority interruption or suspending any other Steam downloads.
        if (!IsReady(state))
        {
            emit(new WorkerEvent { Type = "progress", Id = id, State = state, Message = "Steam pobiera lub aktualizuje mod…" });
            if (!_download(_ugc, id, false)) throw new IOException($"Steam nie rozpoczął pobierania modu {id}.");
            var completed = false;
            var lastProgress = Stopwatch.StartNew();
            WaitFor(TimeSpan.FromMinutes(30), message =>
            {
                if (message.Callback != DownloadResult || message.ParameterSize != Marshal.SizeOf<ItemDownloadResult>()) return;
                var result = Marshal.PtrToStructure<ItemDownloadResult>(message.Parameter);
                if (result.AppId != Program.DayZAppId || result.Id != id) return;
                if (result.Result != 1) throw new IOException($"Pobieranie modu {id} nie powiodło się (Steam: {result.Result}).");
                completed = true;
            }, () =>
            {
                if (lastProgress.Elapsed >= TimeSpan.FromMilliseconds(500))
                {
                    if (!_downloadInfo(_ugc, id, out var done, out var total))
                        done = total = 0;
                    emit(new WorkerEvent { Type = "progress", Id = id, State = _itemState(_ugc, id),
                        BytesDownloaded = done, BytesTotal = total, Message = "Pobieranie lub instalowanie przez Steam…" });
                    lastProgress.Restart();
                }
                return completed;
            });
        }

        // DownloadItem(true return) alone is never success. Wait for the callback above, then
        // re-check Steam's official bits and ask Steam for its installed folder.
        state = _itemState(_ugc, id);
        if (!IsReady(state)) throw new IOException($"Steam nie potwierdził gotowości modu {id} po zakończeniu operacji.");
        var pathBuffer = new byte[32768];
        if (!_installInfo(_ugc, id, out _, pathBuffer, (uint)pathBuffer.Length, out _))
            throw new IOException($"Steam nie podał folderu zainstalowanego modu {id}.");
        var terminator = Array.IndexOf(pathBuffer, (byte)0);
        if (terminator <= 0) throw new InvalidDataException("Steam zwrócił nieprawidłowy folder modu.");
        var path = ValidateFolder(new UTF8Encoding(false, true).GetString(pathBuffer, 0, terminator), id);
        emit(new WorkerEvent { Type = "item", Id = id, State = state, InstalledPath = path,
            Success = true, Message = "Mod jest gotowy w Steam." });
    }

    private void WaitFor(TimeSpan timeout, Action<CallbackMessage> callback, Func<bool> completed)
    {
        var timer = Stopwatch.StartNew();
        while (!completed())
        {
            EnsureOwner();
            if (!_loggedOn(_user)) throw new IOException("Utracono połączenie ze Steam. Zaloguj się ponownie.");
            if (timer.Elapsed >= timeout) throw new TimeoutException("Przekroczono czas oczekiwania na Steam. Pobieranie może być dalej widoczne w kliencie Steam.");
            _runFrame(_pipe);
            var received = 0;
            while (_nextCallback(_pipe, out var message))
            {
                try
                {
                    if (message.ParameterSize < 0 || message.ParameterSize > 1024 * 1024 ||
                        (message.ParameterSize > 0 && message.Parameter == IntPtr.Zero))
                        throw new InvalidDataException("Steam zwrócił nieprawidłowy komunikat.");
                    callback(message);
                }
                finally { _freeCallback(_pipe); }
                if (++received >= 10000) throw new InvalidDataException("Przekroczono limit komunikatów Steam.");
            }
            Thread.Sleep(50);
        }
    }

    private static bool IsReady(uint state) => (state & Installed) != 0 && (state & (NeedsUpdate | Downloading | Pending)) == 0;

    private string? FindExistingFolder(ulong id)
    {
        foreach (var root in _contentRoots)
        {
            var folder = Path.Combine(root, id.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(folder)) return ValidateFolder(folder, id);
        }
        return null;
    }

    private string ValidateFolder(string folder, ulong id)
    {
        if (!Path.IsPathFullyQualified(folder) || folder.Any(char.IsControl))
            throw new InvalidDataException("Steam zwrócił nieprawidłową ścieżkę modu.");
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!_contentRoots.Any(root => string.Equals(canonical, Path.Combine(root, id.ToString(CultureInfo.InvariantCulture)), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Folder modu znajduje się poza biblioteką Steam Workshop DayZ.");
        // Do not accept junctions/symlinks that redirect an apparently valid content path elsewhere.
        for (var directory = new DirectoryInfo(canonical); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Folder Workshop zawiera przekierowanie. Wymagana jest zwykła lokalna biblioteka Steam.");
        if (!Directory.Exists(canonical)) throw new DirectoryNotFoundException("Zainstalowany folder modu nie istnieje.");
        return canonical;
    }

    private static List<string> ContentRoots(string dayz, string steam)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gameApps = Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dayz)))?.Parent;
        if (gameApps is not null && string.Equals(gameApps.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
            roots.Add(Path.Combine(gameApps.FullName, "workshop", "content", "221100"));
        roots.Add(Path.Combine(Path.GetDirectoryName(steam)!, "steamapps", "workshop", "content", "221100"));
        return roots.Select(Path.GetFullPath).ToList();
    }

    private IntPtr Interface(string prefix, int max, int min)
    {
        for (var version = max; version >= min; version--)
            if (NativeLibrary.TryGetExport(_library, prefix + version.ToString("000", CultureInfo.InvariantCulture), out var address))
            {
                var instance = Marshal.GetDelegateForFunctionPointer<PointerDelegate>(address)();
                if (instance != IntPtr.Zero) return instance;
            }
        throw new NotSupportedException("Zainstalowana biblioteka Steam nie udostępnia wymaganego interfejsu. Zweryfikuj pliki DayZ w Steam.");
    }

    private T Export<T>(string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_library, name, out var address))
            throw new NotSupportedException($"Zainstalowana biblioteka Steam nie obsługuje funkcji {name}. Zweryfikuj pliki DayZ w Steam.");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private void EnsureOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Interfejs Steam musi pracować w jednym wątku pomocnika.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        EnsureOwner();
        _disposed = true;
        if (_initialized) _shutdown();
        NativeLibrary.Free(_library);
    }

    // ABI definitions checked against Valve's steam_api.h / isteamugc.h and the maintainer's
    // generated bindings (Windows pack 8; bool is one byte):
    // https://partner.steamgames.com/doc/sdk/api#manual_callback_dispatch
    // https://github.com/rlabrecque/Steamworks.NET/blob/master/com.rlabrecque.steamworks.net/Runtime/autogen/SteamCallbacks.cs
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct CallbackMessage { internal int User; internal int Callback; internal IntPtr Parameter; internal int ParameterSize; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct ApiCallCompleted { internal ulong Handle; internal int Callback; internal uint Size; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct SubscribedFileResult { internal int Result; internal ulong Id; }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct ItemDownloadResult { internal uint AppId; internal ulong Id; internal int Result; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool BoolDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PointerDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PipeVoidDelegate(int pipe);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool PointerBoolDelegate(IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AppIdDelegate(IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool SubscribedAppDelegate(IntPtr instance, uint appId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ItemStateDelegate(IntPtr instance, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong SubscribeDelegate(IntPtr instance, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool DownloadDelegate(IntPtr instance, ulong id, [MarshalAs(UnmanagedType.I1)] bool priority);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool DownloadInfoDelegate(IntPtr instance, ulong id, out ulong done, out ulong total);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool InstallInfoDelegate(IntPtr instance, ulong id, out ulong size, [Out] byte[] folder, uint folderSize, out uint timestamp);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool NextCallbackDelegate(int pipe, out CallbackMessage message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool ApiCallResultDelegate(int pipe, ulong handle, IntPtr data, int size, int callback, [MarshalAs(UnmanagedType.I1)] out bool failed);
}
