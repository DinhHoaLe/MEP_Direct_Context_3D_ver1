using System;
using System.Collections.Generic;
using System.Linq;

namespace MEP_Radial_Routing_ver1.Core
{
    public sealed class CtrlGesture
    {
        public readonly double StartX,StartY;
        public double X {get;private set;}public double Y {get;private set;}
        public bool Released {get;private set;}
        public CtrlGesture(double x,double y) {StartX=X=x;StartY=Y=y;}
        public void Move(double x,double y) {if(!Released) {X=x;Y=y;}}
        public void Release(double x,double y) {Move(x,y);Released=true;}
    }
    public sealed class SystemOption
    {
        public readonly int Id;public readonly string Name,Classification;
        public SystemOption(int id,string name,string classification) {Id=id;Name=name;Classification=classification;}
    }
    public static class DefaultSystemSelection
    {
        public static SystemOption Choose(IEnumerable<SystemOption> options,int rememberedId=-1,string classification=null)
        {
            var all=options.OrderBy(o=>o.Name,StringComparer.OrdinalIgnoreCase).ThenBy(o=>o.Id).ToList();
            if(classification!=null)return all.FirstOrDefault(o=>o.Classification==classification);
            return all.FirstOrDefault(o=>o.Id==rememberedId)
                ??all.FirstOrDefault(o=>o.Classification=="SupplyHydronic")
                ??all.FirstOrDefault(o=>o.Classification=="DomesticColdWater")
                ??all.FirstOrDefault();
        }
    }
    public struct Vec
    {
        public readonly double X, Y, Z;
        public Vec(double x, double y, double z) { X=x; Y=y; Z=z; }
        public static Vec operator +(Vec a, Vec b) => new Vec(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static Vec operator -(Vec a, Vec b) => new Vec(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static Vec operator *(Vec a, double s) => new Vec(a.X*s,a.Y*s,a.Z*s);
        public double Dot(Vec b) => X*b.X+Y*b.Y+Z*b.Z;
        public Vec Cross(Vec b) => new Vec(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);
        public double Length => Math.Sqrt(Dot(this));
        public bool Finite => !double.IsNaN(X+Y+Z) && !double.IsInfinity(X+Y+Z);
        public Vec Unit() { var n=Length; if(!Finite || n<1e-12) throw new ArgumentException("Invalid direction"); return this*(1/n); }
        public static readonly Vec XAxis=new Vec(1,0,0), YAxis=new Vec(0,1,0), ZAxis=new Vec(0,0,1);
    }
    public sealed class RoutingFrame
    {
        public readonly Vec U,V,W;
        RoutingFrame(Vec u,Vec v)
        {
            U=u.Unit();V=(v-U*v.Dot(U)).Unit();W=U.Cross(V).Unit();
        }
        public static RoutingFrame AroundAxis(Vec outward)
        {
            Vec u=outward.Unit(),reference=Math.Abs(u.Dot(Vec.ZAxis))>.99?Vec.XAxis:Vec.ZAxis;
            return new RoutingFrame(u,reference);
        }
        // Roll rotates around the forward axis U. Tilt is deflection away from U.
        // The set of directions forms a cone, independent of camera/model planes.
        public Vec SpatialDirection(double rollDegrees,double tiltDegrees)
        {
            if(double.IsNaN(rollDegrees) || double.IsInfinity(rollDegrees) || double.IsNaN(tiltDegrees) || double.IsInfinity(tiltDegrees) || tiltDegrees<0 || tiltDegrees>90)throw new ArgumentException("Invalid roll/tilt");
            double roll=(rollDegrees%360)*Math.PI/180,tilt=tiltDegrees*Math.PI/180;
            return (U*Math.Cos(tilt)+(V*Math.Cos(roll)+W*Math.Sin(roll))*Math.Sin(tilt)).Unit();
        }
        public RoutingFrame TransportTo(Vec outward)
        {
            Vec u=outward.Unit(),cross=U.Cross(u);double sine=cross.Length,cosine=U.Dot(u);
            if(sine<1e-10)return new RoutingFrame(u,V);
            Vec axis=cross*(1/sine);
            Vec transported=V*cosine+axis.Cross(V)*sine+axis*(axis.Dot(V)*(1-cosine));
            return new RoutingFrame(u,transported);
        }
        public RoutingFrame(Vec outward, bool project)
        {
            U=project ? Vec.XAxis : outward.Unit();
            V=project ? Vec.YAxis : (Math.Abs(U.Dot(Vec.ZAxis))>.99 ? Vec.YAxis : Vec.ZAxis).Cross(U).Unit();
            W=U.Cross(V).Unit();
        }
        // UV: U -> V; UW: U -> W; VW: V -> W. Positive angles follow A toward B.
        public Vec Direction(int plane,double degrees)
        {
            if(plane<0 || plane>2 || double.IsNaN(degrees) || double.IsInfinity(degrees)) throw new ArgumentException("Invalid angle/plane");
            Vec a=plane==2?V:U, b=plane==0?V:W;
            double r=degrees*Math.PI/180; return (a*Math.Cos(r)+b*Math.Sin(r)).Unit();
        }
        public int BestPlane(Vec right,Vec up)
        {
            int best=0;double area=-1;
            for(int plane=0;plane<3;plane++)
            {
                var a=plane==2?V:U;var b=plane==0?V:W;
                double candidate=Math.Abs(a.Dot(right)*b.Dot(up)-a.Dot(up)*b.Dot(right));
                if(candidate>area+1e-9) {area=candidate;best=plane;}
            }
            return best;
        }
        public Vec ScreenDirection(int plane,int sector,Vec right,Vec up,out double modelDegrees)
        {
            double screenAngle=sector*Math.PI/4,tx=Math.Cos(screenAngle),ty=Math.Sin(screenAngle);
            double best=-2;modelDegrees=0;Vec result=Direction(plane,0);
            for(int i=0;i<8;i++)
            {
                Vec candidate=Direction(plane,i*45);double x=candidate.Dot(right),y=candidate.Dot(up),length=Math.Sqrt(x*x+y*y);
                if(length<.08)continue;
                double score=(x*tx+y*ty)/length;
                if(score>best+1e-9) {best=score;modelDegrees=i*45;result=candidate;}
            }
            return result;
        }
    }
    public static class RadialHitTest
    {
        public static double Normalize(double angle) => (angle%360+360)%360;
        public static double Delta(double a,double b) => Math.Abs((Normalize(a-b)+180)%360-180);
        public static int Hit(double x,double y,int previous)
        {
            // A marking gesture may extend beyond the visible wheel. Only its
            // center cancels; releasing farther out must still lock the direction.
            if(double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) || x*x+y*y<38*38) return -1;
            double angle=Normalize(Math.Atan2(-y,x)*180/Math.PI);
            if(previous>=0 && Delta(angle,previous*45)<=26) return previous;
            return ((int)Math.Floor((angle+22.5)/45))%8;
        }
    }
    public sealed class Projection
    {
        public Vec Center,Right,Up;
        public double Left,Top,Width,Height,ModelWidth,ModelHeight;
        public bool Contains(double x,double y) => x>=Left && y>=Top && x<Left+Width && y<Top+Height;
        public bool TryProject(Vec point,out double x,out double y)
        {
            x=y=0;
            if(Width<=0 || Height<=0 || ModelWidth<=0 || ModelHeight<=0 || !point.Finite)return false;
            Vec offset=point-Center;
            x=Left+Width/2+offset.Dot(Right)*Width/ModelWidth;
            y=Top+Height/2-offset.Dot(Up)*Height/ModelHeight;
            return true;
        }
        public bool TryLength(Vec origin,Vec direction,double x,double y,out double length)
        {
            length=0;
            if(Width<=0 || Height<=0 || ModelWidth<=0 || ModelHeight<=0) return false;
            double dx=direction.Dot(Right)*Width/ModelWidth,dy=-direction.Dot(Up)*Height/ModelHeight;
            double den=dx*dx+dy*dy;
            // Near view-normal: reject rather than amplify screen noise into huge lengths.
            if(Math.Sqrt(Math.Pow(direction.Dot(Right),2)+Math.Pow(direction.Dot(Up),2))<.08 || den<1e-12) return false;
            Vec offset=origin-Center;
            double ox=Left+Width/2+offset.Dot(Right)*Width/ModelWidth;
            double oy=Top+Height/2-offset.Dot(Up)*Height/ModelHeight;
            length=((x-ox)*dx+(y-oy)*dy)/den;
            return !double.IsNaN(length) && !double.IsInfinity(length);
        }
    }
    public enum ConnectionKind { Straight,Elbow,Reverse }
    public static class ConnectionGeometry
    {
        public static ConnectionKind Classify(Vec outward,Vec outgoing)
        {
            double dot=outward.Unit().Dot(outgoing.Unit());
            return dot>1-1e-8?ConnectionKind.Straight:dot<-1+1e-8?ConnectionKind.Reverse:ConnectionKind.Elbow;
        }
    }
    public static class ConnectionGraph
    {
        // A turn is valid only along a physical path containing an actual elbow.
        // The target is terminal; unrelated fittings beyond it cannot validate a turn.
        public static IReadOnlyList<int> FindPath(int source,int target,Func<int,IEnumerable<int>> neighbours,Func<int,bool> fitting,Func<int,bool> elbow,bool requireElbow)
        {
            var pending=new Queue<List<int>>();pending.Enqueue(new List<int>{source});
            while(pending.Count>0)
            {
                var path=pending.Dequeue();if(path.Count>32)continue;
                foreach(int next in neighbours(path[path.Count-1]))
                {
                    if(path.Contains(next))continue;
                    var extended=new List<int>(path){next};
                    if(next==target)
                    {
                        bool hasElbow=false;for(int i=1;i<path.Count;i++)if(elbow(path[i])) {hasElbow=true;break;}
                        if(!requireElbow || hasElbow)return extended;
                        continue;
                    }
                    if(fitting(next))pending.Enqueue(extended);
                }
            }
            return null;
        }
    }
    public enum RoutingState { Idle,SelectingConnector,ChoosingDirection,SettingLength,Committing,Completed,Cancelled }
    public sealed class RouteSegment
    {
        public readonly Vec Start,End;
        public RouteSegment(Vec start,Vec end) { Start=start; End=end; }
    }
    public sealed class RouteSession
    {
        readonly List<RouteSegment> segments=new List<RouteSegment>();
        public IReadOnlyList<RouteSegment> Segments => segments;
        public RoutingState State {get; private set;} = RoutingState.Idle;
        public Vec Source {get;private set;}
        public Vec Origin => segments.Count==0?Source:segments[segments.Count-1].End;
        public Vec Direction {get;private set;}
        public double Length {get;set;}
        public double Minimum {get;private set;}
        public void BeginSelection() { Require(RoutingState.Idle); State=RoutingState.SelectingConnector; }
        public void Start(Vec origin,double minimum) { Require(RoutingState.SelectingConnector); Source=origin; Minimum=minimum; State=RoutingState.ChoosingDirection; }
        public void Lock(Vec direction) { if(State!=RoutingState.ChoosingDirection && State!=RoutingState.SettingLength) throw new InvalidOperationException(); Direction=direction.Unit(); Length=0; State=RoutingState.SettingLength; }
        public string Validate(Vec start,Vec end,IEnumerable<RouteSegment> existing)
        {
            if(!start.Finite || !end.Finite || Math.Max(Math.Max(Math.Abs(end.X),Math.Abs(end.Y)),Math.Abs(end.Z))>1e6) return "Invalid coordinates / outside supported model range";
            Vec d=end-start; double n=d.Length;
            if(n<Minimum || n<1e-8) return "Segment is below Revit minimum length";
            Vec u=d.Unit();
            foreach(var s in existing)
            {
                Vec q=s.End-s.Start;
                if(q.Cross(u).Length>1e-6 || (s.Start-start).Cross(u).Length>1e-6) continue;
                double a=(s.Start-start).Dot(u),b=(s.End-start).Dot(u);
                if(Math.Min(n,Math.Max(a,b))-Math.Max(0,Math.Min(a,b))>1e-6) return "Segment overlaps an existing segment";
            }
            return null;
        }
        public string PreviewError => State!=RoutingState.SettingLength?"Choose a direction":Length<=0?"Enter a positive length":Validate(Origin,Origin+Direction*Length,segments);
        public string CommitSegment()
        {
            var error=PreviewError; if(error!=null) return error;
            segments.Add(new RouteSegment(Origin,Origin+Direction*Length)); Length=0; State=RoutingState.ChoosingDirection; return null;
        }
        public void Undo() { if(State!=RoutingState.ChoosingDirection && State!=RoutingState.SettingLength) return; if(segments.Count>0) segments.RemoveAt(segments.Count-1); Escape(); }
        public void Escape() { if(State==RoutingState.ChoosingDirection || State==RoutingState.SettingLength) { Length=0; State=RoutingState.ChoosingDirection; } }
        public string ValidateAll()
        {
            if(segments.Count==0) return "No committed segments";
            var seen=new List<RouteSegment>(); Vec expected=Source;
            foreach(var s in segments) { if((s.Start-expected).Length>1e-6) return "Route is discontinuous"; var e=Validate(s.Start,s.End,seen); if(e!=null)return e; seen.Add(s); expected=s.End; }
            return null;
        }
        public void BeginCommit() { if(State!=RoutingState.ChoosingDirection && State!=RoutingState.SettingLength)throw new InvalidOperationException(); var e=ValidateAll(); if(e!=null)throw new InvalidOperationException(e); State=RoutingState.Committing; }
        public void CommitFailed() { Require(RoutingState.Committing); State=RoutingState.ChoosingDirection; }
        public void Complete() { Require(RoutingState.Committing); State=RoutingState.Completed; }
        public void Cancel() { State=RoutingState.Cancelled; }
        void Require(RoutingState state) { if(State!=state)throw new InvalidOperationException("Unexpected routing state: "+State); }
    }
}
