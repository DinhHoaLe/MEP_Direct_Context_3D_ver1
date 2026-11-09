using System;
using System.Collections.Generic;
using MEP_Radial_Routing_ver1.Core;

static class GeometryTests
{
    static int count;
    static void Check(bool ok,string name) { if(!ok)throw new Exception(name);count++; }
    static void Near(Vec a,Vec b,string name) {Check((a-b).Length<1e-8,name);}
    static RouteSession Session() {var s=new RouteSession();s.BeginSelection();s.Start(new Vec(0,0,0),.01);return s;}
    [STAThread]
    static void Main()
    {
        var transfer=new CtrlGesture(-500,250);transfer.Move(-300,50);transfer.Release(-200,-50);transfer.Move(900,900);
        Check(transfer.StartX==-500&&transfer.StartY==250,"Ctrl handoff preserves original menu center across delayed API execution");
        Check(transfer.Released&&transfer.X==-200&&transfer.Y==-50,"Ctrl handoff preserves release point even after subsequent mouse movement");
        Check(RadialHitTest.Hit(transfer.X-transfer.StartX,transfer.Y-transfer.StartY,-1)==1,"Quick Ctrl gesture can lock direction after native tool ends");
        var systems=new[]{new SystemOption(10,"Cold water","DomesticColdWater"),new SystemOption(20,"Hydronic supply","SupplyHydronic"),new SystemOption(30,"Sanitary","Sanitary")};
        Check(DefaultSystemSelection.Choose(systems).Id==20,"Empty model with multiple systems can start using hydronic supply");
        Check(DefaultSystemSelection.Choose(systems,10).Id==10,"Remember previous successful system");
        Check(DefaultSystemSelection.Choose(systems,999).Id==20,"Deleted remembered system falls back safely");
        Check(DefaultSystemSelection.Choose(systems,classification:"Sanitary").Id==30,"Source connector classification takes priority");
        Check(DefaultSystemSelection.Choose(systems,classification:"FireProtectWet")==null,"Do not assign incompatible default to a source connector");
        Check(DefaultSystemSelection.Choose(new SystemOption[0])==null,"Missing project system types is distinct from multiple system types");
        Check(DefaultSystemSelection.Choose(new[]{systems[0],systems[2]}).Id==10,"Cold water fallback when hydronic supply is absent");
        foreach(var direction in new[]{Vec.XAxis,Vec.ZAxis,Vec.ZAxis*-1,new Vec(1,2,3),new Vec(.0001,0,1)})
        {
            var f=new RoutingFrame(direction,false);
            Near(f.U,direction.Unit(),"Outward direction");Check(Math.Abs(f.U.Dot(f.V))+Math.Abs(f.U.Dot(f.W))+Math.Abs(f.V.Dot(f.W))<1e-10,"Orthonormal frame");Near(f.U.Cross(f.V),f.W,"Right handed");
            for(int plane=0;plane<3;plane++)for(int i=0;i<8;i++)Check(Math.Abs(f.Direction(plane,i*45).Length-1)<1e-10,"Unit directions");
        }
        foreach(var axis in new[]{Vec.XAxis,Vec.ZAxis,Vec.ZAxis*-1,new Vec(1,2,3),new Vec(.0001,0,1)})
        {
            var spatial=RoutingFrame.AroundAxis(axis);
            Near(spatial.U,axis.Unit(),"3D reference follows actual source axis");
            Near(spatial.U.Cross(spatial.V),spatial.W,"3D frame is right handed for horizontal, vertical and inclined sources");
            foreach(double tilt in new[]{0,15,22.5,30,45,60,90})for(int sector3d=0;sector3d<8;sector3d++)
            {
                var vector=spatial.SpatialDirection(sector3d*45,tilt);
                Check(vector.Finite&&Math.Abs(vector.Length-1)<1e-10,"Every 3D roll/tilt yields a finite unit vector");
                Check(Math.Abs(vector.Dot(spatial.U)-Math.Cos(tilt*Math.PI/180))<1e-10,"Deflection is measured from source axis rather than a world plane");
            }
            var d0=spatial.SpatialDirection(0,45);var d1=spatial.SpatialDirection(120,45);var d2=spatial.SpatialDirection(240,45);
            Check(Math.Abs(d0.Dot(d1.Cross(d2)))>.1,"Cone directions are not restricted to any single plane through origin");
            var transported=spatial.TransportTo(spatial.SpatialDirection(45,90));
            Near(transported.U,spatial.SpatialDirection(45,90),"Next segment becomes new reference axis");
            Near(transported.U.Cross(transported.V),transported.W,"Frame remains orthonormal after turning");
        }
        var axisX=RoutingFrame.AroundAxis(Vec.XAxis);
        Near(axisX.SpatialDirection(0,0),Vec.XAxis,"Zero tilt continues straight along pipe");
        Near(axisX.SpatialDirection(0,90),Vec.ZAxis,"Zero roll bends toward local up");
        Near(axisX.SpatialDirection(90,90),Vec.YAxis*-1,"Quarter roll bends around pipe axis");
        var turnedUp=axisX.TransportTo(Vec.ZAxis);
        Near(turnedUp.V,Vec.XAxis*-1,"Transport through vertical preserves roll without resetting to world axes");
        Near(turnedUp.SpatialDirection(0,0),Vec.ZAxis,"Straight continuation follows new vertical segment");
        Near(axisX.TransportTo(Vec.XAxis*-1).U,Vec.XAxis*-1,"Opposite axis transport stays finite");
        bool rejectedTilt=false;try{axisX.SpatialDirection(0,91);}catch(ArgumentException){rejectedTilt=true;}Check(rejectedTilt,"Reject tilt beyond supported fitting deflection");
        bool rejectedRoll=false;try{axisX.SpatialDirection(double.NaN,45);}catch(ArgumentException){rejectedRoll=true;}Check(rejectedRoll,"Reject invalid spatial angle");
        var project=new RoutingFrame(Vec.ZAxis,true);
        Check(ConnectionGeometry.Classify(Vec.XAxis,Vec.XAxis)==ConnectionKind.Straight,"Straight extension needs no elbow");
        Check(ConnectionGeometry.Classify(Vec.XAxis,Vec.YAxis)==ConnectionKind.Elbow,"90-degree turn requires elbow");
        Check(ConnectionGeometry.Classify(Vec.XAxis,new Vec(1,1,0))==ConnectionKind.Elbow,"45-degree turn requires elbow");
        Check(ConnectionGeometry.Classify(Vec.XAxis,Vec.XAxis*-1)==ConnectionKind.Reverse,"Reverse direction rejected separately");
        var links=new Dictionary<int,int[]> {{1,new[]{2}},{2,new[]{1,3}},{3,new[]{2}},{4,new int[0]},{5,new int[0]}};
        Func<bool,IReadOnlyList<int>> path=required=>ConnectionGraph.FindPath(1,2,id=>links[id],id=>id==3||id==4,id=>id==3,required);
        Check(path(false)!=null,"Direct connector link is valid for a straight run");
        Check(path(true)==null,"Regression: direct link at a turn is not a fitting");
        links[1]=new[]{3};links[3]=new[]{1,2};links[2]=new[]{3};
        Check(path(true)!=null&&path(true).Count==3,"A connected elbow validates the turn");
        links[3]=new[]{1};Check(path(true)==null,"Elbow connected on one side is rejected");
        links[1]=new[]{4};links[4]=new[]{1,2};links[2]=new[]{4};
        Check(path(true)==null,"Union alone does not validate a turning connection");
        Check(path(false)!=null,"Union is accepted for a straight connection");
        links[1]=new[]{2,3};links[2]=new[]{1};links[3]=new[]{1};
        Check(path(true)==null,"Unrelated elbow branch cannot validate direct connection");
        links[1]=new[]{5};links[5]=new[]{1,3};links[3]=new[]{5,2};links[2]=new[]{3};
        Check(path(true)==null,"Do not validate through a different pipe in the network");
        Near(project.Direction(0,0),Vec.XAxis,"XY zero");Near(project.Direction(0,90),Vec.YAxis,"XY positive");Near(project.Direction(1,90),Vec.ZAxis,"XZ positive");Near(project.Direction(2,0),Vec.YAxis,"YZ zero");Near(project.Direction(2,90),Vec.ZAxis,"YZ positive");
        Check(RadialHitTest.Hit(0,0,2)==-1,"Center cancels");Check(RadialHitTest.Hit(151,0,0)==0,"Beyond wheel still locks direction");Check(RadialHitTest.Hit(100,0,-1)==0,"East zero");Check(RadialHitTest.Hit(0,-100,-1)==2,"Screen north 90");
        Check(RadialHitTest.Hit(0,-400,-1)==2,"Far pointer above wheel keeps upward direction");
        Check(RadialHitTest.Hit(double.NaN,0,-1)==-1,"Invalid radial pointer rejected");
        Check(project.BestPlane(Vec.XAxis,Vec.YAxis)==0,"Top view chooses XY");
        Check(project.BestPlane(Vec.XAxis,Vec.ZAxis)==1,"Front view chooses XZ");
        Check(project.BestPlane(Vec.YAxis*-1,Vec.ZAxis)==2,"Left view chooses YZ");
        double modelAngle;
        Near(project.ScreenDirection(2,0,Vec.YAxis*-1,Vec.ZAxis,out modelAngle),Vec.YAxis*-1,"Left-view screen right maps to minus Y");
        Check(modelAngle==180,"Menu label follows actual model angle");
        Near(project.ScreenDirection(2,2,Vec.YAxis*-1,Vec.ZAxis,out modelAngle),Vec.ZAxis,"Left-view screen up maps to plus Z");
        Near(project.ScreenDirection(1,6,Vec.XAxis,Vec.ZAxis,out modelAngle),Vec.ZAxis*-1,"Front-view screen down maps to minus Z");
        Near(project.ScreenDirection(0,0,Vec.XAxis*-1,Vec.YAxis*-1,out modelAngle),Vec.XAxis*-1,"Rotated top view respects screen right");
        var leftProjection=new Projection {Center=new Vec(),Right=Vec.YAxis*-1,Up=Vec.ZAxis,Width=1000,Height=1000,ModelWidth=20,ModelHeight=20};
        var leftSession=Session();int sector=RadialHitTest.Hit(0,-400,-1);
        leftSession.Lock(project.ScreenDirection(project.BestPlane(leftProjection.Right,leftProjection.Up),sector,leftProjection.Right,leftProjection.Up,out modelAngle));
        double leftLength;Check(leftProjection.TryLength(new Vec(),leftSession.Direction,500,250,out leftLength),"Screenshot flow gives a visible direction");
        leftSession.Length=leftLength;Check(leftSession.CommitSegment()==null,"Left view can commit upward route");Near(leftSession.Origin,new Vec(0,0,5),"Left-view route ends at expected Z");
        double r=24*Math.PI/180;Check(RadialHitTest.Hit(100*Math.Cos(r),-100*Math.Sin(r),0)==0,"Boundary hysteresis");
        r=28*Math.PI/180;Check(RadialHitTest.Hit(100*Math.Cos(r),-100*Math.Sin(r),0)==1,"Boundary changes");
        var p=new Projection {Center=new Vec(0,0,0),Right=Vec.XAxis,Up=Vec.YAxis,Left=-1000,Top=100,Width=1000,Height=500,ModelWidth=20,ModelHeight=10};
        double sx,sy;Check(p.TryProject(new Vec(2,1,0),out sx,out sy)&&Math.Abs(sx+400)<1e-8&&Math.Abs(sy-300)<1e-8,"Project connector to negative monitor");
        Check(!p.TryProject(new Vec(double.NaN,0,0),out sx,out sy),"Do not project invalid connector");
        Check(!new Projection().TryProject(new Vec(),out sx,out sy),"Reject missing viewport");
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
        InputControllerTests.Run();
    }
}
