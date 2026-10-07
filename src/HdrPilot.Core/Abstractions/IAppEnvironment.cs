namespace HdrPilot.Core.Abstractions;

/// <summary>Umgebungswerte (Systemordner, Windows-Build), damit Pfade und die Build-Weiche testbar sind.</summary>
public interface IAppEnvironment
{
    /// <summary>Windows-Buildnummer, etwa 26200.</summary>
    int WindowsBuild { get; }

    string GetFolderPath(Environment.SpecialFolder folder);

    string? GetEnvironmentVariable(string name);

    string ExpandEnvironmentVariables(string value);
}

public sealed class SystemEnvironment : IAppEnvironment
{
    public static SystemEnvironment Instance { get; } = new();

    public int WindowsBuild => Environment.OSVersion.Version.Build;

    public string GetFolderPath(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string ExpandEnvironmentVariables(string value) => Environment.ExpandEnvironmentVariables(value);
}
