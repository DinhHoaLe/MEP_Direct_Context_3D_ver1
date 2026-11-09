using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using MEP_Radial_Routing_ver1.Core;

namespace MEP_Radial_Routing_ver1.UI
{
    // Hooks only forward primitive input. No document/view/connector API access here.
    public sealed class InputController:IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] struct MouseData { public Point Point; public uint Data,Flags,Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct KeyData { public uint Key,Scan,Flags,Time; public UIntPtr Extra; }
        delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
        readonly Hook keyboard,mouse; IntPtr kh,mh; readonly IntPtr revit;
        readonly DispatcherTimer focusTimer;
        public Func<bool> Enabled;
        public Func<double,double,bool> InViewport;
        public Func<bool> CanCommit;
        public Action<int,double,double> Key;
        public Action<double,double> Move;
        public Action<int> Wheel;
        public Action Click,FocusLost;
        public Action<Exception> Error;
        bool ctrl;
        public InputController(IntPtr handle)
        {
            revit=handle;keyboard=SafeKeyboard;mouse=SafeMouse;
            focusTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
            focusTimer.Tick+=(s,e)=>{ if(!Focused && ctrl) { ctrl=false; FocusLost?.Invoke(); } }; 
        }
        public bool Focused=>GetForegroundWindow()==revit;
        public void Start()
        {
            var module=GetModuleHandle(null); kh=SetWindowsHookEx(13,keyboard,module,0);mh=SetWindowsHookEx(14,mouse,module,0);
            if(kh==IntPtr.Zero || mh==IntPtr.Zero) { Dispose();throw new System.ComponentModel.Win32Exception(); } focusTimer.Start();
        }
        IntPtr SafeKeyboard(int n,IntPtr w,IntPtr l) { try {return Keyboard(n,w,l);} catch(Exception ex) {Error?.Invoke(ex);return CallNextHookEx(IntPtr.Zero,n,w,l);} }
        IntPtr SafeMouse(int n,IntPtr w,IntPtr l) { try {return Mouse(n,w,l);} catch(Exception ex) {Error?.Invoke(ex);return CallNextHookEx(IntPtr.Zero,n,w,l);} }
        IntPtr Keyboard(int n,IntPtr w,IntPtr l)
        {
            if(n>=0 && Focused && Enabled())
            {
                var d=(KeyData)Marshal.PtrToStructure(l,typeof(KeyData)); int key=(int)d.Key,msg=w.ToInt32(); bool down=msg==0x100 || msg==0x104;
                Point p;GetCursorPos(out p);
                if(key==0xA2 || key==0xA3)
                {
                    if(down && !ctrl && InViewport(p.X,p.Y)) { ctrl=true;Key?.Invoke(17,p.X,p.Y);return (IntPtr)1; }
                    if(!down && ctrl) { ctrl=false;Key?.Invoke(-17,p.X,p.Y);return (IntPtr)1; }
                    if(ctrl)return (IntPtr)1;
                }
                if(down && (key==27 || key==8 || key==13) && (ctrl || InViewport(p.X,p.Y))) { Key?.Invoke(key,p.X,p.Y);return (IntPtr)1; }
            }
            return CallNextHookEx(IntPtr.Zero,n,w,l);
        }
        IntPtr Mouse(int n,IntPtr w,IntPtr l)
        {
            if(n>=0 && Focused && Enabled())
            {
                var d=(MouseData)Marshal.PtrToStructure(l,typeof(MouseData));int msg=w.ToInt32();
                if(msg==0x200)Move?.Invoke(d.Point.X,d.Point.Y);
                if(msg==0x20A && ctrl) { Wheel?.Invoke((short)(d.Data>>16)>0?1:-1);return (IntPtr)1; }
                if((msg==0x201 || msg==0x202) && InViewport(d.Point.X,d.Point.Y) && (ctrl || CanCommit())) { if(msg==0x201 && !ctrl)Click?.Invoke();return (IntPtr)1; }
            }
            return CallNextHookEx(IntPtr.Zero,n,w,l);
        }
        public void ActivateRevit()=>SetForegroundWindow(revit);
        public static Point Cursor {get { Point p;GetCursorPos(out p);return p; }}
        public void Dispose() { focusTimer.Stop(); if(kh!=IntPtr.Zero)UnhookWindowsHookEx(kh);if(mh!=IntPtr.Zero)UnhookWindowsHookEx(mh);kh=mh=IntPtr.Zero;ctrl=false; }
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id,Hook proc,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int n,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
        [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
    }
}

