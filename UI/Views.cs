using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MEP_Radial_Routing_ver1.Core;
using MEP_Radial_Routing_ver1.Revit;

namespace MEP_Radial_Routing_ver1.UI
{
    public sealed class Option
    {
        public int Id; public string Name; public override string ToString()=>Name;
    }
    public sealed class SettingsWindow:Window
    {
        public readonly ComboBox Systems=new ComboBox(),Types=new ComboBox(),Levels=new ComboBox(),Connectors=new ComboBox(),Frame=new ComboBox();
        public readonly TextBox Diameter=new TextBox {Text="100"},Length=new TextBox {Text="1000"},Angle=new TextBox {Text="0"};
        readonly TextBlock status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,4)};
        readonly StackPanel setup=new StackPanel();
        public Action SelectSource,Start,Finish,Cancel,ApplyLength,ApplyAngle;
        public SettingsWindow()
        {
            Title="MEP Radial Routing v1";Width=355;Height=730;ResizeMode=ResizeMode.CanMinimize;
            Background=new SolidColorBrush(Color.FromRgb(247,247,250));
            var panel=new StackPanel {Margin=new Thickness(18)};Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            panel.Children.Add(new TextBlock {Text="PIPE / RADIAL ROUTING",FontSize=20,FontWeight=FontWeights.Bold,Foreground=Brushes.Firebrick});
            panel.Children.Add(setup);
            Button(setup,"1  Select source element",()=>SelectSource?.Invoke());
            Add(setup,"Open piping connector (highlighted in 3D)",Connectors);
            Add(setup,"Piping System Type",Systems);Add(setup,"Pipe Type",Types);Add(setup,"Level",Levels);Add(setup,"Diameter (mm)",Diameter);
            Frame.Items.Add("Project / XY · XZ · YZ");Frame.Items.Add("Connector / UV · UW · VW");Frame.SelectedIndex=0;Add(setup,"Reference Frame",Frame);
            setup.Children.Add(new TextBlock {Text="Angle presets: 0°, 45°, 90°, … 315°",Margin=new Thickness(0,10,0,0)});
            Button(setup,"2  Start Routing",()=>Start?.Invoke());
            Add(panel,"Exact length (mm)",Length);Button(panel,"Apply length",()=>ApplyLength?.Invoke());
            Add(panel,"Custom angle (degrees, active plane)",Angle);Button(panel,"Lock custom angle",()=>ApplyAngle?.Invoke());
            Button(panel,"Finish committed segments",()=>Finish?.Invoke());Button(panel,"Cancel",()=>Cancel?.Invoke());
            panel.Children.Add(status);
            panel.Children.Add(new TextBlock {Text="Hold Ctrl: direction wheel · Wheel: plane\nClick / Enter: commit · Backspace: undo\nEsc: discard preview · Finish: placeholders only",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,8,0,0)});
        }
        static void Add(StackPanel p,string label,Control c) { p.Children.Add(new TextBlock {Text=label,Margin=new Thickness(0,9,0,3)});c.MinHeight=25;p.Children.Add(c); }
        static void Button(StackPanel p,string label,Action action) { var b=new Button {Content=label,Margin=new Thickness(0,8,0,0),Padding=new Thickness(8,5,8,5)}; b.Click+=(s,e)=>action();p.Children.Add(b); }
        public void Status(string message,bool error=false) { status.Text=message;status.Foreground=error?Brushes.Firebrick:Brushes.DarkSlateGray; }
        public void FreezeSetup()=>setup.IsEnabled=false;
        public bool TrySettings(out RoutingSettings settings)
        {
            settings=null;double diameter;
            if(Systems.SelectedItem==null || Types.SelectedItem==null || Levels.SelectedItem==null || !Number(Diameter.Text,out diameter) || diameter<=0) {Status("Select system, pipe type, level and a positive diameter.",true);return false;}
            settings=new RoutingSettings {SystemId=((Option)Systems.SelectedItem).Id,PipeTypeId=((Option)Types.SelectedItem).Id,LevelId=((Option)Levels.SelectedItem).Id,DiameterMm=diameter};return true;
        }
        public static bool Number(string text,out double value)=>double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out value) && !double.IsNaN(value) && !double.IsInfinity(value);
    }
    public sealed class RadialMenuView:FrameworkElement
    {
        public int Selected=-1;public string Plane="XY";
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);var center=new Point(160,160);
            for(int i=0;i<8;i++)
            {
                double a=(i*45-22.5)*Math.PI/180,b=(i*45+22.5)*Math.PI/180;
                var g=new StreamGeometry();using(var c=g.Open())
                {
                    c.BeginFigure(P(42,a),true,true);c.LineTo(P(149,a),true,false);c.ArcTo(P(149,b),new Size(149,149),0,false,SweepDirection.Counterclockwise,true,false);
                    c.LineTo(P(42,b),true,false);c.ArcTo(P(42,a),new Size(42,42),0,false,SweepDirection.Clockwise,true,false);
                }
                dc.DrawGeometry(i==Selected?Brushes.Firebrick:new SolidColorBrush(Color.FromArgb(240,35,40,48)),new Pen(Brushes.Gray,1),g);
                double r=i*Math.PI/4;var p=P(102,r);Text(dc,i*45+"°",p.X,p.Y,16);
                Vec dir=new Vec(Math.Cos(r),-Math.Sin(r),0);Point tip=P(135,r),tail=P(119,r);
                dc.DrawLine(new Pen(Brushes.White,2),tail,tip);
                dc.DrawLine(new Pen(Brushes.White,2),tip,new Point(tip.X-7*dir.X-4*dir.Y,tip.Y-7*dir.Y+4*dir.X));
                dc.DrawLine(new Pen(Brushes.White,2),tip,new Point(tip.X-7*dir.X+4*dir.Y,tip.Y-7*dir.Y-4*dir.X));
            }
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(20,23,29)),new Pen(Brushes.IndianRed,2),center,40,40);
            Text(dc,Plane,160,144,13);Text(dc,Selected<0?"Keep":Selected*45+"°",160,164,14);Text(dc,"Ctrl ↓",160,181,10);
        }
        static Point P(double radius,double a)=>new Point(160+radius*Math.Cos(a),160-radius*Math.Sin(a));
        void Text(DrawingContext dc,string text,double x,double y,double size)
        {
            var f=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(f,new Point(x-f.Width/2,y-f.Height/2));
        }
    }
    public sealed class RadialWindow:Window
    {
        public readonly RadialMenuView Menu=new RadialMenuView();
        public double CenterX,CenterY,ScaleX=1,ScaleY=1;
        public RadialWindow(IntPtr owner)
        {
            Width=Height=320;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;IsHitTestVisible=false;ResizeMode=ResizeMode.NoResize;Content=Menu;
            new WindowInteropHelper(this).Owner=owner;
            SourceInitialized+=(s,e)=> { var h=new WindowInteropHelper(this).Handle;SetWindowLong(h,-20,GetWindowLong(h,-20)|0x08000000|0x20|0x80); };
        }
        public void Open(double x,double y)
        {
            Show();
            // Move to the cursor's monitor before reading WPF's effective DPI transform.
            SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,(int)x,(int)y,0,0,0x15);
            var source=PresentationSource.FromVisual(this);var t=source.CompositionTarget.TransformToDevice;ScaleX=t.M11;ScaleY=t.M22;
            CenterX=x;CenterY=y;Menu.Selected=-1;
            SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,(int)Math.Round(x-160*ScaleX),(int)Math.Round(y-160*ScaleY),(int)Math.Round(320*ScaleX),(int)Math.Round(320*ScaleY),0x14);
            Menu.InvalidateVisual();
        }
        public int Hit(double x,double y)=>RadialHitTest.Hit((x-CenterX)/ScaleX,(y-CenterY)/ScaleY,Menu.Selected);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int index,int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
    }
}
