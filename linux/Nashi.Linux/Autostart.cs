namespace Nashi.Linux;

/// XDG autostart entry (~/.config/autostart/nashi.desktop); Nashi starts in the tray.
internal static class Autostart
{
    private static string Directory=>Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is {Length:>0} config&&Path.IsPathFullyQualified(config)?config:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".config"),"autostart");
    private static string File=>Path.Combine(Directory,"nashi.desktop");
    internal static bool Enabled=>System.IO.File.Exists(File);
    internal static void Set(bool enabled)
    {
        if(!enabled){if(System.IO.File.Exists(File))System.IO.File.Delete(File);return;}
        System.IO.Directory.CreateDirectory(Directory);
        // A packaged install starts through its launcher on PATH; a self-built copy starts in place.
        var executable=Nashi.Core.SystemTools.Find("nashi")??Environment.ProcessPath??throw new IOException("Nashi's location is unknown.");
        // Desktop Entry Exec quoting: wrap in double quotes and escape the reserved characters.
        var quoted="\""+executable.Replace("\\","\\\\").Replace("\"","\\\"").Replace("`","\\`").Replace("$","\\$")+"\"";
        System.IO.File.WriteAllText(File,$"[Desktop Entry]\nType=Application\nName=Nashi\nComment=Convert, edit and share files\nExec={quoted} --background\nIcon=nashi\nTerminal=false\nX-GNOME-Autostart-enabled=true\n");
    }
}
