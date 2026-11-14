using System;
using System.Security.Cryptography;
using System.Text;

#if DEBUG
using System.Diagnostics;
#endif

namespace DatabaseMigrator.Core.Services;

public static class CredentialProtectionService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DatabaseMigrator::CredentialEntropy::v1");

    public static bool TryProtect(string plainText, out string protectedBase64)
    {
        protectedBase64 = string.Empty;

        if (string.IsNullOrEmpty(plainText))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
#if DEBUG
            Debug.WriteLine("[CredentialProtectionService] DPAPI unavailable on non-Windows; credentials stored unencrypted.");
#endif
            return false;
        }

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            protectedBase64 = Convert.ToBase64String(protectedBytes);
            return true;
        }
#pragma warning disable CS0168 // variabile 'ex' dichiarata ma non usata in Release (blocco DEBUG rimosso)
        catch (Exception ex)
#pragma warning restore CS0168
        {
#if DEBUG
            Debug.WriteLine($"[CredentialProtectionService] DPAPI protect failed: {ex.Message}");
#endif
            return false;
        }
    }

    public static bool TryUnprotect(string protectedBase64, out string plainText)
    {
        plainText = string.Empty;

        if (string.IsNullOrEmpty(protectedBase64))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
#if DEBUG
            Debug.WriteLine("[CredentialProtectionService] DPAPI unavailable on non-Windows; cannot decrypt credentials.");
#endif
            return false;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
#pragma warning disable CS0168 // variabile 'ex' dichiarata ma non usata in Release (blocco DEBUG rimosso)
        catch (Exception ex)
#pragma warning restore CS0168
        {
#if DEBUG
            Debug.WriteLine($"[CredentialProtectionService] DPAPI unprotect failed: {ex.Message}");
#endif
            return false;
        }
    }
}
