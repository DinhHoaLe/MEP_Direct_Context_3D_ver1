using System;
using MEP_Radial_Routing_ver1.Core;

static class GeometryTests
{
    static int count;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name);count++; }
    static void Near(Vec a,Vec b,string name) {Check((a-b).Length<1e-8,name);}
    static RouteSession Session() {var s=new RouteSession();s.BeginSelection();s.Start(new Vec(0,0,0),.01);return s;}
    static void Main()
    {
        foreach(var direction in new[]{Vec.XAxis,Vec.ZAxis,Vec.ZAxis*-1,new Vec(1,2,3),new Vec(.0001,0,1)})
        {
            var f=new RoutingFrame(direction,false);
            Near(f.U,direction.Unit(),"Outward direction");Check(Math.Abs(f.U.Dot(f.V))+Math.Abs(f.U.Dot(f.W))+Math.Abs(f.V.Dot(f.W))<1e-10,"Orthonormal frame");Near(f.U.Cross(f.V),f.W,"Right handed");
            for(int plane=0;plane<3;plane++)for(int i=0;i<8;i++)Check(Math.Abs(f.Direction(plane,i*45).Length-1)<1e-10,"Unit directions");
        }
        var project=new RoutingFrame(Vec.ZAxis,true);
        Near(project.Direction(0,0),Vec.XAxis,"XY zero");Near(project.Direction(0,90),Vec.YAxis,"XY positive");Near(project.Direction(1,90),Vec.ZAxis,"XZ positive");Near(project.Direction(2,0),Vec.YAxis,"YZ zero");Near(project.Direction(2,90),Vec.ZAxis,"YZ positive");
        Check(RadialHitTest.Hit(0,0,2)==-1,"Center cancels");Check(RadialHitTest.Hit(151,0,0)==-1,"Outside cancels");Check(RadialHitTest.Hit(100,0,-1)==0,"East zero");Check(RadialHitTest.Hit(0,-100,-1)==2,"Screen north 90");
        double r=24*Math.PI/180;Check(RadialHitTest.Hit(100*Math.Cos(r),-100*Math.Sin(r),0)==0,"Boundary hysteresis");
        r=28*Math.PI/180;Check(RadialHitTest.Hit(100*Math.Cos(r),-100*Math.Sin(r),0)==1,"Boundary changes");
        var p=new Projection {Center=new Vec(0,0,0),Right=Vec.XAxis,Up=Vec.YAxis,Left=-1000,Top=100,Width=1000,Height=500,ModelWidth=20,ModelHeight=10};
        double length;Check(p.TryLength(new Vec(2,1,0),Vec.XAxis,-250,300,out length)&&Math.Abs(length-3)<1e-8,"Screen to model with offset and negative monitor");
        Check(!p.TryLength(new Vec(),Vec.ZAxis,0,0,out length),"View-normal rejected");
        Check(!p.TryLength(new Vec(),new Vec(.01,0,1).Unit(),0,0,out length),"Nearly view-normal rejected");
        var s=Session();Check(s.State==RoutingState.ChoosingDirection,"Start state");s.Lock(Vec.XAxis);Check(s.CommitSegment()!=null,"Zero rejected");s.Length=-1;Check(s.CommitSegment()!=null,"Negative rejected");s.Length=.001;Check(s.CommitSegment()!=null,"Short rejected");s.Length=3;Check(s.CommitSegment()==null,"Commit valid");Near(s.Origin,new Vec(3,0,0),"Advance origin");
        s.Lock(Vec.XAxis*-1);s.Length=1;Check(s.CommitSegment()!=null,"Reverse overlap rejected");s.Lock(Vec.YAxis);s.Length=2;Check(s.CommitSegment()==null,"Second segment");s.Undo();Near(s.Origin,new Vec(3,0,0),"Undo origin");Check(s.Segments.Count==1&&s.State==RoutingState.ChoosingDirection,"Undo state");
        s.Lock(Vec.ZAxis);s.Length=5;s.Escape();Check(s.Segments.Count==1&&s.State==RoutingState.ChoosingDirection,"Escape retains committed segments");
        Check(s.ValidateAll()==null,"Full validation");s.BeginCommit();Check(s.State==RoutingState.Committing,"Committing");s.CommitFailed();Check(s.State==RoutingState.ChoosingDirection&&s.Segments.Count==1,"Rollback retains route");s.BeginCommit();s.Complete();Check(s.State==RoutingState.Completed,"Complete");
        var empty=Session();Check(empty.ValidateAll()!=null,"Empty Finish blocked");empty.Cancel();Check(empty.State==RoutingState.Cancelled,"Cancel");
        Check(s.Validate(new Vec(),new Vec(double.NaN,0,0),new RouteSegment[0])!=null,"NaN rejected");
        var crossing=Session();crossing.Lock(Vec.XAxis);crossing.Length=5;crossing.CommitSegment();crossing.Lock(Vec.YAxis);crossing.Length=1;crossing.CommitSegment();crossing.Lock(Vec.XAxis*-1);crossing.Length=5;crossing.CommitSegment();crossing.Lock(Vec.YAxis*-1);crossing.Length=1;crossing.CommitSegment();crossing.Lock(Vec.XAxis);crossing.Length=2;Check(crossing.CommitSegment()!=null,"Earlier segment duplicate rejected");
        Console.WriteLine("PASS: "+count+" assertions (geometry, projection, radial, route state/rollback)");
    }
}
