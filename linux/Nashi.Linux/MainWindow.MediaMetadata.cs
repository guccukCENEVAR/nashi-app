using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class MainWindow
{
    private async Task EditMediaMetadataAsync(string[] selected)
    {
        if(!tools.Ready){await MissingMediaTools();return;}var removeAll=selected.Length>1;var updates=new Dictionary<string,string>();
        if(selected.Length==1)
        {
            var values=await MediaMetadata.Read(tools,selected[0]);var editor=new MetadataEditor(values,media:true);if(!await editor.ShowDialog<bool>(this))return;
            removeAll=editor.RemoveAll;updates=removeAll?[]:editor.Updates;
        }
        await RunAsync((destination,p,t)=>new MediaEngine(tools.Executable("ffmpeg"),tools.Executable("ffprobe")).ToolAsync("removeMetadata",selected,destination,new(){RemoveAllMetadata=removeAll,Metadata=updates},p,t),"Metadata");
    }
}
