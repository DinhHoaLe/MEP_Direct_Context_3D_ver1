using System;
using System.Reflection;
using System.Runtime.InteropServices;
using MEP_Radial_Routing_ver1.UI;

static class InputControllerTests
{
    [StructLayout(LayoutKind.Sequential)] struct KeyData {public uint Key,Scan,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Sequential)] struct MouseData {public InputController.Point Point;public uint Data,Flags,Time;public UIntPtr Extra;}
    static int count;
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);count++;}
    static IntPtr Invoke(InputController input,string method,int message,object data)
    {
        var memory=Marshal.AllocHGlobal(Marshal.SizeOf(data));
        try {Marshal.StructureToPtr(data,memory,false);return (IntPtr)typeof(InputController).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(input,new object[]{0,(IntPtr)message,memory});}
        finally {Marshal.FreeHGlobal(memory);}
    }
    static void Drain(InputController input)=>typeof(InputController).GetMethod("Drain",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(input,null);
    public static void Run()
    {
        bool canCommit=true,enabled=true,foreground=true;int clicks=0,keys=0,moves=0;double movedX=0;
        using(var input=new InputController(IntPtr.Zero,()=>foreground))
        {
            input.Enabled=()=>enabled;input.InViewport=(x,y)=>true;input.CanCommit=()=>canCommit;
            input.Click=()=>{clicks++;canCommit=false;};input.Key=(key,x,y)=>keys++;
            input.Move=(x,y)=>{moves++;movedX=x;};
            var mouse=new MouseData {Point=new InputController.Point {X=100,Y=200}};
            Check(Invoke(input,"Mouse",0x201,mouse)==(IntPtr)1,"Consume routing press");
            Check(clicks==0,"Hook must not commit synchronously");Drain(input);
            Check(clicks==1,"Dispatch commits once");
            Check(Invoke(input,"Mouse",0x202,mouse)==(IntPtr)1,"Consume release after commit changed state");
            Invoke(input,"Keyboard",0x100,new KeyData {Key=0xA2});
            Check(keys==0,"Ctrl hook must not open WPF synchronously");Drain(input);Check(keys==1,"Dispatch Ctrl press");
            Invoke(input,"Keyboard",0x101,new KeyData {Key=0xA2});Drain(input);Check(keys==2,"Dispatch Ctrl release");
            moves=0;
            for(int i=0;i<100;i++) {mouse.Point.X=i;Invoke(input,"Mouse",0x200,mouse);}
            Drain(input);Check(moves==1&&movedX==99,"Coalesce mouse moves to latest point");
            Invoke(input,"Keyboard",0x100,new KeyData {Key=0xA2});foreground=false;Drain(input);Check(keys==2,"Discard queued input after focus loss");
            foreground=true;Invoke(input,"Keyboard",0x100,new KeyData {Key=0xA2});enabled=false;Drain(input);Check(keys==2,"Discard queued input when session disabled");
            enabled=true;input.CaptureRoutingKeys=false;
            Invoke(input,"Keyboard",0x100,new KeyData {Key=13});Drain(input);Check(keys==2,"Passive connector watcher leaves Enter to Revit");
            input.CaptureRoutingKeys=true;input.Key=(key,x,y)=>{keys++;enabled=false;};
            Invoke(input,"Keyboard",0x100,new KeyData {Key=13});Invoke(input,"Keyboard",0x100,new KeyData {Key=13});Drain(input);
            Check(keys==3,"Pending document request stops queued repeated commits");
            Check(((System.Collections.ICollection)typeof(InputController).GetField("events",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(input)).Count==0,"Discard input queued before document request");
            enabled=true;input.Key=(key,x,y)=>keys++;
            Invoke(input,"Keyboard",0x100,new KeyData {Key=0x11});Drain(input);
            Invoke(input,"Keyboard",0x101,new KeyData {Key=0x11});Drain(input);Check(keys==5,"Generic Ctrl press and release are supported");
            Invoke(input,"Keyboard",0x100,new KeyData {Key=0xA2});Drain(input);
            Check(Invoke(input,"Keyboard",0x100,new KeyData {Key=9})==(IntPtr)1,"Tab is captured while the Ctrl menu is open");Drain(input);
            Invoke(input,"Keyboard",0x101,new KeyData {Key=0xA2});Drain(input);Check(keys==8,"Ctrl-Tab system change is dispatched without a setup window");
            int observed=0;input.CaptureRoutingKeys=false;input.ObserveKey=(key,down)=>{if(key==27&&down)observed++;};
            Invoke(input,"Keyboard",0x100,new KeyData {Key=27});Check(observed==0,"Native tool observation is deferred outside the hook");Drain(input);
            Check(observed==1&&keys==8,"Escape is observed without becoming a radial routing action");
            input.ObserveKey=null;foreground=false;Check(!input.TryEndNativeCommand(),"Never post native cancellation when Revit is not foreground");
            foreground=true;Check(!input.TryEndNativeCommand(),"Never post cancellation to an invalid window handle");
        }
        Console.WriteLine("PASS: "+count+" input assertions (deferred callbacks, paired clicks, coalescing, focus)");
    }
}
