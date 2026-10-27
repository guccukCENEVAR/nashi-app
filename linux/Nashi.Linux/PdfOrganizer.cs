using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
namespace Nashi.Linux;

internal sealed class PdfOrganizer:Window
{
    private sealed class Page(int number,Avalonia.Media.Imaging.Bitmap image){public int Number{get;}=number;public Avalonia.Media.Imaging.Bitmap Image{get;}=image;public int Rotation{get;set;}}
    private readonly CheckBox uniformWidth=new(){Margin=new(8)};public bool UniformWidth=>uniformWidth.IsChecked==true;
    private readonly List<Page> pages=[];private readonly ListBox gallery=new(){SelectionMode=SelectionMode.Multiple};
    public int[] Pages=>pages.Select(p=>p.Number).ToArray();
    public Dictionary<int,int> Rotations=>pages.GroupBy(p=>p.Number).ToDictionary(g=>g.Key,g=>g.First().Rotation);
    public int[] PageRotations=>pages.Select(p=>p.Rotation).ToArray();
    private PdfOrganizer()
    {
        Width=1000;Height=760;MinWidth=700;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;Icon=Ui.Icon();L10n.Title(this,"Organize PDF · Nashi");L10n.Bind(uniformWidth,ContentControl.ContentProperty,"Make all pages the same width");
        var root=new DockPanel{Margin=new(26)};Content=root;var heading=Ui.Heading("Organize PDF");heading.Margin=new(0,0,0,18);DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var footer=new DockPanel{Margin=new(0,16,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var save=Ui.Primary("Save organized copy ↗",async()=>{if(pages.Count==0){await Dialogs.Info(this,"Keep at least one page.","Organize PDF");return;}Close(true);});DockPanel.SetDock(save,Dock.Right);footer.Children.Add(save);
        var controls=new WrapPanel();foreach(var (title,run) in new (string,Action)[]{("Move left",()=>Move(-1)),("Move right",()=>Move(1)),("Rotate",Rotate),("Duplicate",Duplicate),("Remove",Remove),("Cancel",()=>Close(false))})controls.Children.Add(Ui.Button(title,run));controls.Children.Add(uniformWidth);footer.Children.Add(controls);
        gallery.ItemsPanel=new FuncTemplate<Panel?>(()=>new WrapPanel());root.Children.Add(new ScrollViewer{Content=gallery});
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Escape){Close(false);e.Handled=true;}else if(e.Key==Key.Delete){Remove();e.Handled=true;}},RoutingStrategies.Tunnel);
    }
    public static async Task<PdfOrganizer> CreateAsync(string file)
    {
        var result=new PdfOrganizer();var images=await NativePdf.RenderAsync(file,72);
        for(var i=0;i<images.Length;i++){using var frame=ImageFrame.FromPng(images[i]);result.pages.Add(new(i+1,frame.ToAvalonia()));}
        result.Reload();return result;
    }
    private void Reload()
    {
        var selected=Selected();var items=new List<ListBoxItem>();
        foreach(var page in pages)
        {
            var content=new StackPanel();content.Children.Add(new LayoutTransformControl{Child=new Image{Source=page.Image,Width=130,Height=170,Stretch=Stretch.Uniform},LayoutTransform=new RotateTransform(page.Rotation),HorizontalAlignment=HorizontalAlignment.Center});
            content.Children.Add(Ui.Text("Page "+page.Number,13).Also(t=>{t.TextAlignment=TextAlignment.Center;t.Margin=new(0,8,0,0);}));
            items.Add(new ListBoxItem{Content=content,Tag=page,Margin=new(6),Padding=new(10)});
        }
        gallery.ItemsSource=items;foreach(var item in items.Where(i=>selected.Contains((Page)i.Tag!)))gallery.SelectedItems?.Add(item);
    }
    private Page[] Selected()=>gallery.SelectedItems?.OfType<ListBoxItem>().Select(i=>(Page)i.Tag!).ToArray()??[];
    private void Move(int delta){var selected=Selected();foreach(var item in delta<0?selected:selected.Reverse()){var index=pages.IndexOf(item);var next=index+delta;if(next<0||next>=pages.Count)continue;pages.RemoveAt(index);pages.Insert(next,item);}Reload();}
    private void Rotate(){foreach(var page in Selected())page.Rotation=(page.Rotation+90)%360;Reload();}
    private void Duplicate(){foreach(var page in Selected().Reverse()){var index=pages.IndexOf(page);pages.Insert(index+1,new(page.Number,page.Image){Rotation=page.Rotation});}Reload();}
    private void Remove(){if(pages.Count-Selected().Length<1)return;foreach(var page in Selected())pages.Remove(page);Reload();}
}
