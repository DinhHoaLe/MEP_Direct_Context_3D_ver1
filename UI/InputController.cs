using System;
using System.Diagnostics;
using System.Collections.Generic;
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
        readonly DispatcherTimer dispatchTimer;
        readonly Queue<Action> events=new Queue<Action>();
        readonly Func<bool> isForeground;
        public Func<bool> Enabled;
        public Func<double,double,bool> InViewport;
        public Func<bool> CanCommit;
        public bool CaptureRoutingKeys=true;
        public Action<int,double,double> Key;
        public Action<double,double> Move;
        public Action<int> Wheel;
        public Action<int,bool> ObserveKey;
        public Action Click,FocusLost;
        public Action<Exception> Error;
        bool ctrl, swallowLeftUp, disposed;
        Point lastMove; bool moved;
        public InputController(IntPtr handle,Func<bool> foreground=null)
        {
            revit=handle;keyboard=SafeKeyboard;mouse=SafeMouse;
            isForeground=foreground??(()=>GetForegroundWindow()==revit);
            dispatchTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(16)};
            dispatchTimer.Tick+=(s,e)=>Drain();
            focusTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
            focusTimer.Tick+=(s,e)=>{ if(!Focused && ctrl) { ctrl=false; FocusLost?.Invoke(); } }; 
        }
        public bool Focused=>isForeground();
        public void Start()
        {
            if(kh!=IntPtr.Zero || mh!=IntPtr.Zero)return;
            disposed=false;
            var module=GetModuleHandle(null); kh=SetWindowsHookEx(13,keyboard,module,0);mh=SetWindowsHookEx(14,mouse,module,0);
            if(kh==IntPtr.Zero || mh==IntPtr.Zero) { Dispose();throw new System.ComponentModel.Win32Exception(); } focusTimer.Start();dispatchTimer.Start();
        }
        // A low-level hook must return promptly. WPF rendering and route changes run later.
        void Drain()
        {
            if(disposed)return;
            try
            {
                if(!Focused || !Enabled()) {events.Clear();moved=false;ctrl=false;FocusLost?.Invoke();return;}
                while(events.Count>0 && !disposed && Enabled())events.Dequeue()();
                if(!disposed && !Enabled()) {events.Clear();moved=false;return;}
                if(moved && !disposed) {moved=false;Move?.Invoke(lastMove.X,lastMove.Y);}
            }
            catch(Exception ex) {events.Clear();Error?.Invoke(ex);}
        }
        void QueueKey(int key,Point p) {moved=false;events.Enqueue(()=>Key?.Invoke(key,p.X,p.Y));}
        IntPtr SafeKeyboard(int n,IntPtr w,IntPtr l) { try {return Keyboard(n,w,l);} catch(Exception ex) {events.Enqueue(()=>Error?.Invoke(ex));return CallNextHookEx(IntPtr.Zero,n,w,l);} }
        IntPtr SafeMouse(int n,IntPtr w,IntPtr l) { try {return Mouse(n,w,l);} catch(Exception ex) {events.Enqueue(()=>Error?.Invoke(ex));return CallNextHookEx(IntPtr.Zero,n,w,l);} }
        IntPtr Keyboard(int n,IntPtr w,IntPtr l)
        {
            if(n>=0 && Focused && Enabled())
            {
                var d=(KeyData)Marshal.PtrToStructure(l,typeof(KeyData)); int key=(int)d.Key,msg=w.ToInt32(); bool down=msg==0x100 || msg==0x104;
                if(ObserveKey!=null)events.Enqueue(()=>ObserveKey?.Invoke(key,down));
                Point p;GetCursorPos(out p);
                if(key==0x11 || key==0xA2 || key==0xA3)
                {
                    if(down && !ctrl && InViewport(p.X,p.Y)) { ctrl=true;QueueKey(17,p);return (IntPtr)1; }
                    if(!down && ctrl) { ctrl=false;QueueKey(-17,p);return (IntPtr)1; }
                    if(ctrl)return (IntPtr)1;
                }
                if(CaptureRoutingKeys && down && ((key==9 && ctrl) || ((key==27 || key==8 || key==13) && (ctrl || InViewport(p.X,p.Y))))) { QueueKey(key,p);return (IntPtr)1; }
            }
            return CallNextHookEx(IntPtr.Zero,n,w,l);
        }
        IntPtr Mouse(int n,IntPtr w,IntPtr l)
        {
            // Pair the release with the consumed press even after Commit changes session state.
            if(n>=0 && w.ToInt32()==0x202 && swallowLeftUp) {swallowLeftUp=false;return Focused?(IntPtr)1:CallNextHookEx(IntPtr.Zero,n,w,l);}
            if(n>=0 && Focused && Enabled())
            {
                var d=(MouseData)Marshal.PtrToStructure(l,typeof(MouseData));int msg=w.ToInt32();
                if(msg==0x200) {lastMove=d.Point;moved=true;}
                if(msg==0x20A && ctrl) {int delta=(short)(d.Data>>16)>0?1:-1;events.Enqueue(()=>Wheel?.Invoke(delta));return (IntPtr)1; }
                if(msg==0x201 && InViewport(d.Point.X,d.Point.Y) && (ctrl || CanCommit()))
                {
                    swallowLeftUp=true;
                    if(!ctrl) {var point=d.Point;moved=false;events.Enqueue(()=>{Move?.Invoke(point.X,point.Y);Click?.Invoke();});}
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(IntPtr.Zero,n,w,l);
        }
        public bool ControlHeld=>(GetAsyncKeyState(0x11)&0x8000)!=0;
        // A Ctrl handoff ends only the foreground Revit tool. This sends messages
        // to that HWND, never system-wide input, and never moves focus or the cursor.
        public bool TryEndNativeCommand()
        {
            if(!Focused || revit==IntPtr.Zero)return false;
            return PostMessage(revit,0x100,(IntPtr)27,(IntPtr)1)
                && PostMessage(revit,0x101,(IntPtr)27,new IntPtr(unchecked((int)0xC0000001)))
                && PostMessage(revit,0x100,(IntPtr)27,(IntPtr)1)
                && PostMessage(revit,0x101,(IntPtr)27,new IntPtr(unchecked((int)0xC0000001)));
        }
        public void AdoptControlPress() {ctrl=ControlHeld;}
        public static Point Cursor {get { Point p;GetCursorPos(out p);return p; }}
        public void Dispose() { disposed=true;focusTimer.Stop();dispatchTimer.Stop();events.Clear();moved=false;swallowLeftUp=false;if(kh!=IntPtr.Zero)UnhookWindowsHookEx(kh);if(mh!=IntPtr.Zero)UnhookWindowsHookEx(mh);kh=mh=IntPtr.Zero;ctrl=false; }
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id,Hook proc,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int n,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
        [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
    }
}

