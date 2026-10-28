using System.Diagnostics;
using Nashi.Core;
namespace Nashi.Linux;

/// KDE Plasma on Wayland does not tell apps where the pointer is, so Nashi cannot open its drop
/// wheel under it. KWin knows: a small KWin script, loaded for the session, centres each new
/// Nashi drop wheel on the pointer. It touches no other window.
internal static class KWinPlacement
{
    internal const string WheelTitle="Nashi drop wheel";
    private const string PluginName="nashi-drop-wheel";
    internal static bool Supported=>!X11.IsX11Session&&
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")??"").Split(':').Any(d=>d.Equals("KDE",StringComparison.OrdinalIgnoreCase));
    // KWin may suffix a duplicate caption (" <2>"), so match the start.
    internal static string Script=>$$"""
        // Nashi: centre Nashi's drop wheel on the pointer; Wayland keeps the pointer position from apps.
        workspace.windowAdded.connect(function (window) {
            if (String(window.caption).indexOf("{{WheelTitle}}") !== 0) return;
            const pointer = workspace.cursorPos, frame = window.frameGeometry;
            window.frameGeometry = { x: pointer.x - frame.width / 2, y: pointer.y - frame.height / 2, width: frame.width, height: frame.height };
        });
        """;
    /// Loads (or reloads) the script; false when KWin is unavailable, so wheels open at screen centre instead.
    internal static async Task<bool> LoadAsync()
    {
        if(!Supported||SystemTools.Find("dbus-send") is not {} dbus)return false;
        try
        {
            var file=Path.Combine(SingleInstance.RuntimeDirectory(),PluginName+".js");await File.WriteAllTextAsync(file,Script);
            // A previous Nashi may have left its copy loaded; replace it.
            try{await Send(dbus,"/Scripting","org.kde.kwin.Scripting.unloadScript","string:"+PluginName);}catch(IOException){}
            var reply=await Send(dbus,"/Scripting","org.kde.kwin.Scripting.loadScript","string:"+file,"string:"+PluginName);
            if(!int.TryParse(reply.Output.Split((char[])[' ','\n'],StringSplitOptions.RemoveEmptyEntries).LastOrDefault(),out var id)||id<0)return false;
            try{await Send(dbus,"/Scripting/Script"+id,"org.kde.kwin.Script.run");}
            catch(IOException){await Send(dbus,"/Scripting","org.kde.kwin.Scripting.start");}
            return true;
        }
        catch(Exception error)when(error is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){return false;}
    }
    /// Removes the script when Nashi quits; KWin also drops it when the session ends.
    internal static void Unload()
    {
        if(!Supported||SystemTools.Find("dbus-send") is not {} dbus)return;
        try
        {
            using var process=Process.Start(new ProcessStartInfo(dbus){ArgumentList={"--session","--dest=org.kde.KWin","/Scripting","org.kde.kwin.Scripting.unloadScript","string:"+PluginName},UseShellExecute=false});
            process?.WaitForExit(1000);
        }
        catch(Exception error)when(error is InvalidOperationException or System.ComponentModel.Win32Exception){}
    }
    private static Task<ProcessResult> Send(string dbus,string path,string method,params string[] arguments)=>
        ProcessRunner.RunAsync(dbus,["--session","--print-reply","--dest=org.kde.KWin",path,method,..arguments]);
}
