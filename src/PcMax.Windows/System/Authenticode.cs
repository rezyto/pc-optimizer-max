using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PcMax.Windows.Native;

namespace PcMax.Windows.SystemServices;

/// <summary>Authenticode checks for downloaded installers (WinVerifyTrust + signer certificate).</summary>
[SupportedOSPlatform("windows")]
public static class Authenticode
{
    /// <summary>True when the file has a valid Authenticode signature chaining to a trusted root (revocation checked).</summary>
    public static bool HasValidSignature(string path)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var fileInfoPtr = IntPtr.Zero;
        try
        {
            var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
                pcwszFilePath = pathPtr,
            };
            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new NativeMethods.WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_DATA>(),
                dwUIChoice = NativeMethods.WTD_UI_NONE,
                fdwRevocationChecks = NativeMethods.WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                pFile = fileInfoPtr,
                dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY,
                dwProvFlags = NativeMethods.WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT | NativeMethods.WTD_DISABLE_MD2_MD4,
            };
            var action = NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            var result = NativeMethods.WinVerifyTrust(new IntPtr(-1) /* INVALID_HANDLE_VALUE: no UI */, ref action, ref data);

            data.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
            NativeMethods.WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (result != 0) Log.Warn($"Assinatura de {Path.GetFileName(path)} inválida ou ausente (0x{result:X8}).");
            return result == 0;
        }
        finally
        {
            if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    /// <summary>SHA-256 thumbprint of the certificate that signed the file, or null when it carries no Authenticode signature.</summary>
    public static string? SignerThumbprint(string path)
    {
        try
        {
            using var cert = X509Certificate.CreateFromSignedFile(path);
            return cert.GetCertHashString(HashAlgorithmName.SHA256);
        }
        catch (CryptographicException) { return null; }
    }

    /// <summary>Subject of the signer certificate, for log messages.</summary>
    public static string? SignerSubject(string path)
    {
        try
        {
            using var cert = X509Certificate.CreateFromSignedFile(path);
            return cert.Subject;
        }
        catch (CryptographicException) { return null; }
    }

    /// <summary>
    /// Signature policy for an update: when <paramref name="runningExe"/> is signed, the installer must carry a valid
    /// signature from the same certificate (same SHA-256 thumbprint). When the running app is unsigned (no signing key
    /// yet), only the SHA-256 check done by the caller applies. Returns null when acceptable, else the reason.
    /// </summary>
    public static string? CheckUpdateSignature(string installer, string runningExe)
    {
        var expected = SignerThumbprint(runningExe);
        if (expected is null) return null; // unsigned build: hash check only
        if (!HasValidSignature(installer)) return "o instalador não tem uma assinatura digital válida";
        var actual = SignerThumbprint(installer);
        if (actual is null || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            return $"o instalador foi assinado por outro certificado ({SignerSubject(installer) ?? "desconhecido"})";
        return null;
    }
}
