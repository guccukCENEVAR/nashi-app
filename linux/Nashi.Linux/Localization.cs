using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Nashi.Core;
namespace Nashi.Linux;

/// Live UI language. Observers are held weakly: a binding lives exactly as long
/// as the control that owns it, so discarded rows never stay rooted here.
internal sealed class LanguageState
{
    public static LanguageState Shared{get;}=new();
    private readonly List<WeakReference<IObserver<string>>> observers=[];
    private string language="en";
    public event Action? Changed;
    public string Language
    {
        get=>language;
        set
        {
            var next=AppLocalization.Normalize(value);if(next==language)return;language=next;
            foreach(var observer in Live())observer.OnNext(next);
            Changed?.Invoke();
        }
    }
    internal IObservable<string> Translate(string source,bool uppercase=false,Func<string,string>? format=null)=>new Translation(this,source,uppercase,format);
    private IObserver<string>[] Live()
    {
        observers.RemoveAll(o=>!o.TryGetTarget(out _));
        return observers.Select(o=>o.TryGetTarget(out var t)?t:null).OfType<IObserver<string>>().ToArray();
    }
    private sealed class Translation(LanguageState state,string source,bool uppercase,Func<string,string>? format):IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer)
        {
            var mapped=new Mapped(observer,source,uppercase,format);var reference=new WeakReference<IObserver<string>>(mapped);
            state.observers.Add(reference);mapped.OnNext(state.language);
            return new Subscription(()=>state.observers.Remove(reference),mapped);
        }
    }
    private sealed class Mapped(IObserver<string> target,string source,bool uppercase,Func<string,string>? format):IObserver<string>
    {
        public void OnNext(string value){var text=LinuxCatalog.Text(source,value);text=uppercase?text.ToUpper(CultureInfo.GetCultureInfo(value)):text;target.OnNext(format is null?text:format(text));}
        public void OnError(Exception error)=>target.OnError(error);
        public void OnCompleted()=>target.OnCompleted();
    }
    // Keeps the mapping observer alive for as long as the binding holds its subscription.
    private sealed class Subscription(Action dispose,object keepAlive):IDisposable
    {
        private object? retained=keepAlive;
        public void Dispose(){if(retained is null)return;retained=null;dispose();}
    }
}

internal static class L10n
{
    internal static string Language{get=>LanguageState.Shared.Language;set=>LanguageState.Shared.Language=value;}
    internal static string Text(string source)=>LinuxCatalog.Text(source,Language);
    internal static string Uppercase(string source)=>Text(source).ToUpper(CultureInfo.GetCultureInfo(Language));
    internal static T Bind<T>(T target,AvaloniaProperty property,string source)where T:AvaloniaObject{target.Bind(property,LanguageState.Shared.Translate(source));return target;}
    internal static TextBlock Label(TextBlock text){Bind(text,TextBlock.TextProperty,text.Text??"");return text;}
    internal static void SetText(TextBlock text,string source)=>Bind(text,TextBlock.TextProperty,source);
    internal static T Content<T>(T control)where T:ContentControl{if(control.Content is string source)Bind(control,ContentControl.ContentProperty,source);return control;}
    internal static void Tip(Control control,string source)=>control.Bind(ToolTip.TipProperty,LanguageState.Shared.Translate(source));
    internal static void Title(Window window,string source)=>Bind(window,Window.TitleProperty,source);

