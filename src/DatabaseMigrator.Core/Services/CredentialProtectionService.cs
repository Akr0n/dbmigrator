using System;
using System.Security.Cryptography;
using System.Text;

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
            return false;
        }

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            protectedBase64 = Convert.ToBase64String(protectedBytes);
            return true;
        }
        catch
        {
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
            return false;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
