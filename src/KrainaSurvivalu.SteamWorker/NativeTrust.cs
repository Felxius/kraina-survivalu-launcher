using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace KrainaSurvivalu.SteamWorker;

internal static class NativeTrust
{
    internal static void RequireValveSignature(string path)
    {
        var name = Marshal.StringToHGlobalUni(path);
        var filePointer = IntPtr.Zero;
        try
        {
            var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = name };
            filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1,
                File = filePointer, StateAction = 1, ProviderFlags = 0x1010 // offline cached trust; no UI/network
            };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            data.StateAction = 2;
            _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (result != 0)
                throw new InvalidDataException("Nie udało się potwierdzić podpisu biblioteki Steam. Zweryfikuj pliki DayZ w Steam.");
            // This API extracts an Authenticode certificate from a PE file; the newer certificate
            // loaders only accept certificate encodings, not signed executable containers.
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var publisher = certificate.GetNameInfo(X509NameType.SimpleName, false);
            if (publisher is not ("Valve Corp." or "Valve Corporation"))
                throw new InvalidDataException("Biblioteka Steam nie jest podpisana przez Valve. Zweryfikuj pliki DayZ w Steam.");
        }
        finally
        {
            if (filePointer != IntPtr.Zero) Marshal.FreeHGlobal(filePointer);
            Marshal.FreeHGlobal(name);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustFile { internal uint Size; internal IntPtr Path, Handle, KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        internal uint Size;
        internal IntPtr Policy, Sip;
        internal uint UiChoice, RevocationChecks, UnionChoice;
        internal IntPtr File;
        internal uint StateAction;
        internal IntPtr StateData, UrlReference;
        internal uint ProviderFlags, UiContext;
        internal IntPtr SignatureSettings;
    }
}
