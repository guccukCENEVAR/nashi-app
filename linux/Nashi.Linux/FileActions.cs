using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Nashi.Core;
namespace Nashi.Linux;

internal static class FileActions
{
    private static readonly DataFormat<string> GnomeCopiedFiles=DataFormat.CreateStringPlatformFormat("x-special/gnome-copied-files");
    internal static bool IsActionError(Exception error)=>error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or ArgumentException;
    private static string FileUri(string path)=>new Uri(Path.GetFullPath(path)).AbsoluteUri;

    /// Files as the desktop expects them: storage items (text/uri-list) plus GNOME's copied-files list.
    internal static async Task<DataTransfer?> Transfer(TopLevel owner,IReadOnlyList<string> paths,bool clipboard=false)
    {
        var transfer=new DataTransfer();
        foreach(var path in paths)
        {
            IStorageItem? item=Directory.Exists(path)?await owner.StorageProvider.TryGetFolderFromPathAsync(path):await owner.StorageProvider.TryGetFileFromPathAsync(path);
            if(item is not null)transfer.Add(DataTransferItem.CreateFile(item));
        }
        if(transfer.Items.Count==0)return null;
        if(clipboard)transfer.Add(DataTransferItem.Create(GnomeCopiedFiles,"copy\n"+string.Join('\n',paths.Select(FileUri))));
        return transfer;
    }
    public static async Task Copy(TopLevel owner,IReadOnlyList<string> paths)
    {
        var transfer=await Transfer(owner,paths,clipboard:true)??throw new IOException("These files are no longer available.");
        await (owner.Clipboard??throw new InvalidOperationException("The clipboard is unavailable.")).SetDataAsync(transfer);
    }
    public static Task CopyPaths(TopLevel owner,IReadOnlyList<string> paths)=>(owner.Clipboard??throw new InvalidOperationException("The clipboard is unavailable.")).SetTextAsync(string.Join(Environment.NewLine,paths));
    /// Opens the user's terminal in the file's folder: $TERMINAL first, then common desktop terminals.
    public static void Terminal(string path)
    {
        var folder=Directory.Exists(path)?path:Path.GetDirectoryName(path)!;
        var candidates=new List<(string Name,string[] Arguments)>();
        if(Environment.GetEnvironmentVariable("TERMINAL") is {Length:>0} preferred&&!preferred.Contains(' '))candidates.Add((preferred,[]));
        candidates.AddRange([("xdg-terminal-exec",[]),("konsole",["--workdir",folder]),("gnome-terminal",["--working-directory="+folder]),("kgx",["--working-directory="+folder]),("ptyxis",["--working-directory="+folder]),("xfce4-terminal",["--working-directory="+folder]),("kitty",["--directory",folder]),("alacritty",["--working-directory",folder]),("wezterm",["start","--cwd",folder]),("foot",["--working-directory="+folder]),("tilix",["--working-directory="+folder]),("terminator",["--working-directory="+folder]),("xterm",[])]);
        foreach(var (name,arguments) in candidates)
        {
            var executable=Path.IsPathFullyQualified(name)&&SystemTools.IsExecutable(name)?name:SystemTools.Find(name);if(executable is null)continue;
            var info=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=folder};foreach(var argument in arguments)info.ArgumentList.Add(argument);Process.Start(info)?.Dispose();return;
        }
        throw new InvalidOperationException("No terminal app was found. Set the TERMINAL environment variable to your terminal.");
    }
    /// Highlights the item in the file manager (Dolphin, Files, Nemo, Thunar…), or opens its folder.
    public static void Reveal(string path)
    {
        var full=Path.GetFullPath(path);var dbus=SystemTools.Find("dbus-send");
        if(dbus is not null)
        {
            var info=new ProcessStartInfo(dbus){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true};
            foreach(var argument in new[]{"--session","--print-reply","--dest=org.freedesktop.FileManager1","--type=method_call","/org/freedesktop/FileManager1","org.freedesktop.FileManager1.ShowItems","array:string:"+FileUri(full),"string:"})info.ArgumentList.Add(argument);
            using var process=Process.Start(info);if(process is not null&&process.WaitForExit(3000)&&process.ExitCode==0)return;
        }
        Ui.Open(Directory.Exists(full)?full:Path.GetDirectoryName(full)!);
    }
    public static void Open(string path){if(File.Exists(path)||Directory.Exists(path))Ui.Open(Path.GetFullPath(path));}
    /// Linux has no single share sheet: offer e-mail (xdg-email) and nearby KDE Connect devices.
    public static async Task Share(Window owner,IReadOnlyList<string> paths)
    {
        var files=paths.Where(File.Exists).Select(Path.GetFullPath).ToArray();if(files.Length==0)throw new InvalidOperationException("Share files rather than folders. Create a ZIP first to share a folder.");
        var choices=new List<(string Title,Func<Task> Run)>();
        if(SystemTools.Find("xdg-email") is {} email)choices.Add(("Send by email",()=>{var info=new ProcessStartInfo(email){UseShellExecute=false};foreach(var file in files){info.ArgumentList.Add("--attach");info.ArgumentList.Add(file);}Process.Start(info)?.Dispose();return Task.CompletedTask;}));
        if(SystemTools.Find("kdeconnect-cli") is {} connect)
        {
            try
            {
                var listed=await ProcessRunner.RunAsync(connect,["--list-available","--id-name-only"]);
                foreach(var line in listed.Output.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
                {
                    var parts=line.Split(' ',2);if(parts.Length!=2||parts[0].Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('_' or '-')))continue;var id=parts[0];
                    choices.Add(("KDE Connect · "+parts[1],async()=>{foreach(var file in files)await ProcessRunner.RunAsync(connect,["--device",id,"--share",file]);}));
                }
            }
            catch(Exception error)when(error is IOException or Win32Exception){}
        }
        if(choices.Count==0)throw new InvalidOperationException("Install xdg-utils or KDE Connect to share files from Nashi.");
        var picker=new ShareWindow(files,choices);await picker.ShowDialog(owner);
    }
    public static async Task<IReadOnlyList<string>> MoveAsync(IReadOnlyList<string> inputs,string destination,IProgress<OperationProgress>? progress,CancellationToken token)
    {
        using var tx=new ExportTransaction(destination);var staged=new List<string>();
        foreach(var path in inputs)
        {
            var full=Path.GetFullPath(path);var dest=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
            if(Directory.Exists(full)&&(dest.Equals(full,StringComparison.Ordinal)||dest.StartsWith(full.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.Ordinal)))throw new ArgumentException("Choose a destination outside the folder being moved.");
            var target=SafePaths.Unique(tx.StagingDirectory,Path.GetFileName(full));await CopyTree(full,target,token);staged.Add(target);progress?.Report(new(staged.Count/(double)inputs.Count*.95,"Preparing move…"));
        }
        token.ThrowIfCancellationRequested();var published=tx.Publish(staged,token);
        // Only after every copy was published may originals be removed. If one remains, the copy is kept and reported.
        var retained=new List<string>();foreach(var input in inputs){try{if(Directory.Exists(input))Directory.Delete(input,true);else File.Delete(input);}catch(IOException){retained.Add(input);}catch(UnauthorizedAccessException){retained.Add(input);}}
        if(retained.Count>0)throw new IOException("Files were copied safely, but some originals could not be removed. New copies are in "+destination+". Check the originals before retrying.");
        return published;
    }
    private static async Task CopyTree(string source,string target,CancellationToken token,int depth=0)
    {
        token.ThrowIfCancellationRequested();if(depth>64||(File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Move regular files and folders without symbolic links.");
        if(Directory.Exists(source)){Directory.CreateDirectory(target);foreach(var item in Directory.EnumerateFileSystemEntries(source))await CopyTree(item,Path.Combine(target,Path.GetFileName(item)),token,depth+1);}
        else{await using var input=File.OpenRead(source);await using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);await input.CopyToAsync(output,token);File.SetUnixFileMode(target,File.GetUnixFileMode(source));}
    }
}
