namespace Nashi.Linux;

internal sealed partial class MainWindow
{
    internal static readonly ToolChoice[] FileActionChoices=[new("share","Share","◉"),new("float","Float","▣"),new("copy","Copy","▱"),new("move","Move","↗"),new("copyPath","Copy Path","↪"),new("terminal","Terminal",">_")];
    private readonly List<FloatShelf> shelves=[];
    private void ShowFileActions()
    {
        if(busy||files.Count==0)return;WheelWindow.Present(this,FileActionChoices,async c=>await PerformFileAction(c.Id,files.ToArray()));
    }
    internal const string WheelActionPrefix="wheel:";
    internal async Task PerformFileAction(string action,IReadOnlyList<string> paths)
    {
        if(busy||paths.Count==0)return;
        // A wheel chosen on a Float drop: tools and conversions continue in the main window.
        if(action.StartsWith(WheelActionPrefix,StringComparison.Ordinal))
        {
            var choice=action[WheelActionPrefix.Length..];
            if(!FileActionChoices.Any(c=>c.Id==choice)){files.Clear();Add(paths);ShowLibrary(false);ShowMain();}
            await RunDropChoice(choice,paths);return;
        }
        try
        {
            switch(action)
            {
                case "copy":await FileActions.Copy(this,paths);L10n.SetText(status,"Files copied. Paste them wherever you need.");break;
                case "copyPath":await FileActions.CopyPaths(this,paths);L10n.SetText(status,"File paths copied.");break;
                case "terminal":FileActions.Terminal(paths[0]);break;
                case "share":ShowMain();await FileActions.Share(this,paths);break;
                case "float":
                    if(preferences.Value.FloatBehavior=="addToExisting"&&shelves.LastOrDefault(s=>s.IsVisible) is {} existing){existing.Add(paths);existing.Activate();break;}
                    var shelf=new FloatShelf(paths,async(p,a)=>await PerformFileAction(a,p),p=>{files.Clear();Add(p);ShowLibrary(false);ShowMain();},tools,officeRuntime);shelves.Add(shelf);shelf.Closed+=(_,_)=>shelves.Remove(shelf);shelf.Show();break;
                case "move":await RunAsync((destination,p,t)=>FileActions.MoveAsync(paths,destination,p,t),"Move");files.Clear();Refresh();break;
            }
        }catch(Exception error)when(FileActions.IsActionError(error)){await Dialogs.Info(this,error.Message,"File action could not finish");}
    }
}
