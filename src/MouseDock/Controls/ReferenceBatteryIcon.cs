using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MouseDock.Models;
namespace MouseDock.Controls;
public sealed class ReferenceBatteryIcon : FrameworkElement
{
    public string StyleName="ring";
    public List<(DeviceKind Kind,int Level,bool Stale,bool Charging)> Devices=new();
    public Brush Accent=new SolidColorBrush(Color.FromRgb(82,125,108));
    public bool HideBackground;
    public Brush Ink = new SolidColorBrush(Color.FromRgb(30,43,42));
    protected override void OnRender(DrawingContext d)
    {
        base.OnRender(d);
        var bg=HideBackground?Brushes.Transparent:Brushes.White;
        var ink=Ink;
        var item=Devices.Count>0?Devices[0]:(DeviceKind.Mouse,-1,false,false);
        string value=item.Item2>=0?item.Item2.ToString():"—";
        double x=ActualWidth/2,y=ActualHeight/2;
        var p=new Pen(Accent,2);var track=new Pen(new SolidColorBrush(Color.FromRgb(215,226,222)),2);
        void Text(string text,double size,double cx,double cy,Brush brush)
        {
            var f=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            d.DrawText(f,new Point(cx-f.Width/2,cy-f.Height/2));
        }
        void Number(double size,double cx,double cy)
        {
            var digits=new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,ink,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double suffix=item.Item2>=0?size*.4:0;
            Text(value,size,cx-suffix/2,cy,ink);
            if(item.Item2>=0)Text("%",size*.42,cx+digits.Width/2+2,cy+size*.14,ink);
        }
        void Bar(double cx,double cy,double length)
        {d.DrawLine(track,new Point(cx-length/2,cy),new Point(cx+length/2,cy));if(item.Item2>=0)d.DrawLine(p,new Point(cx-length/2,cy),new Point(cx-length/2+length*Math.Clamp(item.Item2,0,100)/100,cy));}
        void Glyph(DeviceKind kind,double cx,double cy)
        {
            if(kind==DeviceKind.Keyboard){d.DrawRoundedRectangle(null,p,new Rect(cx-7,cy-4,14,8),1,1);d.DrawLine(p,new Point(cx-4,cy+1),new Point(cx+4,cy+1));}
            else if(kind==DeviceKind.Headset){var g=new StreamGeometry();using(var c=g.Open()){c.BeginFigure(new Point(cx-6,cy+4),false,false);c.ArcTo(new Point(cx+6,cy+4),new Size(6,8),0,true,SweepDirection.Clockwise,true,false);}d.DrawGeometry(null,p,g);}
            else {d.DrawRoundedRectangle(null,p,new Rect(cx-4,cy-6,8,12),4,4);d.DrawLine(p,new Point(cx,cy-4),new Point(cx,cy-1));}
        }
        switch(StyleName)
        {
            case "ring":
                d.DrawEllipse(bg,null,new Point(x,y),38,38);d.DrawEllipse(null,track,new Point(x,y),31,31);
                if(item.Item2>0){var g=new StreamGeometry();using(var c=g.Open()){double angle=Math.Min(359.99,item.Item2*3.6)*Math.PI/180;c.BeginFigure(new Point(x,y-31),false,false);c.ArcTo(new Point(x+31*Math.Sin(angle),y-31*Math.Cos(angle)),new Size(31,31),0,item.Item2>50,SweepDirection.Clockwise,true,false);}d.DrawGeometry(null,p,g);}
                Number(25,x-3,y);break;
            case "tile":d.DrawRoundedRectangle(bg,track,new Rect(x-37,y-37,74,74),19,19);Number(31,x-5,y-3);Bar(x,y+23,42);break;
            case "pill":d.DrawRoundedRectangle(bg,null,new Rect(x-49,y-21,98,42),21,21);d.DrawRoundedRectangle(Accent,null,new Rect(x-39,y-8,10,16),3,3);Number(26,x+2,y);break;
            case "battery":d.DrawRoundedRectangle(bg,track,new Rect(x-42,y-21,78,42),6,6);d.DrawRoundedRectangle(Accent,null,new Rect(x+39,y-6,3,12),1,1);if(item.Item2>=0)d.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(60,82,125,108)),null,new Rect(x-38,y-17,70*item.Item2/100,34),3,3);Number(28,x-7,y);break;
            case "plain":Number(34,x-5,y-3);Bar(x,y+22,50);break;
            case "multi":
                d.DrawRoundedRectangle(bg,track,new Rect(2,y-23,Math.Max(1,ActualWidth-4),46),10,10);
                if(Devices.Count==0)Text("—",20,x,y,ink);
                for(int i=0;i<Math.Min(6,Devices.Count);i++){var a=Devices[i];double cx=24+i*42;Glyph(a.Kind,cx,y-12);Text(a.Level<0?"—":a.Level.ToString(),16,cx,y+6,ink);if(a.Stale)Text("上次",8,cx,y+19,Accent);if(i>0)d.DrawLine(track,new Point(cx-21,y-15),new Point(cx-21,y+15));}break;
        }
        if(StyleName!="multi" && item.Item3)Text("上次",9,x,y+(ActualHeight<90?26:39),Accent);
        if(item.Item4)Text("ϟ",15,x+35,y-30,Accent);
    }
}
