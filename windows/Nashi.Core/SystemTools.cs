namespace Nashi.Core;

/// Linux and other Unix systems install the same upstream tools through their
/// package manager instead of Nashi's pinned Windows downloads.
public static class SystemTools
{
    public static bool UsesPackageManager=>!OperatingSystem.IsWindows();

    /// Resolves a command from PATH to an absolute, executable regular file.
    public static string? Find(string name,string? searchPath=null)
    {
        if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(['/','\\','\0'])>=0)return null;
        foreach(var directory in (searchPath??Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {
            if(!Path.IsPathFullyQualified(directory))continue;
            var candidate=Path.Combine(directory,name);
            if(IsExecutable(candidate))return candidate;
        }
        return null;
    }

    public static bool IsExecutable(string path)
    {
        if(!ToolInstallation.UsableFile(path))return false;
        if(OperatingSystem.IsWindows())return true;
        return (File.GetUnixFileMode(path)&(UnixFileMode.UserExecute|UnixFileMode.GroupExecute|UnixFileMode.OtherExecute))!=0;
    }
}
