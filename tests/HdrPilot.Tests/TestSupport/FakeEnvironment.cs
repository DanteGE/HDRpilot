using HdrPilot.Core.Abstractions;

namespace HdrPilot.Tests.TestSupport;

public sealed class FakeEnvironment : IAppEnvironment
{
    public int WindowsBuild { get; set; } = 26200;

    public Dictionary<Environment.SpecialFolder, string> Folders { get; } = new()
    {
        [Environment.SpecialFolder.CommonApplicationData] = @"C:\ProgramData",
        [Environment.SpecialFolder.ApplicationData] = @"C:\Users\Test\AppData\Roaming",
        [Environment.SpecialFolder.LocalApplicationData] = @"C:\Users\Test\AppData\Local",
        [Environment.SpecialFolder.ProgramFiles] = @"C:\Program Files",
        [Environment.SpecialFolder.ProgramFilesX86] = @"C:\Program Files (x86)",
        [Environment.SpecialFolder.UserProfile] = @"C:\Users\Test",
        [Environment.SpecialFolder.Desktop] = @"C:\Users\Test\Desktop",
        [Environment.SpecialFolder.StartMenu] = @"C:\Users\Test\AppData\Roaming\Microsoft\Windows\Start Menu",
        [Environment.SpecialFolder.Programs] = @"C:\Users\Test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
    };

    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProgramData"] = @"C:\ProgramData",
        ["APPDATA"] = @"C:\Users\Test\AppData\Roaming",
        ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local",
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
        ["USERPROFILE"] = @"C:\Users\Test",
    };

    public string GetFolderPath(Environment.SpecialFolder folder) => Folders.TryGetValue(folder, out var path) ? path : string.Empty;

    public string? GetEnvironmentVariable(string name) => Variables.TryGetValue(name, out var value) ? value : null;

    public string ExpandEnvironmentVariables(string value)
    {
        var result = value;
        foreach (var (name, replacement) in Variables)
        {
            result = result.Replace("%" + name + "%", replacement, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
