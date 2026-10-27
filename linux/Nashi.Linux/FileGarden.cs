using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
namespace Nashi.Linux;

// The same composition and exported pear artwork as macOS HomeView.FileGarden.
internal sealed class FileGarden:Grid
{
    private readonly Image mark;
    internal FileGarden()
    {
        Width=166;Height=150;IsHitTestVisible=false;
        Children.Add(new Ellipse{Width=144,Height=144}.Res(Shape.FillProperty,"Sand"));
        AddCard("PDF",-16,-34,12,"Muted");AddCard("PNG",7,31,-2,"Gold");
        mark=new Image{Width=65,Height=65,RenderTransform=new TranslateTransform(0,42)};Children.Add(mark);SetSelected(false);
    }
    internal void SetSelected(bool selected)=>mark.Source=Ui.Asset(selected?"NashiMarkHappy":"NashiMark");
    private void AddCard(string label,double angle,double x,double y,string color)
    {
        var content=new StackPanel();
        // Drawn symbols rather than font glyphs, which vary between Linux font sets.
        var glyph=new Canvas{Width=32,Height=30,Margin=new(0,0,0,8),HorizontalAlignment=HorizontalAlignment.Left};
        glyph.Children.Add(new Avalonia.Controls.Shapes.Path{Data=Geometry.Parse(label=="PDF"?"M5,1 L22,1 L29,8 L29,29 L5,29 Z M22,1 L22,8 L29,8 M10,14 L24,14 M10,19 L24,19 M10,24 L20,24":"M2,4 L30,4 L30,27 L2,27 Z M3,25 L12,14 L18,20 L23,14 L30,23 M11,10 A2,2 0 1 1 7,10 A2,2 0 1 1 11,10"),StrokeThickness=2,StrokeJoin=PenLineJoin.Round}.Res(Shape.StrokeProperty,color));
        content.Children.Add(glyph);
        content.Children.Add(new TextBlock{Text=label,FontSize=10,FontWeight=FontWeight.Bold,Margin=new(0,0,0,8)}.Res(TextBlock.ForegroundProperty,color));
        content.Children.Add(new Border{Width=37,Height=4,CornerRadius=new(2),HorizontalAlignment=HorizontalAlignment.Left,Opacity=.18}.Res(Border.BackgroundProperty,color));
        var transforms=new TransformGroup();transforms.Children.Add(new RotateTransform(angle));transforms.Children.Add(new TranslateTransform(x,y));
        Children.Add(new Border{Width=77,Height=103,CornerRadius=new(15),Padding=new(13),Child=content,BorderThickness=new(1),RenderTransformOrigin=RelativePoint.Center,RenderTransform=transforms}.Res(Border.BackgroundProperty,"Surface").Res(Border.BorderBrushProperty,"Line"));
    }
}
