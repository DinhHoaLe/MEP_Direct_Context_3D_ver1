using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MEP_Radial_Routing_ver1.Core;

namespace MEP_Radial_Routing_ver1.UI
{
    public sealed class RadialMenuView:FrameworkElement
    {
        public int Selected=-1;public double TiltDegrees=45;
        public readonly double[] Angles=new double[8];
        public string Notice;
        public string SystemLabel;
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
                double r=i*Math.PI/4;var p=P(102,r);Text(dc,"R "+Angles[i].ToString("0")+"°",p.X,p.Y,14);
                Vec dir=new Vec(Math.Cos(r),-Math.Sin(r),0);Point tip=P(135,r),tail=P(119,r);
                dc.DrawLine(new Pen(Brushes.White,2),tail,tip);
                dc.DrawLine(new Pen(Brushes.White,2),tip,new Point(tip.X-7*dir.X-4*dir.Y,tip.Y-7*dir.Y+4*dir.X));
                dc.DrawLine(new Pen(Brushes.White,2),tip,new Point(tip.X-7*dir.X+4*dir.Y,tip.Y-7*dir.Y-4*dir.X));
            }
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(20,23,29)),new Pen(Brushes.IndianRed,2),center,40,40);
            Text(dc,"AXIS 3D",160,143,11);Text(dc,Selected<0?(Notice??"Choose"):"R "+Angles[Selected].ToString("0")+"°",160,161,12);Text(dc,"T "+TiltDegrees.ToString("0.#")+"°",160,179,12);
            if(!string.IsNullOrEmpty(SystemLabel))
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(245,35,40,48)),null,new Rect(12,297,296,22),4,4);
                var label=SystemLabel.Length>42?SystemLabel.Substring(0,39)+"...":SystemLabel;
                Text(dc,label,160,308,11);
            }
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
