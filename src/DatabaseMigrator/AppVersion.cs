using System.Reflection;

namespace DatabaseMigrator;

/// <summary>The version of this build, without any commit id: the release workflow stamps -p:Version=X.Y.Z
/// (local builds fall back to the latest git tag, see DatabaseMigrator.csproj); the SDK may append +sha, which is dropped.</summary>
public static class AppVersion
{
    /// <summary>"1.0.77": the version part of the informational version, commit included or not.</summary>
    public static string Format(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return "unknown";
        int plus = informationalVersion.IndexOf('+');
        return plus < 0 ? informationalVersion : informationalVersion[..plus];
    }

    public static string Current { get; } =
        Format(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
