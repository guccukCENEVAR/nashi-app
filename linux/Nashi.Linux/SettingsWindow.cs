using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed class SettingsWindow:Window
{
    private readonly LibraryStore? appearanceStore;
    private readonly PreferencesStore store;private readonly Action changed;private readonly Func<Task> update;
    private readonly StackPanel page=new(){Margin=new(28)};private readonly ListBox sections=new(){Width=175};
    private static readonly string[] Sections=["General","Formats","Compatibility","Folders","Updates","About"];
    public SettingsWindow(PreferencesStore store,Action changed,Func<Task> update,LibraryStore? appearanceStore=null)
    {
        this.appearanceStore=appearanceStore;this.store=store;this.changed=changed;this.update=update;
        Width=850;Height=680;MinWidth=740;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;Icon=Ui.Icon();L10n.Title(this,"Make it yours · Nashi");
        sections.ItemsSource=Sections.Select(s=>L10n.Bind(new ListBoxItem{Tag=s},ContentControl.ContentProperty,s)).ToArray();sections.SelectedIndex=0;sections.Margin=new(12);
        var root=new DockPanel();Content=root;DockPanel.SetDock(sections,Dock.Left);root.Children.Add(sections);root.Children.Add(new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        sections.SelectionChanged+=(_,_)=>ShowSection();ShowSection();
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}},RoutingStrategies.Tunnel);
    }
    private string Section=>(sections.SelectedItem as ListBoxItem)?.Tag as string??"General";
    private async void Save(Preferences value){try{store.Save(value);changed();}catch(Exception error)when(error is IOException or ArgumentException or UnauthorizedAccessException){await Dialogs.Info(this,error.Message,"Settings could not be saved");}}
    private void Heading(string title,string hint){page.Children.Add(Ui.Heading(title));var text=Ui.Text(hint,13,"Muted");text.Margin=new(0,8,0,20);page.Children.Add(text);}
    private void Toggle(string title,bool value,Action<bool> save)
    {
        var box=new CheckBox{IsChecked=value,Margin=new(4,12)};L10n.Bind(box,ContentControl.ContentProperty,title);box.IsCheckedChanged+=(_,_)=>save(box.IsChecked==true);page.Children.Add(box);
    }
    private void Pick(string title,string[] values,string value,Action<string> save)
    {
        var label=Ui.Text(title,13);label.Margin=new(4,12,4,0);page.Children.Add(label);var combo=L10n.Choices(values,value);
        combo.SelectionChanged+=(_,_)=>{if(L10n.Selected(combo) is {} selected&&selected!=value)save(selected);};page.Children.Add(combo);
    }
    private void LanguagePicker()
    {
        var label=Ui.Text("Language",13);label.Margin=new(4,12,4,0);page.Children.Add(label);
        var language=new ComboBox{ItemsSource=new[]{"English","Türkçe"},SelectedIndex=store.Value.Language=="tr"?1:0,MinWidth=160};AutomationProperties.SetAutomationId(language,"AppLanguage");
        language.SelectionChanged+=(_,_)=>{var chosen=language.SelectedIndex==1?"tr":"en";if(chosen!=store.Value.Language)Save(store.Value with{Language=chosen});};page.Children.Add(language);
    }
    private void ShowSection()
    {
        page.Children.Clear();
        switch(Section)
        {
            case "General":
                Heading("Your kind of flow.","A few small preferences. A smoother everyday.");LanguagePicker();
                if(appearanceStore is not null)Pick("Appearance",["system","light","dark"],appearanceStore.Appearance,async v=>{try{appearanceStore.SetAppearance(v);NashiTheme.Apply(v);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){await Dialogs.Info(this,error.Message,"Appearance could not be saved");}});
                Toggle("Enable Nashi drag shortcuts",store.Value.Enabled,v=>Save(store.Value with{Enabled=v}));
                if(!GlobalDragController.GlobalGesturesAvailable)page.Children.Add(Ui.Text("Wayland keeps other apps’ drags and keys private, so the wheel can’t follow a drag everywhere. Drag files onto the Nashi window and drop them on Convert, Tools or File actions.",11,"Muted").Also(t=>t.Margin=new(30,0,4,8)));
                Toggle("Sound feedback",store.Value.Feedback,v=>Save(store.Value with{Feedback=v}));page.Children.Add(Ui.Button("Try feedback",()=>{var was=Feedback.Enabled;Feedback.Enabled=true;Feedback.Play();Feedback.Enabled=was;}));
                Toggle("Subtle animations",store.Value.Animations,v=>Save(store.Value with{Animations=v}));Toggle("Keep running in the system tray",store.Value.KeepInTray,v=>Save(store.Value with{KeepInTray=v}));Toggle("Reveal finished files in Files",store.Value.RevealFinished,v=>Save(store.Value with{RevealFinished=v}));
                Pick("Wheel finish",["glass","solid"],store.Value.WheelFinish,v=>Save(store.Value with{WheelFinish=v}));
                var sizeLabel=Ui.Text("Wheel size",13);sizeLabel.Margin=new(4,12,4,0);page.Children.Add(sizeLabel);
                var size=new Slider{Minimum=.85,Maximum=1.4,Value=store.Value.WheelScale,Margin=new(4,4,4,8)};L10n.Tip(size,"Wheel size");size.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty&&Math.Abs(size.Value-store.Value.WheelScale)>.001)Save(store.Value with{WheelScale=Math.Round(size.Value,3)});};page.Children.Add(size);
                var modifiers=new[]{"Ctrl","Shift","Alt","Ctrl+Shift","Ctrl+Alt","Shift+Alt"};
                Pick("Tools · while dragging",modifiers.Where(m=>m!=store.Value.ConvertModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ToolsModifier,v=>{Save(store.Value with{ToolsModifier=v});ShowSection();});
                Pick("Convert · while dragging",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ActionsModifier).ToArray(),store.Value.ConvertModifier,v=>{Save(store.Value with{ConvertModifier=v});ShowSection();});
                Pick("File actions · while dragging",modifiers.Where(m=>m!=store.Value.ToolsModifier&&m!=store.Value.ConvertModifier).ToArray(),store.Value.ActionsModifier,v=>{Save(store.Value with{ActionsModifier=v});ShowSection();});
                Pick("Float behavior",["createNew","addToExisting"],store.Value.FloatBehavior,v=>Save(store.Value with{FloatBehavior=v}));
                Toggle("Launch at login",Autostart.Enabled,async v=>{try{Autostart.Set(v);}catch(Exception error)when(error is IOException or UnauthorizedAccessException){await Dialogs.Info(this,error.Message,"Launch at login");}});
                var shortcut=Ui.Text("Download a copied link from anywhere: on X11 press Ctrl+Shift+D. On Wayland, add a custom shortcut in your desktop settings that runs: nashi --open-copied-link",11,"Muted");shortcut.Margin=new(4,14,4,4);page.Children.Add(shortcut);
                break;
            case "Formats":FormatDefaults();break;
            case "Compatibility":
                Heading("Everything has a next form.","The same conversion and tool catalog as macOS.");
                foreach(var pair in FeatureCatalog.Conversions.OrderBy(p=>p.Key,StringComparer.Ordinal))page.Children.Add(new TextBlock{Text=pair.Key.ToUpperInvariant()+"  →  "+string.Join(", ",pair.Value.Select(v=>v.ToUpperInvariant())),TextWrapping=Avalonia.Media.TextWrapping.Wrap,Margin=new(4,8)});
                break;
            case "Folders":
                Heading("A place for fresh files.","Originals remain safe. Choose where new copies should land.");
                Pick("Save new copies",["beside","ask","folder"],store.Value.OutputMode,v=>Save(store.Value with{OutputMode=v}));
                page.Children.Add(Ui.Button("Choose output folder…",async()=>{var picked=await Dialogs.PickFolder(this,"Save new copies here",store.Value.OutputFolder);if(picked is not null){Save(store.Value with{OutputFolder=picked,OutputMode="folder"});ShowSection();}}));page.Children.Add(Ui.Text(store.Value.OutputFolder,12,"Muted",localize:false));
                page.Children.Add(Ui.Button("Choose downloads folder…",async()=>{var picked=await Dialogs.PickFolder(this,"Save downloaded media here",store.Value.DownloadFolder);if(picked is not null){Save(store.Value with{DownloadFolder=picked});ShowSection();}}));page.Children.Add(Ui.Text(store.Value.DownloadFolder,12,"Muted",localize:false));
                break;
            case "Updates":
                Heading("Fresh things, on their way.","Nashi tells you when a newer public release exists. Your package manager installs it; Nashi never replaces its own files.");
                Toggle("Check for updates automatically",store.Value.AutomaticUpdateChecks,v=>Save(store.Value with{AutomaticUpdateChecks=v}));page.Children.Add(Ui.Button("Check for updates",async()=>await update()));
                break;
            default:
                Heading("Nashi","A little pear. A lot of possibility.");
                page.Children.Add(Ui.Text("Local by design · MIT source",13).Also(t=>t.Margin=new(4,12,4,0)));page.Children.Add(Ui.Text("Version "+Updates.Current.ToString(3),13,"Muted").Also(t=>t.Margin=new(4,4,4,12)));
                page.Children.Add(Ui.Button("Source & third-party notices",()=>Ui.OpenUrl("https://github.com/"+PublicUpdates.Repository)));
                page.Children.Add(Ui.Button("Open local notices",()=>FileActions.Open(Path.Combine(AppContext.BaseDirectory,"THIRD-PARTY-NOTICES.txt"))));
                break;
        }
    }
    private void FormatDefaults()
    {
        Heading("Your go-to exports.","Defaults apply whenever you choose an output format.");
        var formats=FeatureCatalog.Conversions.Values.SelectMany(v=>v).Distinct().Order(StringComparer.Ordinal).ToArray();var select=L10n.Choices(formats,formats[0]);page.Children.Add(select);var panel=new StackPanel();page.Children.Add(panel);
        void Render()
        {
            panel.Children.Clear();var name=L10n.Selected(select)??formats[0];var profile=store.Profile(name);var fields=new Dictionary<string,TextBox>();var choices=new Dictionary<string,ComboBox>();
            void Number(string key,string title,double value){panel.Children.Add(Ui.Text(title,13).Also(t=>t.Margin=new(4,12,4,0)));var input=new TextBox{Text=value.ToString("0.###",CultureInfo.CurrentCulture)};fields[key]=input;panel.Children.Add(input);}
            void Choice(string key,string title,string[] values,string value){panel.Children.Add(Ui.Text(title,13).Also(t=>t.Margin=new(4,12,4,0)));var input=L10n.Choices(values,value);choices[key]=input;panel.Children.Add(input);}
            var audio=MediaEngine.AudioFormats.Contains(name);var video=MediaEngine.VideoFormats.Contains(name)&&name!="gif";var image=new[]{"png","jpg","heic","webp","avif","tiff","bmp"}.Contains(name);
            if(video||new[]{"jpg","heic","webp","avif"}.Contains(name))Number("quality","Quality (%)",profile.Quality*100);
            if(video||audio&&name is not ("wav" or "aiff" or "flac"))Number("bitrate","Audio bitrate (kbps)",profile.AudioBitrate);
            if(name is "wav" or "aiff")Choice("depth","Audio sample depth",["matchSource","pcm16","pcm24","pcm32","float32"],profile.SampleDepth);
            if(video)Number("height","Maximum video height (0 = original)",profile.MaxVideoHeight);
            if(name=="gif"){Number("fps","GIF frames per second",profile.GifFrameRate);Number("width","GIF width",profile.GifWidth);}
            if(name is "png" or "jpg" or "epub")Number("dpi","PDF rendering DPI",profile.Dpi);
            if(name is "docx" or "pages")Choice("layout","PDF layout",["editable","preserve"],profile.Layout);
            if(name=="epub")Choice("epub","EPUB layout",["reflowable","fixed"],profile.EpubLayout);
            var metadata=new CheckBox{IsChecked=profile.PreserveMetadata,Margin=new(4,12)};L10n.Bind(metadata,ContentControl.ContentProperty,"Preserve metadata");var showMetadata=video||audio||image&&name!="bmp";if(showMetadata)panel.Children.Add(metadata);
            if(fields.Count+choices.Count==0&&!showMetadata)panel.Children.Add(Ui.Text("This format uses its original document structure. No export defaults are needed.",13).Also(t=>t.Margin=new(4,18)));
            double Value(string key,double fallback)=>fields.TryGetValue(key,out var field)?double.Parse(field.Text??"",CultureInfo.CurrentCulture):fallback;
            string Picked(string key,string fallback)=>choices.TryGetValue(key,out var field)?L10n.Selected(field)??fallback:fallback;
            panel.Children.Add(Ui.Button("Save defaults",async()=>{try{store.SetProfile(name,profile with{Quality=Value("quality",profile.Quality*100)/100,AudioBitrate=checked((int)Value("bitrate",profile.AudioBitrate)),SampleDepth=Picked("depth",profile.SampleDepth),MaxVideoHeight=checked((int)Value("height",profile.MaxVideoHeight)),GifFrameRate=checked((int)Value("fps",profile.GifFrameRate)),GifWidth=checked((int)Value("width",profile.GifWidth)),Dpi=checked((int)Value("dpi",profile.Dpi)),Layout=Picked("layout",profile.Layout),EpubLayout=Picked("epub",profile.EpubLayout),PreserveMetadata=metadata.IsChecked==true});changed();}catch(Exception error)when(error is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException){await Dialogs.Info(this,error.Message,"Check export defaults");}}));
            panel.Children.Add(Ui.Button("Restore defaults",async()=>{try{store.SetProfile(name,new());Render();changed();}catch(Exception error)when(error is IOException or UnauthorizedAccessException){await Dialogs.Info(this,error.Message,"Defaults could not be restored");}}));
        }
        select.SelectionChanged+=(_,_)=>Render();Render();
    }
}
