using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views;

public sealed class HeadsetAudioWindow : Window
{
    private readonly IHeadsetAudioBackend backend;
    private readonly AppSettings settings;
    private readonly Action save;
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer volumeDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private IReadOnlyList<AudioEndpointInfo> endpoints = Array.Empty<AudioEndpointInfo>();
    private bool loading, refreshing, changingMic, changingVolume;
    private int generation;
    private string selectedId, pendingVolumeId;
    private int pendingVolume;
    private readonly CheckBox otherOutputs = new() { Content = "显示其他音频输出", Margin = new Thickness(0, 10, 0, 0), FontSize = 11 };
    internal ComboBox OutputSelector { get; } = new() { MinHeight = 34, FontSize = 12, Margin = new Thickness(0, 7, 0, 0) };
    internal ComboBox MicrophoneSelector { get; } = new() { MinHeight = 34, FontSize = 12, Margin = new Thickness(0, 7, 0, 10) };
    internal Slider VolumeSlider { get; } = new() { Minimum=0, Maximum=100, TickFrequency=1, IsSnapToTickEnabled=true, Margin=new Thickness(0,12,0,3) };
    internal Button MuteButton { get; }
    internal TextBlock StatusText { get; }
    internal TextBlock VolumeText { get; }
    private readonly TextBlock micState, empty;
    private readonly SolidColorBrush text = new(), dim = new(), card = new(), accent = new();
    private readonly Border root;

    public HeadsetAudioWindow() : this(new WindowsHeadsetAudioBackend(), App.Settings, App.Theme, App.SaveSettings) { }

    internal HeadsetAudioWindow(IHeadsetAudioBackend backend, AppSettings settings, ThemePreset theme, Action save, bool liveUpdates = true)
    {
        this.backend=backend; this.settings=settings; this.save=save;
        settings.HeadsetMicrophoneBindings ??= new();
        selectedId=settings.HeadsetOutputId;
        Title="MouseDock · 耳机"; Width=430; SizeToContent=SizeToContent.Height;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize; ShowInTaskbar=true;
        FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
        text.Color=theme.Text; dim.Color=theme.TextDim; accent.Color=theme.Accent;
        card.Color=Color.FromArgb(theme.IsLight ? (byte)180 : (byte)18,255,255,255);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/MouseDock;component/Controls/AudioControls.xaml",UriKind.Relative) });
        Resources["AudioAccentBrush"]=accent; Resources["AudioTrackBrush"]=dim;
        VolumeSlider.Style=(Style)FindResource("AudioVolumeSlider");
        root=new Border { CornerRadius=new CornerRadius(18), Padding=new Thickness(20), BorderThickness=new Thickness(1),
            BorderBrush=new SolidColorBrush(Color.FromArgb(30,128,128,128)),
            Background=new LinearGradientBrush(theme.BgTop,theme.BgBottom,90) };
        Content=root;
        var body=new StackPanel();
        root.Child=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(260,SystemParameters.WorkArea.Height-70) };
        OutputSelector.SetBinding(ToolTipProperty,new Binding("SelectedItem.Name") { Source=OutputSelector });
        MicrophoneSelector.SetBinding(ToolTipProperty,new Binding("SelectedItem.Name") { Source=MicrophoneSelector });
        System.Windows.Automation.AutomationProperties.SetName(OutputSelector,"耳机播放设备");
        System.Windows.Automation.AutomationProperties.SetName(MicrophoneSelector,"对应麦克风");
        System.Windows.Automation.AutomationProperties.SetName(VolumeSlider,"耳机音量百分比");
        var header=new Grid { Margin=new Thickness(0,0,0,6) };
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        header.Children.Add(Label("耳机",20,true));
        var close=Button("×"); close.Padding=new Thickness(10,2,10,4); close.FontSize=18;
        close.Click+=(_,__)=>Hide(); Grid.SetColumn(close,1); header.Children.Add(close);
        header.MouseLeftButtonDown+=(_,e)=> { if (e.OriginalSource is TextBlock && IsVisible) DragMove(); };
        body.Children.Add(header);
        body.Children.Add(Label("设备名称由 Windows 提供",11));

        var outputCard=Card(body,"播放设备"); outputCard.Children.Add(OutputSelector);
        otherOutputs.Foreground=dim; outputCard.Children.Add(otherOutputs);
        var help=Label("USB 耳机若显示为“扬声器”，可展开后选择。",10); help.Margin=new Thickness(0,5,0,0); outputCard.Children.Add(help);
        empty=Label("未发现耳机，请连接后刷新，或显示其他音频输出。",11); empty.Margin=new Thickness(0,9,0,0); outputCard.Children.Add(empty);

        var volumeCard=Card(body,"耳机音量");
        VolumeText=Label("—",26,true); VolumeText.Margin=new Thickness(0,7,0,0);
        volumeCard.Children.Add(VolumeText); volumeCard.Children.Add(VolumeSlider);
        var volumeHelp=Label("调节所选播放设备，影响使用该设备的应用。",10); volumeCard.Children.Add(volumeHelp);

