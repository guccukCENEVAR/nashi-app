using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed class MetadataEditor:Window
{
    private readonly CheckBox removeAll=new(){Margin=new(4,14)};
    private readonly Dictionary<string,TextBox> fields=[];private readonly Dictionary<string,string> original=[];
    public bool RemoveAll=>removeAll.IsChecked==true;
    public Dictionary<string,string> Updates=>fields.Where(p=>(p.Value.Text??"")!=original.GetValueOrDefault(p.Key,"")).ToDictionary(p=>p.Key,p=>p.Value.Text??"");
    public MetadataEditor(IReadOnlyDictionary<string,string> metadata,bool media=false)
    {
        Width=820;Height=700;MinWidth=640;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;Icon=Ui.Icon();L10n.Title(this,"Metadata · Nashi");L10n.Bind(removeAll,ContentControl.ContentProperty,"Remove all metadata");
        var root=new DockPanel{Margin=new(26)};Content=root;
        var header=new StackPanel();header.Children.Add(Ui.Heading("Metadata"));header.Children.Add(Ui.Text(media?"Edit fields or remove metadata. Encoded audio and video stay untouched.":"Edit fields or remove metadata. Encoded image pixels stay untouched.",13).Also(t=>t.Margin=new(0,8,0,0)));header.Children.Add(removeAll);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=new DockPanel{LastChildFill=false,Margin=new(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var save=Ui.Primary("Save new copy ↗",()=>Close(true));DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);var cancel=Ui.Button("Cancel",()=>Close(false));DockPanel.SetDock(cancel,Dock.Left);footer.Children.Add(cancel);
        var stack=new StackPanel();root.Children.Add(new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var source=metadata.GetValueOrDefault("SourceFile","")??"";var prefix=FeatureCatalog.Extension(source)=="pdf"?"PDF:":FeatureCatalog.Extension(source)=="mp3"?"ID3:":FeatureCatalog.IsVideo(source)||FeatureCatalog.IsAudio(source)?"QuickTime:":"";
        IEnumerable<string> editable=MetadataService.EditableTags.Where(k=>prefix.Length>0?k.StartsWith(prefix,StringComparison.Ordinal):k.StartsWith("EXIF:",StringComparison.Ordinal)||k.StartsWith("XMP:",StringComparison.Ordinal));
        if(media)editable=MediaMetadata.Editable.Concat(metadata.Keys.Where(k=>k.StartsWith("stream:",StringComparison.Ordinal)||k.StartsWith("chapter:",StringComparison.Ordinal)).Where(k=>k.EndsWith(":title",StringComparison.Ordinal)||k.EndsWith(":language",StringComparison.Ordinal)||k.EndsWith(":handler_name",StringComparison.Ordinal)||k.EndsWith(":comment",StringComparison.Ordinal))).Distinct();
        foreach(var key in editable)
        {
            var value=metadata.GetValueOrDefault(key,"");original[key]=value;stack.Children.Add(new TextBlock{Text=media?MediaMetadata.Label(key):key,Margin=new(4,12,4,0)});
            var field=new TextBox{Text=value};fields[key]=field;stack.Children.Add(field);
        }
        var details=new StackPanel();
        foreach(var pair in metadata.Where(p=>!fields.ContainsKey(p.Key)))details.Children.Add(new SelectableTextBlock{Text=(media?MediaMetadata.Label(pair.Key):pair.Key)+"  ·  "+pair.Value,TextWrapping=Avalonia.Media.TextWrapping.Wrap,Margin=new(4,5)});
        stack.Children.Add(Ui.Disclosure("All metadata",details).Also(d=>d.Margin=new(4,18,4,12)));
        removeAll.IsCheckedChanged+=(_,_)=>{foreach(var field in fields.Values)field.IsEnabled=removeAll.IsChecked!=true;};
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Escape){Close(false);e.Handled=true;}},RoutingStrategies.Tunnel);
    }
}

/// The Linux share sheet: e-mail and nearby KDE Connect devices.
internal sealed class ShareWindow:Window
{
    internal ShareWindow(IReadOnlyList<string> files,IReadOnlyList<(string Title,Func<Task> Run)> choices)
    {
        Width=460;SizeToContent=SizeToContent.Height;CanResize=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;Icon=Ui.Icon();L10n.Title(this,"Share · Nashi");
        var stack=new StackPanel{Margin=new(26)};stack.Children.Add(Ui.Heading("Share",24));
        stack.Children.Add(Ui.Text(files.Count==1?Path.GetFileName(files[0]):$"{files.Count} files",13,"Muted",localize:files.Count!=1).Also(t=>t.Margin=new(0,4,0,14)));
        var status=Ui.Text("",12,"Muted",localize:false);
        foreach(var (title,run) in choices)
        {
            var button=new Button{HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left};
            if(title.StartsWith("KDE Connect · ",StringComparison.Ordinal))button.Content="KDE Connect · "+title["KDE Connect · ".Length..];else L10n.Bind(button,ContentControl.ContentProperty,title);
            button.Click+=async(_,_)=>{IsEnabled=false;try{await run();Close();}catch(Exception error)when(FileActions.IsActionError(error)){status.Text=error.Message;IsEnabled=true;}};stack.Children.Add(button);
        }
        stack.Children.Add(status);stack.Children.Add(Ui.Button("Cancel",Close).Also(b=>b.HorizontalAlignment=HorizontalAlignment.Right));
        Content=stack;AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}},RoutingStrategies.Tunnel);
    }
}