    private static readonly Dictionary<string,string> ChoiceLabels=new(StringComparer.Ordinal){["balanced"]="Balanced",["strong"]="Strong",["matchSource"]="Match source",["pcm16"]="16-bit PCM",["pcm24"]="24-bit PCM",["pcm32"]="32-bit PCM",["float32"]="32-bit float",["createNew"]="Create a new Float",["addToExisting"]="Add to an existing Float",["beside"]="Beside original files",["ask"]="Ask each time",["folder"]="Selected folder",["editable"]="Editable paragraphs",["preserve"]="Preserve original pages",["reflowable"]="Reflowable",["fixed"]="Fixed pages",["video"]="Video",["audio"]="Audio · MP3",["thumbnail"]="Thumbnail",["photos"]="Photos",["image"]="Image",["glass"]="Glass",["solid"]="Solid",["gradient"]="Gradient",["grid"]="Grid",["row"]="Row",["column"]="Column",["featured"]="Featured",["blur"]="Blur",["pixelate"]="Pixelate",["bleep"]="Bleep",["mute"]="Mute",["waveform"]="Waveform",["spectrum"]="Spectrum",["bars"]="Bars",["system"]="System",["light"]="Light",["dark"]="Dark"};
    private static readonly HashSet<string> Formats=FeatureCatalog.Conversions.Values.SelectMany(v=>v).ToHashSet();
    /// Display text for a stored option value such as "pcm24" or "webp".
    internal static string ChoiceSource(string value)=>ChoiceLabels.GetValueOrDefault(value,Formats.Contains(value)?value.ToUpperInvariant():value);
    internal static string Choice(string value)=>Text(ChoiceSource(value));

    /// A ComboBox whose items are stored values and whose labels follow the UI language.
    internal static ComboBox Choices(IEnumerable<string> values,string? selected=null,double minWidth=110)
    {
        var combo=new ComboBox{MinWidth=minWidth};SetChoices(combo,values,selected);return combo;
    }
    internal static void SetChoices(ComboBox combo,IEnumerable<string> values,string? selected=null)
    {
        var items=values.Select(v=>Bind(new ComboBoxItem{Tag=v},ContentControl.ContentProperty,ChoiceSource(v))).ToArray();combo.ItemsSource=items;
        combo.SelectedItem=items.FirstOrDefault(i=>(string?)i.Tag==selected)??items.FirstOrDefault();
    }
    internal static string? Selected(ComboBox combo)=>(combo.SelectedItem as ComboBoxItem)?.Tag as string;
    internal static void Select(ComboBox combo,string value){if(combo.ItemsSource is IEnumerable<ComboBoxItem> items&&items.FirstOrDefault(i=>(string?)i.Tag==value) is {} item)combo.SelectedItem=item;}
    internal static string[] Values(ComboBox combo)=>combo.ItemsSource is IEnumerable<ComboBoxItem> items?items.Select(i=>(string)i.Tag!).ToArray():[];
}

/// Turkish copy for Linux-only wording (package names, Files, KDE Connect…). The shared
/// catalog in Nashi.Core stays the source for every string the platforms have in common.
internal static class LinuxCatalog
{
    internal static IReadOnlyDictionary<string,string> Turkish{get;}=Load();
    private static Dictionary<string,string> Load()
    {
        using var stream=typeof(LinuxCatalog).Assembly.GetManifestResourceStream("Nashi.Linux.Turkish.json")??throw new InvalidDataException("Missing Linux Turkish UI catalog.");
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??throw new InvalidDataException("Invalid Linux Turkish UI catalog.");
    }
    private static readonly (System.Text.RegularExpressions.Regex Pattern,string Target)[] templates=Turkish.Where(x=>x.Key.Contains("{0}",StringComparison.Ordinal)).OrderByDescending(x=>x.Key.Length).Select(x=>
    {
        var pattern=System.Text.RegularExpressions.Regex.Escape(x.Key);
        for(var i=0;i<10;i++)pattern=pattern.Replace(System.Text.RegularExpressions.Regex.Escape("{"+i+"}"),"(.*?)",StringComparison.Ordinal);
        return(new System.Text.RegularExpressions.Regex("^"+pattern+"$",System.Text.RegularExpressions.RegexOptions.Singleline|System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)),x.Value);
    }).ToArray();
    internal static string Text(string source,string language)
    {
        if(AppLocalization.Normalize(language)!="tr")return source;
        if(Turkish.TryGetValue(source,out var exact))return exact;
        if(source.Length<16_384)foreach(var (pattern,target) in templates)
        {
            var match=pattern.Match(source);if(!match.Success)continue;
            return System.Text.RegularExpressions.Regex.Replace(target,@"\{([0-9])\}",token=>int.TryParse(token.Groups[1].Value,out var index)&&index+1<match.Groups.Count?match.Groups[index+1].Value:token.Value);
        }
        return AppLocalization.Text(source,language);
    }
}