        var microphoneCard=Card(body,"麦克风"); microphoneCard.Children.Add(MicrophoneSelector);
        micState=Label("请先选择对应的麦克风",11); microphoneCard.Children.Add(micState);
        MuteButton=Button("麦克风静音"); MuteButton.Margin=new Thickness(0,10,0,0);
        microphoneCard.Children.Add(MuteButton);
        var micHelp=Label("控制 Windows 麦克风静音状态，可随时恢复。",10); micHelp.Margin=new Thickness(0,8,0,0); microphoneCard.Children.Add(micHelp);

        StatusText=Label("",11); StatusText.Margin=new Thickness(0,10,0,10); body.Children.Add(StatusText);
        var refresh=Button("刷新设备"); refresh.Click+=async(_,__)=>await RefreshAsync(); body.Children.Add(refresh);
        OutputSelector.SelectionChanged+=(_,__)=>
        {
            if (loading) return;
            volumeDelay.Stop(); generation++;
            selectedId=(OutputSelector.SelectedItem as AudioEndpointInfo)?.Id;
            settings.HeadsetOutputId=selectedId??""; save();
            StatusText.Text="";
            UpdateControls();
        };
        MicrophoneSelector.SelectionChanged+=(_,__)=>
        {
            if (loading || selectedId==null) return;
            generation++;
            var choice=MicrophoneSelector.SelectedItem as MicChoice;
            // Persist an explicit empty choice too: it must not auto-select another microphone later.
            settings.HeadsetMicrophoneBindings[selectedId]=choice?.Id??"";
            settings.HeadsetOutputId=selectedId; save(); UpdateMicState();
        };
        otherOutputs.Checked+=(_,__)=>DisplayEndpoints(endpoints);
        otherOutputs.Unchecked+=(_,__)=>DisplayEndpoints(endpoints);
        VolumeSlider.ValueChanged+=(_,__)=>
        {
            VolumeText.Text=$"{VolumeSlider.Value:0}%";
            if (loading || selectedId==null || !VolumeSlider.IsEnabled) return;
            pendingVolumeId=selectedId; pendingVolume=(int)Math.Round(VolumeSlider.Value);
            volumeDelay.Stop(); volumeDelay.Start();
        };
        volumeDelay.Tick+=async(_,__)=> { volumeDelay.Stop(); await ApplyVolumeAsync(pendingVolumeId,pendingVolume); };
        MuteButton.Click+=async(_,__)=>await ToggleMicrophoneAsync();
        poll.Tick+=async(_,__)=>
        {
            if (!changingMic && !changingVolume && !volumeDelay.IsEnabled && !VolumeSlider.IsMouseCaptureWithin &&
                !OutputSelector.IsDropDownOpen && !MicrophoneSelector.IsDropDownOpen) await RefreshAsync();
        };
        if (liveUpdates) IsVisibleChanged+=async(_,__)=>
        {
            if (IsVisible) { poll.Start(); await RefreshAsync(); }
            else { poll.Stop(); volumeDelay.Stop(); generation++; }
        };
        DisplayEndpoints(endpoints);
    }

    private TextBlock Label(string value,double size,bool bold=false) => new()
    {
        Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,
        Foreground=size<=11?dim:text,TextWrapping=TextWrapping.Wrap
    };
    private Button Button(string label) => new()
    {
        Content=label,Padding=new Thickness(12,8,12,8),FontSize=12,Cursor=Cursors.Hand,
        Style=(Style)FindResource("AudioGhostButton"),
        Foreground=text,Background=card,BorderBrush=accent,BorderThickness=new Thickness(1)
    };
    private StackPanel Card(Panel body,string title)
    {
        var panel=new StackPanel(); panel.Children.Add(Label(title,11,true));
        body.Children.Add(new Border { Child=panel,Background=card,CornerRadius=new CornerRadius(12),
            Padding=new Thickness(14),Margin=new Thickness(0,12,0,0) });
        return panel;
    }

    internal async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing=true;
        int request=generation;
        try
        {
            var snapshot=await Task.Run(backend.Enumerate);
            if (request==generation) DisplayEndpoints(snapshot);
        }
        catch { if (request==generation) { StatusText.Text="读取音频设备失败，请检查 Windows 音频服务后刷新。"; DisableControls(); } }
        finally { refreshing=false; }
    }

    internal void DisplayEndpoints(IReadOnlyList<AudioEndpointInfo> snapshot)
    {
        endpoints=snapshot;
        loading=true;
        try
        {
            var outputs=snapshot.Where(e=>!e.Capture && (otherOutputs.IsChecked==true || e.IsHeadset || e.Id==selectedId)).ToList();
            OutputSelector.IsEnabled=true;
            OutputSelector.ItemsSource=outputs;
            var selected=outputs.FirstOrDefault(e=>e.Id==selectedId);
            if (string.IsNullOrEmpty(selectedId)) selected??=outputs.FirstOrDefault();
            OutputSelector.SelectedItem=selected;
            if (selected!=null) selectedId=selected.Id;
            empty.Text=string.IsNullOrEmpty(selectedId)?"未发现耳机，请连接后刷新，或显示其他音频输出。":"所选耳机已断开，请重新连接或选择其他设备。";
            empty.Visibility=selected==null?Visibility.Visible:Visibility.Collapsed;
        }
        finally { loading=false; }
        UpdateControls();
    }

    private void UpdateControls()
    {
        loading=true;
        try
        {
            var output=OutputSelector.SelectedItem as AudioEndpointInfo;
            bool pendingSame=volumeDelay.IsEnabled && pendingVolumeId==output?.Id;
            if (!pendingSame) volumeDelay.Stop();
            VolumeSlider.IsEnabled=output?.Volume!=null;
            VolumeSlider.Value=pendingSame?pendingVolume:output?.Volume??0;
            VolumeText.Text=output?.Volume!=null?$"{VolumeSlider.Value:0}%":"—";
            string paired=HeadsetAudioPolicy.MicrophoneFor(output,endpoints,settings.HeadsetMicrophoneBindings);
            var choices=new[] { new MicChoice("","请选择对应的麦克风") }.Concat(endpoints.Where(e=>e.Capture).Select(e=>new MicChoice(e.Id,e.Name))).ToArray();
            MicrophoneSelector.ItemsSource=choices;
            MicrophoneSelector.SelectedItem=choices.FirstOrDefault(c=>c.Id==paired)??choices[0];
            MicrophoneSelector.IsEnabled=output!=null;
            StatusText.Text=output?.Error??"";
        }
        finally { loading=false; }
        UpdateMicState();
    }

    private void UpdateMicState()
    {
        string id=(MicrophoneSelector.SelectedItem as MicChoice)?.Id;
        var mic=endpoints.FirstOrDefault(e=>e.Capture && e.Id==id);
        MuteButton.IsEnabled=mic?.Muted!=null && !changingMic;
        micState.Text=mic?.Muted is bool muted ? (muted?"已静音":"麦克风未静音") : "未选定可控制的麦克风";
        MuteButton.Content=mic?.Muted==true?"恢复麦克风":"麦克风静音";
    }

    internal async Task ApplyVolumeAsync(string id,int percent)
    {
        if (id!=selectedId || !endpoints.Any(e=>e.Id==id && !e.Capture)) return;
        if (changingVolume) { pendingVolumeId=id; pendingVolume=percent; volumeDelay.Start(); return; }
        changingVolume=true;
        generation++;
        settings.HeadsetOutputId=id; save();
        try
        {
            var result=await Task.Run(()=>backend.SetVolume(id,percent));
            if (result.Volume is int confirmed)
                endpoints=endpoints.Select(e=>e.Id==id && !e.Capture?e with { Volume=confirmed }:e).ToArray();
            if (id==selectedId)
            {
                StatusText.Text=result.Message;
                if (result.Volume is int actual && !volumeDelay.IsEnabled)
                {
                    loading=true;
                    try { VolumeSlider.Value=actual; VolumeText.Text=actual+"%"; }
                    finally { loading=false; }
                }
            }
        }
        catch { StatusText.Text="音量调节失败，请刷新后重试。"; }
        finally { changingVolume=false; }
    }

    internal async Task ToggleMicrophoneAsync()
    {
        if (changingMic || !MuteButton.IsEnabled) return;
        string id=(MicrophoneSelector.SelectedItem as MicChoice)?.Id;
        var mic=endpoints.FirstOrDefault(e=>e.Capture && e.Id==id);
        if (mic?.Muted==null) return;
        int request=++generation;
        settings.HeadsetOutputId=selectedId; save();
        changingMic=true; MuteButton.IsEnabled=false;
        try
        {
            var result=await Task.Run(()=>backend.SetMicrophoneMute(id,!mic.Muted.Value));
            if (request==generation) StatusText.Text=result.Message;
            if (result.Success && result.Muted.HasValue)
                endpoints=endpoints.Select(e=>e.Id==id?e with { Muted=result.Muted }:e).ToArray();
        }
        catch { StatusText.Text="麦克风操作失败，请刷新后重试。"; }
        finally { changingMic=false; UpdateMicState(); }
    }

    private void DisableControls() { volumeDelay.Stop(); OutputSelector.IsEnabled=false; VolumeSlider.IsEnabled=false; MuteButton.IsEnabled=false; MicrophoneSelector.IsEnabled=false; }
    internal void ApplyTheme(ThemePreset theme)
    {
        text.Color=theme.Text; dim.Color=theme.TextDim; accent.Color=theme.Accent;
        card.Color=Color.FromArgb(theme.IsLight?(byte)180:(byte)18,255,255,255);
        root.Background=new LinearGradientBrush(theme.BgTop,theme.BgBottom,90);
    }
    protected override void OnClosing(CancelEventArgs e) { poll.Stop(); volumeDelay.Stop(); generation++; e.Cancel=true; Hide(); base.OnClosing(e); }
    private sealed record MicChoice(string Id,string Name) { public override string ToString()=>Name; }
}
