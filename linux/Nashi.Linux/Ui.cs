using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
namespace Nashi.Linux;

/// Small building blocks shared by every window, mirroring the Windows helpers.
internal static class Ui
{
    internal static TextBlock Text(string value,double size,string? color=null,bool localize=true)
    {
        var text=new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,FontWeight=size>=17?FontWeight.SemiBold:FontWeight.Normal};
        if(color is not null)text.Res(TextBlock.ForegroundProperty,color);
        return localize?L10n.Label(text):text;
    }
    internal static TextBlock Heading(string value,double size=28)
    {
        var text=Text(value,size);text.FontWeight=FontWeight.Bold;text.LetterSpacing=-0.5;return text;
    }
    internal static TextBlock Eyebrow(string value)
    {
        var text=new TextBlock{FontSize=10,FontWeight=FontWeight.Bold,LetterSpacing=1.4,Margin=new(0,0,0,6)};
        text.Bind(TextBlock.TextProperty,LanguageState.Shared.Translate(value,uppercase:true));
        return text.Res(TextBlock.ForegroundProperty,"GoldInk");
    }
    internal static Border Card(Control content)
    {
        var card=new Border{Child=content};card.Classes.Add("card");return card;
    }
    internal static Button Button(string title,Action action)
    {
        var button=L10n.Content(new Button{Content=title});button.Click+=(_,_)=>action();return button;
    }
    internal static Button Primary(string title,Action action)
    {
        var button=Button(title,action);button.Classes.Add("primary");return button;
    }
    internal static Button Quiet(string title,Action action)
    {
        var button=Button(title,action);button.Classes.Add("quiet");return button;
    }
    internal static Bitmap Asset(string name)=>new(AssetLoader.Open(new Uri("avares://Nashi/Assets/"+name+".png")));
    internal static WindowIcon Icon()=>new(AssetLoader.Open(new Uri("avares://Nashi/Assets/NashiIcon.png")));
    internal static Border Pill(string title,string color="Forest")
    {
        var text=Text(title,10,color);text.FontWeight=FontWeight.SemiBold;
        var pill=new Border{Child=text,CornerRadius=new(10),Padding=new(9,5),HorizontalAlignment=HorizontalAlignment.Left};
        return pill.Res(Border.BackgroundProperty,"Sage");
    }
    /// A titled section that opens on click, drawn with Nashi's own card instead of the platform expander.
    internal static Border Disclosure(string title,Control content)
    {
        var chevron=new TextBlock{Text="⌄",FontSize=16,VerticalAlignment=VerticalAlignment.Center};
        var header=new DockPanel();DockPanel.SetDock(chevron,Avalonia.Controls.Dock.Right);header.Children.Add(chevron);header.Children.Add(Text(title,14));
        var toggle=new Button{Content=header,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=Brushes.Transparent,BorderThickness=new(0),Margin=new(0),Padding=new(16,12)};
        content.IsVisible=false;content.Margin=new(12,0,12,12);
        toggle.Click+=(_,_)=>{content.IsVisible=!content.IsVisible;chevron.Text=content.IsVisible?"⌃":"⌄";};
        var stack=new StackPanel();stack.Children.Add(toggle);stack.Children.Add(content);
        var card=Card(stack);card.Padding=new(0);card.CornerRadius=new(16);return card;
    }
    internal static Control Dock(Control control,Avalonia.Controls.Dock dock){DockPanel.SetDock(control,dock);return control;}
    internal static void Open(string target)
    {
        var info=new ProcessStartInfo("xdg-open"){UseShellExecute=false};info.ArgumentList.Add(target);Process.Start(info)?.Dispose();
    }
    internal static void OpenUrl(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))throw new ArgumentException("Only web links can be opened.");
        Open(uri.AbsoluteUri);
    }
}
