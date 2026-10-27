using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed partial class MainWindow
{
    private readonly TextBlock selectionPill=Ui.Text("✦  START HERE",11,"GoldInk");
    private Guid? selectedLibraryId;private Point libraryDragStart;private bool libraryPressed;
    private readonly TextBlock homeTitle=Ui.Heading("Your files. Their next form.",30);
    private readonly StackPanel shortcutHints=new();
    private readonly Dictionary<Guid,Border> libraryCards=[];
    private void ShowToolSetupMenu()
    {
        if(busy)return;
        toolSetupOffered=true;
        if(toolSetupWindow is not null){toolSetupWindow.Activate();return;}
        toolSetupWindow=new ToolSetupWindow(tools,officeRuntime,metadataService,SetBusy);toolSetupWindow.Closed+=(_,_)=>{toolSetupWindow=null;SetBusy(busy);};
        if(IsVisible)toolSetupWindow.Show(this);else toolSetupWindow.Show();
    }
    private void OfferFirstRun()
    {
        if(busy)return;
        if(!WelcomeState.Seen){ShowWelcome();return;}
        if(toolSetupOffered||ToolsReady)return;
        toolSetupOffered=true;ShowToolSetupMenu();
    }
    private void ShowWelcome()
    {
        var welcome=new WelcomeWindow(tools.Ready&&officeRuntime.Executable is not null,()=>
        {
            WelcomeState.MarkSeen();
            if(!ToolsReady&&!toolSetupOffered){toolSetupOffered=true;ShowToolSetupMenu();}
        });
        if(IsVisible)welcome.Show(this);else welcome.Show();
    }
    private void BuildFileChips()
    {
        chips.Children.Clear();
        foreach(var path in files)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal};var icon=FileArtwork.Create(path,17);icon.Margin=new(0,0,6,0);row.Children.Add(icon);
            row.Children.Add(new TextBlock{Text=Path.GetFileName(path),FontSize=10,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=160,VerticalAlignment=VerticalAlignment.Center});
            var remove=new Button{Content="×",Padding=new(4,0),Margin=new(2,0,0,0),BorderThickness=new(0),Background=Brushes.Transparent};L10n.Tip(remove,"Remove from selection");var target=path;remove.Click+=(_,e)=>{files.Remove(target);Refresh();e.Handled=true;};row.Children.Add(remove);
            var chip=new Border{CornerRadius=new(9),Padding=new(7,3,3,3),Margin=new(0,3,6,0),Child=row}.Res(Border.BackgroundProperty,"Inset");ToolTip.SetTip(chip,path);chips.Children.Add(chip);
        }
    }
    private void ConfigureLibraryRow(Border card,LibraryEntry entry)
    {
        libraryCards[entry.Id]=card;card.Tag=entry;card.Focusable=true;L10n.Tip(card,"Space: preview · Enter: use in Nashi");
        card.Res(Border.BorderBrushProperty,selectedLibraryId==entry.Id?"Gold":"Line");
        card.GotFocus+=(_,_)=>SelectCard(entry.Id);
        card.PointerPressed+=(_,e)=>
        {
            if(IsButton(e.Source as Visual))return;var point=e.GetCurrentPoint(card);if(!point.Properties.IsLeftButtonPressed)return;
            libraryDragStart=point.Position;libraryPressed=true;SelectCard(entry.Id);card.Focus();if(e.ClickCount==2){libraryPressed=false;PreviewEntry(entry);e.Handled=true;}
        };
        card.PointerReleased+=(_,_)=>libraryPressed=false;
        card.PointerMoved+=async(_,e)=>
        {
            if(!libraryPressed||!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)return;var delta=e.GetPosition(card)-libraryDragStart;
            if(Math.Abs(delta.X)+Math.Abs(delta.Y)<8||entry.AvailablePaths.Length==0)return;libraryPressed=false;
            var transfer=await FileActions.Transfer(this,entry.AvailablePaths);if(transfer is not null)await DragDrop.DoDragDropAsync(e,transfer,DragDropEffects.Copy);
        };
    }
    private static bool IsButton(Visual? value){while(value is not null){if(value is Button)return true;value=value.GetVisualParent();}return false;}
    private void SelectCard(Guid id){selectedLibraryId=id;foreach(var pair in libraryCards)pair.Value.Res(Border.BorderBrushProperty,pair.Key==id?"Gold":"Line");}
    private void SelectLibrary(int direction)
    {
        var entries=SearchLibrary(search.Text).ToArray();if(entries.Length==0)return;var old=Array.FindIndex(entries,e=>e.Id==selectedLibraryId);var next=old<0?0:Math.Clamp(old+direction,0,entries.Length-1);SelectCard(entries[next].Id);
        if(libraryCards.TryGetValue(entries[next].Id,out var card)){card.Focus();card.BringIntoView();}
    }
    private LibraryEntry? SelectedLibrary()=>SearchLibrary(search.Text).FirstOrDefault(e=>e.Id==selectedLibraryId)??SearchLibrary(search.Text).FirstOrDefault();
    private void PreviewSelectedLibrary(){if(SelectedLibrary() is {} entry)PreviewEntry(entry);}
    private void PreviewEntry(LibraryEntry entry){if(entry.AvailablePaths.Length>0)new FilePreviewWindow(entry.AvailablePaths,tools,officeRuntime).Show(this);else L10n.SetText(status,"These files have moved or were deleted.");}
    private void ReuseSelectedLibrary(){if(busy||SelectedLibrary() is not {} entry)return;files.Clear();Add(entry.AvailablePaths);ShowLibrary(false);}
    private void BuildSelectionActions()
    {
        L10n.SetText(homeTitle,files.Count==0?"Your files. Their next form.":"Let’s make something of it.");
        shortcutHints.Children.Clear();
        foreach(var (modifier,hint) in new[]{(preferences.Value.ConvertModifier,"Convert"),(preferences.Value.ToolsModifier,"Tools"),(preferences.Value.ActionsModifier,"File actions")})
        {var text=new TextBlock{FontSize=11};text.Bind(TextBlock.TextProperty,LanguageState.Shared.Translate(hint,format:t=>modifier+"  "+t));text.Res(TextBlock.ForegroundProperty,"Muted");var pill=Ui.Card(text);pill.Padding=new(9,5);pill.Margin=new(0,0,7,0);pill.CornerRadius=new(9);shortcutHints.Children.Add(pill);}
        if(files.Count==0)return;
        var stack=new StackPanel();actions.Children.Add(stack);var tiles=new Grid{ColumnDefinitions=new ColumnDefinitions("*,*,*")};stack.Children.Add(tiles);
        var formats=FeatureCatalog.Formats(files);var choices=Choices();
        void Tile(int column,string title,string subtitle,string symbol,IEnumerable<(string Name,bool Localize,Action Run)> commands)
        {
            var label=new DockPanel();var icon=Ui.Text(symbol,22,"GoldInk",localize:false);icon.Margin=new(0,0,12,0);DockPanel.SetDock(icon,Dock.Left);label.Children.Add(icon);
            var caption=new StackPanel();var heading=Ui.Text(title,14);heading.FontWeight=FontWeight.SemiBold;caption.Children.Add(heading);caption.Children.Add(Ui.Text(subtitle,11,"Muted"));label.Children.Add(caption);
            var button=new Button{Content=label,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new(15),Margin=new(column==0?0:6,0,column==2?0:6,0)};
            var menu=new ContextMenu();foreach(var (name,localize,run) in commands){var item=new MenuItem{Header=name};if(localize)L10n.Bind(item,MenuItem.HeaderProperty,name);item.Click+=(_,_)=>run();menu.Items.Add(item);}
            button.IsEnabled=menu.Items.Count>0;button.Click+=(_,_)=>menu.Open(button);Grid.SetColumn(button,column);tiles.Children.Add(button);
        }
        Tile(0,"Convert",$"{formats.Length} ways to change it","⇄",formats.Select(f=>(f.ToUpperInvariant(),false,(Action)(async()=>await EditAsync(new("convert:"+f,"Convert","↗"))))));
        Tile(1,"Fine-tune","Edit, compress, and more","☷",choices.Select(t=>(t.Title,true,(Action)(async()=>await EditAsync(t)))));
        Tile(2,"Move & share","Send it on its way","↗",FileActionChoices.Select(t=>(t.Title,true,(Action)(async()=>await PerformFileAction(t.Id,files.ToArray())))));
        var quick=new WrapPanel{Margin=new(0,8,0,0)};var label=Ui.Text("QUICK PICKS",10,"Muted");label.Margin=new(4,0,8,0);quick.Children.Add(label);
        foreach(var format in formats.Take(4)){var button=new Button{Content=format.ToUpperInvariant()+" ↗"};button.Click+=async(_,_)=>await EditAsync(new("convert:"+format,"Convert","↗"));quick.Children.Add(button);}
        stack.Children.Add(quick);
    }
}
