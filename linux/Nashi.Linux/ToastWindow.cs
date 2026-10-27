using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
namespace Nashi.Linux;

/// Borderless progress and result notice; success fades after three seconds.
internal sealed class ToastWindow:Window
{
    private readonly TextBlock label=new(){FontSize=16,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock detail=new(){Margin=new(0,4,0,8),TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar bar=new(){Height=5,Minimum=0,Maximum=1,IsIndeterminate=true};
    private readonly Button action=new(){MinWidth=85};private bool complete;private IReadOnlyList<string> outputs=[];
    public ToastWindow(Window owner,Action? cancel)
    {
        SystemDecorations=SystemDecorations.None;CanResize=false;ShowInTaskbar=false;ShowActivated=false;Topmost=true;SizeToContent=SizeToContent.Height;Width=510;
        Background=Brushes.Transparent;TransparencyLevelHint=[WindowTransparencyLevel.Transparent];Icon=Ui.Icon();
        L10n.Bind(action,ContentControl.ContentProperty,"Cancel");detail.Res(TextBlock.ForegroundProperty,"Muted");
        var content=new StackPanel();content.Children.Add(label);content.Children.Add(detail);content.Children.Add(bar);
        var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};grid.Children.Add(content);Grid.SetColumn(action,1);grid.Children.Add(action);action.Margin=new(18,0,0,0);action.VerticalAlignment=VerticalAlignment.Center;
        Content=new Border{CornerRadius=new(22),BorderThickness=new(1),Padding=new(24),Child=grid}.Res(Border.BackgroundProperty,"Surface").Res(Border.BorderBrushProperty,"Line");
        Opened+=(_,_)=>Place(owner);
        action.Click+=(_,_)=>
        {
            if(!complete){cancel?.Invoke();action.IsEnabled=false;return;}
            if(outputs.Count>0&&(File.Exists(outputs[0])||Directory.Exists(outputs[0])))FileActions.Reveal(outputs[0]);
        };
        Update(new(null,"A fresh file is on its way…"));
    }
    private void Place(Window owner)
    {
        if(owner.IsVisible)
        {
            var scale=owner.RenderScaling;var x=owner.Position.X+(int)((owner.Bounds.Width-Bounds.Width)/2*scale);var y=owner.Position.Y+(int)((owner.Bounds.Height-Bounds.Height-30)*scale);
            Position=new PixelPoint(Math.Max(8,x),Math.Max(8,y));
        }
        else if(Screens.Primary is {} screen)
        {
            var area=screen.WorkingArea;Position=new PixelPoint(area.X+(area.Width-(int)(Bounds.Width*screen.Scaling))/2,area.Bottom-(int)((Bounds.Height+40)*screen.Scaling));
        }
    }
    public void Update(Nashi.Core.OperationProgress progress)
    {
        if(complete)return;L10n.SetText(label,progress.Label);bar.IsIndeterminate=progress.Fraction is null;if(progress.Fraction is {} fraction)bar.Value=Math.Clamp(fraction,0,1);
        if(progress.Fraction is {} value)detail.Text=value.ToString("P0",System.Globalization.CultureInfo.CurrentCulture);else L10n.SetText(detail,"Working…");
    }
    public void Complete(IReadOnlyList<string> results)
    {
        complete=true;outputs=results;
        L10n.SetText(label,results.Count==0?"All set. You’re ready to go.":$"Nicely done. {results.Count} {(results.Count==1?"file is":"files are")} ready.");
        detail.Text=results.Count>0?Path.GetFileName(results[0]):L10n.Text("Ready.");bar.IsVisible=false;L10n.Bind(action,ContentControl.ContentProperty,"Show in Files");action.IsEnabled=results.Count>0;
        DispatcherTimer.RunOnce(async()=>{await NashiTheme.FadeOut(this);Close();},TimeSpan.FromSeconds(3));
    }
}
