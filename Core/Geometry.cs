using System;
using System.Collections.Generic;

namespace MEP_Radial_Routing_ver1.Core
{
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
    }
    public static class RadialHitTest
    {
        public static double Normalize(double angle) => (angle%360+360)%360;
        public static double Delta(double a,double b) => Math.Abs((Normalize(a-b)+180)%360-180);
        public static int Hit(double x,double y,int previous)
        {
            if(x*x+y*y<38*38 || x*x+y*y>150*150) return -1;
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
