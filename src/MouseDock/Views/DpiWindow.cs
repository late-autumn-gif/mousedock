using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

namespace MouseDock.Views;

public sealed class DpiWindow : Window
{
    private readonly DpiService service;
    private readonly DeviceInfo device;
    private readonly SolidColorBrush ink=new(),dim=new(),accent=new(),surface=new(),border=new(),selected=new(),background=new();
    private readonly TextBlock subtitle,note;
    private readonly Button refresh;
    private DpiSnapshot snapshot;
    private CancellationTokenSource operation;
    private bool busy,closed;
    private int generation;
    internal TextBlock CurrentText {get;}=new(){FontSize=28,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)};
    internal TextBlock StatusText {get;}=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,0)};
    internal ListBox StageSelector {get;}=new(){BorderThickness=new Thickness(0),Padding=new Thickness(0),Margin=new Thickness(0,8,0,0)};
    internal TextBox ValueEditor {get;}=new(){FontSize=22,MinWidth=120,Padding=new Thickness(8,5,8,5),VerticalContentAlignment=VerticalAlignment.Center};
    internal Button SaveButton {get;}
    internal Button SwitchButton {get;}

    public DpiWindow(DpiService service,DeviceInfo device,ThemePreset theme) : this(service,device,theme,true) { }
    internal DpiWindow(DpiService service,DeviceInfo device,ThemePreset theme,bool autoRead)
    {
        this.service=service;this.device=device;
        Title=$"MouseDock · {device.Name} DPI";Width=540;Height=535;MinWidth=410;MinHeight=430;
        MaxHeight=Math.Max(430,SystemParameters.WorkArea.Height-40);WindowStartupLocation=WindowStartupLocation.CenterScreen;
        FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");ShowInTaskbar=true;
        Resources["DpiInk"]=ink;Resources["DpiSurface"]=surface;Resources["DpiBorder"]=border;
        Resources["DpiAccent"]=accent;Resources["DpiSelected"]=selected;
        Background=background;Foreground=ink;
        var body=new StackPanel{Margin=new Thickness(22)};
        Content=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var heading=new Grid();heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var name=Label(device.Name,18,true);name.Margin=new Thickness(0,0,10,0);heading.Children.Add(name);
        refresh=Command("\uE72C","重新读取");refresh.ToolTip="重新读取 DPI";refresh.Click+=async(_,__)=>await RefreshAsync();
        Grid.SetColumn(refresh,1);heading.Children.Add(refresh);body.Children.Add(heading);
        body.Children.Add(Label("DPI",12));
        var current=new StackPanel{Margin=new Thickness(0,18,0,18)};
        current.Children.Add(Label("当前 DPI",12));CurrentText.Foreground=ink;current.Children.Add(CurrentText);
        subtitle=Label("",12);subtitle.Margin=new Thickness(0,6,0,0);current.Children.Add(subtitle);body.Children.Add(current);
        var rule=new Border{Height=1,Background=border,Margin=new Thickness(0,0,0,16)};body.Children.Add(rule);
        body.Children.Add(Label("档位",13,true));body.Children.Add(StageSelector);
        StageSelector.Background=background;StageSelector.Foreground=ink;
        var panelFactory=new FrameworkElementFactory(typeof(UniformGrid));panelFactory.SetValue(UniformGrid.ColumnsProperty,4);
        StageSelector.ItemsPanel=new ItemsPanelTemplate(panelFactory);
        var rowFactory=new FrameworkElementFactory(typeof(TextBlock));
        rowFactory.SetBinding(TextBlock.TextProperty,new Binding(nameof(StageChoice.Label)));
        rowFactory.SetValue(TextBlock.TextAlignmentProperty,TextAlignment.Center);rowFactory.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        rowFactory.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
        StageSelector.ItemTemplate=new DataTemplate{VisualTree=rowFactory};
        var itemStyle=new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("DpiSurface")));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("DpiInk")));
        itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension("DpiBorder")));
        itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,5,5)));
        itemStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty,64d));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));
        var selectedTrigger=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("DpiSelected")));
        selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension("DpiAccent")));itemStyle.Triggers.Add(selectedTrigger);
        StageSelector.ItemContainerStyle=itemStyle;
        StageSelector.SelectionChanged+=(_,__)=>SelectStage();
        var input=new Grid{Margin=new Thickness(0,14,0,0)};
        input.ColumnDefinitions.Add(new(){Width=new GridLength(150)});input.ColumnDefinitions.Add(new());
        ValueEditor.Foreground=ink;ValueEditor.Background=surface;ValueEditor.BorderBrush=border;
        ValueEditor.PreviewTextInput+=(_,e)=>e.Handled=e.Text.Any(c=>c<'0'||c>'9');
        ValueEditor.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await SubmitAsync(false);}};
        input.Children.Add(ValueEditor);
        var units=Label("DPI  ·  X / Y",12);units.Margin=new Thickness(12,0,0,0);units.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(units,1);input.Children.Add(units);body.Children.Add(input);
        note=Label("",11);note.Margin=new Thickness(0,8,0,0);body.Children.Add(note);
        var commands=new Grid{Margin=new Thickness(0,16,0,0)};
        commands.ColumnDefinitions.Add(new());commands.ColumnDefinitions.Add(new());
        SaveButton=Command("\uE74E","保存此档数值");SaveButton.Margin=new Thickness(0,0,5,0);
        SwitchButton=Command("\uE768","切换到此档");SwitchButton.Margin=new Thickness(5,0,0,0);
        Grid.SetColumn(SwitchButton,1);commands.Children.Add(SaveButton);commands.Children.Add(SwitchButton);body.Children.Add(commands);
        SaveButton.Click+=async(_,__)=>await SubmitAsync(false);SwitchButton.Click+=async(_,__)=>await SubmitAsync(true);
        StatusText.Foreground=dim;body.Children.Add(StatusText);
        System.Windows.Automation.AutomationProperties.SetName(StageSelector,"DPI 档位");
        System.Windows.Automation.AutomationProperties.SetName(ValueEditor,"所选档位 DPI");
        ApplyTheme(theme);RenderSnapshot(null);
        device.PropertyChanged+=DeviceChanged;
        Closed+=(_,__)=>
        {
            closed=true;generation++;operation?.Cancel();service.Invalidate(device.Id);device.PropertyChanged-=DeviceChanged;
        };
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
        if(autoRead)Loaded+=async(_,__)=>await RefreshAsync();
    }
    private sealed record StageChoice(int Index,string Label);
    private TextBlock Label(string value,double size,bool bold=false)=>new()
    {Text=value,FontSize=size,Foreground=size<=12?dim:ink,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};
    private Button Command(string icon,string label)
    {
        var content=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
        content.Children.Add(new TextBlock{Text=icon,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=13,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)});
        content.Children.Add(new TextBlock{Text=label,FontSize=12,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center});
        return new Button{Content=content,Padding=new Thickness(10,9,10,9),MinHeight=36,Foreground=ink,Background=surface,BorderBrush=border,
            BorderThickness=new Thickness(1),Cursor=Cursors.Hand,ToolTip=label};
    }
    public void ApplyTheme(ThemePreset theme)
    {
        Tint(ink,theme.Text);Tint(dim,theme.TextDim);Tint(accent,theme.Accent);Tint(background,theme.BgTop);
        Tint(surface,theme.Surface==default?(theme.IsLight?Colors.White:Color.FromRgb(37,39,43)):theme.Surface);
        Tint(border,theme.Border==default?(theme.IsLight?Color.FromRgb(210,213,218):Color.FromRgb(76,79,85)):theme.Border);
        Tint(selected,Color.FromArgb(theme.IsLight?(byte)45:(byte)65,theme.Accent.R,theme.Accent.G,theme.Accent.B));
    }
    // A binding keeps shared brushes mutable when WPF seals a style or renders the window.
    private static void Tint(SolidColorBrush brush,Color color)=>
        BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new Binding{Source=color});
    private void DeviceChanged(object sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is not (nameof(DeviceInfo.Connected) or nameof(DeviceInfo.RecognitionEnabled)))return;
        if(device.Connected&&device.RecognitionEnabled)return;
        Dispatcher.InvokeAsync(()=>
        {
            generation++;operation?.Cancel();service.Invalidate(device.Id);busy=false;RenderSnapshot(null);
            StatusText.Text="设备已断开或停止识别";
        });
    }
    internal void RenderSnapshot(DpiSnapshot state,int? selectedStage=null)
    {
        snapshot=state;
        CurrentText.Text=state?.Current.Label??"DPI 未确认";
        bool currentOnly=state?.Family==DpiFamily.MchoseReadOnly;
        subtitle.Text=state==null?"":currentOnly?$"扫描于 {state.CapturedAt.LocalDateTime:HH:mm:ss} · 只读":state.Context.StartsWith("live:")?state.Note:$"当前第 {state.Active+1} 档 · {state.Stages.Count} 档"+(state.Writable?"":" · 只读");
        note.Text=state?.Note??"";
        StageSelector.ItemsSource=currentOnly?null:state?.Stages.Select((row,index)=>new StageChoice(index,
            $"第 {index+1} 档{(index==state.Active?" · 使用中":"")}\n{(row.X==row.Y?row.X.ToString():row.X+"/"+row.Y)}")).ToArray();
        StageSelector.SelectedIndex=state==null||currentOnly?-1:selectedStage.HasValue&&selectedStage.Value>=0&&selectedStage.Value<state.Stages.Count?selectedStage.Value:state.Active;
        SelectStage();UpdateEnabled();
    }
    private void SelectStage()
    {
        int index=StageSelector.SelectedIndex;
        ValueEditor.Text=snapshot!=null&&index>=0&&index<snapshot.Stages.Count?snapshot.Stages[index].X.ToString(CultureInfo.InvariantCulture):"";
    }
    private void UpdateEnabled()
    {
        refresh.IsEnabled=!busy&&device.Connected&&device.RecognitionEnabled;
        StageSelector.IsEnabled=!busy&&snapshot!=null;
        ValueEditor.IsEnabled=SaveButton.IsEnabled=SwitchButton.IsEnabled=!busy&&snapshot?.Writable==true;
    }
    internal Task RefreshAsync()=>ExecuteAsync(false,false);
    internal Task SubmitAsync(bool switching)=>ExecuteAsync(true,switching);
    private async Task ExecuteAsync(bool writing,bool switching)
    {
        if(busy||closed||writing&&snapshot?.Writable!=true)return;
        int stage=StageSelector.SelectedIndex;int? value=null;
        if(writing)
        {
            if(!switching)
            {
                if(!int.TryParse(ValueEditor.Text,NumberStyles.None,CultureInfo.InvariantCulture,out int parsed))
                {StatusText.Text="请输入整数 DPI";return;}value=parsed;
            }
            try{snapshot.Target(stage,value);}catch(Exception e){StatusText.Text=e.Message;return;}
        }
        busy=true;UpdateEnabled();StatusText.Text=writing?"正在保存并回读确认…":"正在读取…";
        operation?.Dispose();operation=new CancellationTokenSource();int request=++generation;
        try
        {
            var work=writing?service.ApplyAsync(snapshot,stage,value,operation.Token):service.ReadAsync(device,operation.Token);
            var state=await work.ConfigureAwait(false);
            await Dispatcher.InvokeAsync(()=>
            {
                if(closed||request!=generation)return;
                RenderSnapshot(state,writing?stage:null);
                StatusText.Text=writing?"已保存 · 回读确认成功":state.Family==DpiFamily.MchoseReadOnly?"已显示最近扫描的只读读数":"已从设备读取";
            });
        }
        catch(Exception e)
        {
            await Dispatcher.InvokeAsync(()=>
            {
                if(closed||request!=generation)return;
                RenderSnapshot(null);StatusText.Text=e.Message;
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(()=>
            {if(!closed&&request==generation){busy=false;UpdateEnabled();}});
        }
    }
}
