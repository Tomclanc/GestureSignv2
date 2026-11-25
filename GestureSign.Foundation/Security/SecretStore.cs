using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GestureSign.Foundation.Security;

/// <summary>
/// Encrypts secrets such as AI API keys for storage in settings files, with Windows DPAPI
/// for the current user: only the same Windows account on the same PC can read them back.
/// Stored form: "dpapi:" + base64. Plain values written by older versions are still read.
/// </summary>
public static class SecretStore
{
    public const string Prefix = "dpapi:";

    // Ties the ciphertext to this purpose, so other DPAPI data of the user cannot be swapped in.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GestureSign.SecretStore.v1");

    public static bool IsProtected(string? value)
        => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Encrypts a secret. Empty stays empty; an already protected value is returned as is.
    /// Outside Windows (tests) the value is returned unchanged.</summary>
    public static string Protect(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || IsProtected(secret))
            return secret ?? "";
        if (!OperatingSystem.IsWindows())
            return secret;
        var data = Transform(Encoding.UTF8.GetBytes(secret), protect: true)
            ?? throw new InvalidOperationException("Windows could not encrypt the secret (CryptProtectData failed).");
        return Prefix + Convert.ToBase64String(data);
    }

    /// <summary>Decrypts a stored secret. Plain (legacy) values are returned unchanged; a value
    /// that cannot be decrypted (another user or PC, damaged) gives an empty string.</summary>
    public static string Reveal(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return "";
        if (!IsProtected(stored))
            return stored;
        if (!OperatingSystem.IsWindows())
            return "";
        try
        {
            var plain = Transform(Convert.FromBase64String(stored.Substring(Prefix.Length)), protect: false);
            return plain is null ? "" : Encoding.UTF8.GetString(plain);
        }
        catch (FormatException)
        {
            return "";
        }
    }

    private static byte[]? Transform(byte[] input, bool protect)
    {
        var inBlob = new DataBlob();
        var entropyBlob = new DataBlob();
        var outBlob = new DataBlob();
        try
        {
            inBlob = DataBlob.From(input);
            entropyBlob = DataBlob.From(Entropy);
            var ok = protect
                ? CryptProtectData(ref inBlob, "GestureSign", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outBlob);
            if (!ok || outBlob.Data == IntPtr.Zero)
                return null;
            var result = new byte[outBlob.Length];
            Marshal.Copy(outBlob.Data, result, 0, outBlob.Length);
            return result;
        }
        finally
        {
            inBlob.Free();
            entropyBlob.Free();
            if (outBlob.Data != IntPtr.Zero)
            {
                // Clear the plain text DPAPI handed back before releasing it.
                if (!protect)
                    Marshal.Copy(new byte[outBlob.Length], 0, outBlob.Data, outBlob.Length);
                LocalFree(outBlob.Data);
            }
        }
    }

    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;

        public static DataBlob From(byte[] bytes)
        {
            var blob = new DataBlob { Length = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
            Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }

        public void Free()
        {
            if (Data == IntPtr.Zero) return;
            Marshal.Copy(new byte[Length], 0, Data, Length);
            Marshal.FreeHGlobal(Data);
            Data = IntPtr.Zero;
        }
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
