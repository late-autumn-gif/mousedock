using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MouseDock.Services;
namespace MouseDock.Views;
internal sealed class UpdateAvailableWindow : Window
{
    internal bool DownloadRequested { get; private set; }
    internal Button DownloadNow { get; }
    internal Button Later { get; }
    internal UpdateAvailableWindow(UpdateService.UpdateInfo info)
    {
        Title="MouseDock 更新";Width=430;SizeToContent=SizeToContent.Height;
        ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=new SolidColorBrush(App.Theme.BgTop);Foreground=new SolidColorBrush(App.Theme.Text);
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock{Text="发现新版本 v"+info.Version,FontSize=20,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="当前版本 v"+UpdateService.CurrentVersionText+"\n\n下载后将自动解压、替换程序文件并重启。个人配置保留。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,10)});
        panel.Children.Add(new TextBlock{Text="GitHub直连优先，失败后尝试GH-Proxy镜像。",TextWrapping=TextWrapping.Wrap,FontSize=12,Opacity=.8});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        Later=new Button{Content="稍后",Padding=new Thickness(18,8,18,8),Margin=new Thickness(0,0,10,0),IsCancel=true};
        DownloadNow=new Button{Content="立即下载",Padding=new Thickness(18,8,18,8),IsDefault=true};
        Later.Click+=(_,__)=>Close();
        DownloadNow.Click+=(_,__)=>{DownloadRequested=true;Close();};
        buttons.Children.Add(Later);buttons.Children.Add(DownloadNow);panel.Children.Add(buttons);Content=panel;
    }
}
