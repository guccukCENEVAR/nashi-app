using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
namespace Nashi.Linux;

internal static class NashiTheme
{
    internal static bool Dark=>Application.Current?.ActualThemeVariant==ThemeVariant.Dark;
    internal static bool AnimationsEnabled=>WheelPresentation.Preferences.Animations;
    internal static void Apply(string appearance)
    {
        if(Application.Current is {} app)app.RequestedThemeVariant=appearance switch{"light"=>ThemeVariant.Light,"dark"=>ThemeVariant.Dark,_=>ThemeVariant.Default};
    }
    internal static IBrush Brush(string key)
    {
        var app=Application.Current;
        return app is not null&&app.TryGetResource(key,app.ActualThemeVariant,out var value)&&value is IBrush brush?brush:Brushes.Gray;
    }
    internal static Color Color(string key)=>Brush(key) is ISolidColorBrush solid?solid.Color:Colors.Gray;
    /// Follows the light/dark palette, like WPF's SetResourceReference.
    internal static T Res<T>(this T control,AvaloniaProperty property,string key)where T:Control{control.Bind(property,control.GetResourceObservable(key));return control;}
    internal static void Entrance(Visual element)
    {
        if(!AnimationsEnabled)return;
        var animation=new Animation{Duration=TimeSpan.FromMilliseconds(180),Easing=new CubicEaseOut(),FillMode=FillMode.None,Children=
        {
            new KeyFrame{Cue=new Cue(0),Setters={new Setter(Visual.OpacityProperty,0d),new Setter(TranslateTransform.YProperty,8d)}},
            new KeyFrame{Cue=new Cue(1),Setters={new Setter(Visual.OpacityProperty,1d),new Setter(TranslateTransform.YProperty,0d)}}
        }};
        element.RenderTransform??=new TranslateTransform();
        _ = animation.RunAsync(element);
    }
    internal static Task FadeIn(Visual element,int milliseconds=140)
    {
        if(!AnimationsEnabled)return Task.CompletedTask;
        var animation=new Animation{Duration=TimeSpan.FromMilliseconds(milliseconds),FillMode=FillMode.None,Children={new KeyFrame{Cue=new Cue(0),Setters={new Setter(Visual.OpacityProperty,0d)}},new KeyFrame{Cue=new Cue(1),Setters={new Setter(Visual.OpacityProperty,1d)}}}};
        return animation.RunAsync(element);
    }
    internal static async Task FadeOut(Visual element,int milliseconds=280)
    {
        if(!AnimationsEnabled)return;
        var animation=new Animation{Duration=TimeSpan.FromMilliseconds(milliseconds),FillMode=FillMode.Forward,Children={new KeyFrame{Cue=new Cue(0),Setters={new Setter(Visual.OpacityProperty,1d)}},new KeyFrame{Cue=new Cue(1),Setters={new Setter(Visual.OpacityProperty,0d)}}}};
        await animation.RunAsync(element);
    }
}
