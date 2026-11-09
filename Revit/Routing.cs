using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using MEP_Radial_Routing_ver1.Core;
using MEP_Radial_Routing_ver1.UI;

namespace MEP_Radial_Routing_ver1.Revit
{
    public sealed class RoutingApplication:IExternalApplication
    {
        NativeRoutingHost host;
        public Result OnStartup(UIControlledApplication app)
        {
            var panel=app.CreateRibbonPanel("MEP Radial Routing");
            panel.AddItem(new PushButtonData("MEPRadialRouting","Pipe Radial\nRouting",typeof(RoutingApplication).Assembly.Location,typeof(RoutingCommand).FullName));
            app.ControlledApplication.DocumentClosing+=DocumentClosing;app.ApplicationClosing+=ApplicationClosing;
            host=new NativeRoutingHost(app);
            return Result.Succeeded;
        }
        void DocumentClosing(object sender,DocumentClosingEventArgs e) { if(RoutingCommand.Current?.Document.Equals(e.Document)==true)RoutingCommand.Current.Dispose(); }
        void ApplicationClosing(object sender,Autodesk.Revit.UI.Events.ApplicationClosingEventArgs e)=>RoutingCommand.Current?.Dispose();
        public Result OnShutdown(UIControlledApplication app)
        {
            host?.Dispose();RoutingCommand.Current?.Dispose();app.ControlledApplication.DocumentClosing-=DocumentClosing;app.ApplicationClosing-=ApplicationClosing;return Result.Succeeded;
        }
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class RoutingCommand:IExternalCommand
    {
        public static RoutingSessionController Current;
        public Result Execute(ExternalCommandData data,ref string message,ElementSet elements)
        {
            if(Current!=null) {Current.Resume();return Result.Succeeded;}
            var ui=data.Application.ActiveUIDocument;
            if(ui==null || ui.Document.IsFamilyDocument || !(ui.ActiveView is View3D) || ((View3D)ui.ActiveView).IsPerspective)
            { message="Open an orthographic 3D view in a project document.";return Result.Failed; }
            try { Current=new RoutingSessionController(data.Application); Current.BeginDirect();return Result.Succeeded; }
            catch(Autodesk.Revit.Exceptions.OperationCanceledException) {Current?.Dispose();Current=null;return Result.Cancelled;}
            catch(Exception ex) {Current?.Dispose();Current=null;RoutingLog.Write("Command",ex);message=ex.ToString();return Result.Failed;}
        }
    }
    public sealed class RevitRequestHandler:IExternalEventHandler
    {
        readonly Queue<Action<UIApplication>> requests=new Queue<Action<UIApplication>>();
        public Action<Exception> Error;
        public void Enqueue(Action<UIApplication> request) { lock(requests) requests.Enqueue(request); }
        public void Clear() { lock(requests) requests.Clear(); }
        public void Execute(UIApplication app)
        {
            while(true)
            {
                Action<UIApplication> action;lock(requests) {if(requests.Count==0)return;action=requests.Dequeue();}
                try {action(app);}catch(Exception ex){Error?.Invoke(ex);}
            }
        }
        public string GetName()=>"MEP Radial Routing requests";
    }
    public sealed class RoutingSessionController:IDisposable
    {
        readonly UIApplication app;readonly UIDocument ui;readonly int viewId;
        public Document Document {get;}
        readonly RadialWindow radial;readonly InputController input;readonly PreviewRenderer renderer;
        readonly RevitRequestHandler handler;readonly ExternalEvent external;
        RouteSession session=new RouteSession();
        readonly List<RouteSegment> createdSegments=new List<RouteSegment>();
        List<SystemOption> systemOptions;
        RoutingFrame frame;RoutingSettings settings;ConnectorChoice source;
        Projection projection;bool dirty=true,disposed,menuOpen,pending;
        string creationError,lastError;
        static readonly double[] TiltPresets={0,15,22.5,30,45,60,90};
        int tiltIndex=4,savedTiltIndex;double roll,savedRoll;Vec hoverDirection;bool viewAvailable;
        public RoutingSessionController(UIApplication application)
        {
            app=application;ui=app.ActiveUIDocument;Document=ui.Document;viewId=ui.ActiveView.Id.IntegerValue;
            radial=new RadialWindow(app.MainWindowHandle);input=new InputController(app.MainWindowHandle);
            renderer=new PreviewRenderer(Document,viewId);
            handler=new RevitRequestHandler {Error=ex=>{pending=false;RoutingLog.Write("Route request",ex);Status(ex.Message,true);}};external=ExternalEvent.Create(handler);
            try
            {
                renderer.Register();
                input.Enabled=()=>!disposed && !pending && viewAvailable && (session.State==RoutingState.ChoosingDirection || session.State==RoutingState.SettingLength);
                input.InViewport=(x,y)=>projection!=null && projection.Contains(x,y);
                input.CanCommit=()=>session.State==RoutingState.SettingLength;
                input.Key=OnKey;input.Move=OnMove;input.Click=Commit;input.Wheel=delta=>{if(menuOpen){tiltIndex=Math.Max(0,Math.Min(TiltPresets.Length-1,tiltIndex+delta));UpdateMenu();}};
                input.FocusLost=CloseMenu;input.Error=ex=>{Status("Input error: "+ex.Message,true);Request(a=>Dispose());};app.Idling+=OnIdling;
                Status("Choose a start point, then hold Ctrl to route.");
            }
            catch {Dispose();throw;}
        }
        public void Resume() {dirty=true;}
        public void AcceptCtrlGesture(CtrlGesture gesture)
        {
            if(gesture==null)return;
            input.AdoptControlPress();OnKey(17,gesture.StartX,gesture.StartY);
            if(gesture.Released)OnKey(-17,gesture.X,gesture.Y);
            else if(!input.ControlHeld) {var point=InputController.Cursor;OnKey(-17,point.X,point.Y);}
            else OnMove(gesture.X,gesture.Y);
        }
        void Status(string message,bool error=false)
        {
            if(error && message!=lastError)RoutingLog.Write("Route status",new InvalidOperationException(message));
            lastError=error?message:null;
        }
        public void BeginDirect(ConnectorChoice chosen=null,bool openMenu=false)
        {
            try
            {
                EnsureContext();
                source=chosen;
                Vec origin=new Vec();
                if(source==null)
                {
                    var cursor=InputController.Cursor;
                    var selected=ui.Selection.GetElementIds().Select(Document.GetElement).SelectMany(ConnectorSelectionService.Get).Where(ConnectorSelectionService.Valid).ToList();
                    if(selected.Count>0)
                    {
                        var projection=NativeRoutingHost.Capture(ui);
                        source=selected.OrderBy(c=>NativeRoutingHost.ScreenDistance(projection,PreviewRenderer.FromXyz(c.Origin),cursor.X,cursor.Y)).Select(ConnectorSelectionService.Snapshot).First();
                    }
                    else
                    {
                        origin=PickStartPoint();
                        source=new FilteredElementCollector(Document,ui.ActiveView.Id).WhereElementIsNotElementType().Where(e=>e is MEPCurve || e is FamilyInstance)
                            .SelectMany(ConnectorSelectionService.Get).Where(ConnectorSelectionService.Valid)
                            .Where(c=>c.Origin.DistanceTo(PreviewRenderer.ToXyz(origin))<1.0/304.8)
                            .OrderBy(c=>c.Origin.DistanceTo(PreviewRenderer.ToXyz(origin))).Select(ConnectorSelectionService.Snapshot).FirstOrDefault();
                    }
                }
                if(source!=null)origin=source.Origin;
                settings=source==null?PipeDefaults.ResolveFree(Document,origin):PipeDefaults.Resolve(Document,source);frame=RoutingFrame.AroundAxis(source==null?PreviewRenderer.FromXyz(ui.ActiveView.RightDirection):source.Direction);
                systemOptions=PipeDefaults.Systems(Document);UpdateSystemLabel();
                session.BeginSelection();session.Start(origin,Math.Max(app.Application.ShortCurveTolerance,.1/12)+1e-7);
                projection=NativeRoutingHost.Capture(ui);viewAvailable=projection!=null;
                Status("Ctrl: direction. Click/Enter: create pipe. Escape: end routing.");
                input.Start();dirty=true;
                RoutingLog.Info("Route start: source="+(source==null?"free":source.OwnerUniqueId+"/#"+source.ConnectorId)+", type="+settings.PipeTypeId+", diameter mm="+settings.DiameterMm+", axis="+frame.U.X+","+frame.U.Y+","+frame.U.Z);
                if(openMenu && input.ControlHeld) {input.AdoptControlPress();var p=InputController.Cursor;OnKey(17,p.X,p.Y);}
            }
            catch {Dispose();throw;}
        }
        Vec PickStartPoint()
        {
            // PickPoint requires a work plane. Roll back the temporary plane and view
            // assignment before routing begins, also on Escape or API errors.
            using(var group=new TransactionGroup(Document,"Temporary routing work plane"))
            {
                group.Start();
                try
                {
                    if(ui.ActiveView.SketchPlane==null)
                    {
                        using(var tx=new Transaction(Document,"Routing pick plane"))
                        {
                            tx.Start();
                            ui.ActiveView.SketchPlane=SketchPlane.Create(Document,Plane.CreateByNormalAndOrigin(ui.ActiveView.ViewDirection,ui.ActiveView.Origin));tx.Commit();
                        }
                    }
                    return PreviewRenderer.FromXyz(ui.Selection.PickPoint(Autodesk.Revit.UI.Selection.ObjectSnapTypes.Endpoints|Autodesk.Revit.UI.Selection.ObjectSnapTypes.Intersections,"Click an open connector or start point; Ctrl chooses directions after this point (Esc cancels)"));
                }
                finally {group.RollBack();}
            }
        }
        void Request(Action<UIApplication> action)
        {
            if(disposed || pending)return;pending=true;
            handler.Enqueue(a=>{try {if(!disposed)action(a);}finally {pending=false;}});
            var result=external.Raise();
            if(result!=ExternalEventRequest.Accepted && result!=ExternalEventRequest.Pending) {handler.Clear();pending=false;Status("Revit rejected the request; try again when Revit is ready.",true);}
        }
        void EnsureContext()
        {
            if(app.ActiveUIDocument==null || !app.ActiveUIDocument.Document.Equals(Document) || app.ActiveUIDocument.ActiveView.Id.IntegerValue!=viewId || !(ui.ActiveView is View3D) || ((View3D)ui.ActiveView).IsPerspective)
                throw new InvalidOperationException("Return to the original orthographic 3D view.");
        }
        void OnKey(int key,double x,double y)
        {
            creationError=null;
            if(key==17 && !menuOpen)
            {
                radial.Menu.Notice=null;
                menuOpen=true;savedTiltIndex=tiltIndex;savedRoll=roll;radial.Open(x,y);UpdateMenu();
            }
            else if(key==-17 && menuOpen)
            {
                int hit=radial.Hit(x,y);
                if(hit>=0) {session.Lock(MenuDirection(hit));RoutingLog.Info("Direction locked: axis-relative 3D, roll="+roll+", tilt="+TiltPresets[tiltIndex]);menuOpen=false;radial.Hide();OnMove(x,y);dirty=true;}
                else CloseMenu();
            }
            else if(key==9 && menuOpen && source==null && systemOptions.Count>1)
            {
                int index=systemOptions.FindIndex(s=>s.Id==settings.SystemId);
                settings.SystemId=systemOptions[(index+1)%systemOptions.Count].Id;
                UpdateSystemLabel();radial.Menu.InvalidateVisual();
            }
            else if(key==27) {if(menuOpen)CloseMenu();else if(session.State==RoutingState.ChoosingDirection)Request(a=>Dispose());else {session.Escape();dirty=true;}}
            else if(key==8) {CloseMenu();session.Escape();dirty=true;}
            else if(key==13 && !menuOpen) {if(session.State==RoutingState.ChoosingDirection)Finish();else {OnMove(x,y);Commit();}}
        }
        void OnMove(double x,double y)
        {
            if(menuOpen)
            {
                int hit=radial.Hit(x,y);if(hit!=radial.Menu.Selected){radial.Menu.Selected=hit;UpdateMenu();}return;
            }
            if(session.State!=RoutingState.SettingLength || projection==null || !projection.Contains(x,y))return;
            double length;
            if(!projection.TryLength(session.Origin,session.Direction,x,y,out length))
            {if(session.Length!=0){session.Length=0;dirty=true;}Status("Direction is nearly along the view. Rotate the view or choose another direction.",true);return;}
            if(Math.Abs(session.Length-length)>1e-6) {session.Length=length;creationError=null;dirty=true;}
        }
        void UpdateSystemLabel()
        {
            var option=systemOptions?.FirstOrDefault(s=>s.Id==settings.SystemId);
            radial.Menu.SystemLabel=(option?.Name??"System")+(source==null?"  | Tab: system":"");
        }
        Vec MenuDirection(int sector)
        {
            roll=sector*45;return frame.SpatialDirection(roll,TiltPresets[tiltIndex]);
        }
        void UpdateMenu()
        {
            radial.Menu.TiltDegrees=TiltPresets[tiltIndex];
            for(int i=0;i<8;i++)radial.Menu.Angles[i]=i*45;
            radial.Menu.InvalidateVisual();
            if(radial.Menu.Selected>=0)hoverDirection=MenuDirection(radial.Menu.Selected);dirty=true;
        }
        void CloseMenu()
        {
            if(!menuOpen)return;menuOpen=false;tiltIndex=savedTiltIndex;roll=savedRoll;radial.Hide();dirty=true;
        }
        void Commit()
        {
            if(pending || session.State!=RoutingState.SettingLength)return;
            string error=session.PreviewError;
            if(error==null)error=session.Validate(session.Origin,session.Origin+session.Direction*session.Length,createdSegments);
            if(error!=null) {Status(error,true);return;}
            Request(a=>
            {
                EnsureContext();
                var segment=new RouteSegment(session.Origin,session.Origin+session.Direction*session.Length);
                // Keep the live preview intact until the document transaction succeeds.
                var attempt=new RouteSession();attempt.BeginSelection();attempt.Start(session.Origin,session.Minimum);
                attempt.Lock(session.Direction);attempt.Length=session.Length;
                var invalid=attempt.CommitSegment();if(invalid!=null) {Status(invalid,true);return;}
                try
                {
                    var result=PipeCreationService.Create(Document,attempt,settings,source);
                    createdSegments.Add(segment);source=result.End;
                    frame=frame.TransportTo(source.Direction);
                    PipeDefaults.Remember(Document,settings);UpdateSystemLabel();
                    RoutingLog.Info("Pipe created on click: source="+source.OwnerUniqueId+", elements="+result.Ids.Count);
                    session=new RouteSession();session.BeginSelection();session.Start(source.Origin,attempt.Minimum);
                    creationError=null;dirty=true;
                }
                catch(Exception ex)
                {
                    RoutingLog.Write("Create pipe on click",ex);creationError=ex.Message;Status(creationError,true);
                    radial.Menu.Notice="Error";dirty=true;
                    TaskDialog.Show("Pipe Routing",creationError);
                }
            });
        }
        void Finish()
        {
            if(frame==null || pending)return;
            CloseMenu();Request(a=>Dispose());
        }
        void OnIdling(object sender,IdlingEventArgs e)
        {
            if(disposed)return;
            try
            {
                viewAvailable=app.ActiveUIDocument!=null && app.ActiveUIDocument.Document.Equals(Document) && ui.ActiveView.Id.IntegerValue==viewId && ui.ActiveView is View3D && !((View3D)ui.ActiveView).IsPerspective;
                if(!viewAvailable){projection=null;CloseMenu();return;}
                var view=ui.ActiveView;var uv=ui.GetOpenUIViews().FirstOrDefault(v=>v.ViewId.IntegerValue==viewId);
                projection=null;
                if(uv!=null)
                {
                    var rect=uv.GetWindowRectangle();var corners=uv.GetZoomCorners();Vec right=PreviewRenderer.FromXyz(view.RightDirection),up=PreviewRenderer.FromXyz(view.UpDirection);
                    Vec delta=PreviewRenderer.FromXyz(corners[1]-corners[0]);
                    projection=new Projection {Center=PreviewRenderer.FromXyz((corners[0]+corners[1])*.5),Right=right,Up=up,Left=rect.Left,Top=rect.Top,Width=rect.Right-rect.Left,Height=rect.Bottom-rect.Top,ModelWidth=Math.Abs(delta.Dot(right)),ModelHeight=Math.Abs(delta.Dot(up))};
                }
                if(dirty)
                {
                    dirty=false;BuildPreview();ui.RefreshActiveView();
                    if(frame!=null)
                    {
                        string problem=creationError ?? (session.State==RoutingState.SettingLength?session.PreviewError:null);
                        Status(problem??"Ready",problem!=null);
                    }
                }
                if(renderer.LastError!=null)Status("Graphics error: "+renderer.LastError,true);
            }
            catch(Exception ex) {RoutingLog.Write("Preview update",ex);Status(ex.Message,true);Dispose();}
        }
        void BuildPreview()
        {
            var strokes=new List<PreviewRenderer.Stroke>();
            if(frame!=null)
            {
                Marker(strokes,session.Source,true);
                foreach(var s in session.Segments)Tube(strokes,s.Start,s.End,settings.DiameterMm/609.6,false);
                Vec o=session.Origin;
                strokes.Add(new PreviewRenderer.Stroke(o,o+frame.U*2,235,70,70));strokes.Add(new PreviewRenderer.Stroke(o,o+frame.V*2,80,205,110));strokes.Add(new PreviewRenderer.Stroke(o,o+frame.W*2,85,150,245));
                // Preview the cone of possible 3D directions around the pipe axis.
                if(menuOpen)
                {
                    for(int i=0;i<32;i++)
                    {
                        Vec a=o+frame.SpatialDirection(i*360.0/32,TiltPresets[tiltIndex])*2;
                        Vec b=o+frame.SpatialDirection((i+1)*360.0/32,TiltPresets[tiltIndex])*2;
                        strokes.Add(new PreviewRenderer.Stroke(a,b,110,110,130));
                        if(i%4==0)strokes.Add(new PreviewRenderer.Stroke(o,a,110,110,130));
                    }
                }
                if(menuOpen && radial.Menu.Selected>=0)Arrow(strokes,o,o+hoverDirection*3,false);
                else if(session.State==RoutingState.SettingLength)
                {
                    Vec end=o+session.Direction*session.Length;bool invalid=creationError!=null || session.PreviewError!=null;
                    if(end.Finite && (end-o).Length<1e6) {Tube(strokes,o,end,settings.DiameterMm/609.6,invalid);Arrow(strokes,o,end,invalid);}
                }
            }
            renderer.Set(strokes);
        }
        static void Marker(List<PreviewRenderer.Stroke> lines,Vec o,bool active)
        {
            foreach(var v in new[]{Vec.XAxis,Vec.YAxis,Vec.ZAxis})lines.Add(new PreviewRenderer.Stroke(o-v*.12,o+v*.12,active?(byte)255:(byte)140,active?(byte)55:(byte)140,active?(byte)55:(byte)140));
        }
        static void Arrow(List<PreviewRenderer.Stroke> lines,Vec a,Vec b,bool invalid)
        {
            Vec d=b-a;if(d.Length<1e-8)return;Vec side=(Math.Abs(d.Unit().Z)>.99?Vec.XAxis:Vec.ZAxis).Cross(d).Unit();double n=Math.Min(.3,d.Length*.2);
            byte r=invalid?(byte)255:(byte)255,g=invalid?(byte)45:(byte)190;
            lines.Add(new PreviewRenderer.Stroke(a,b,r,g,30));lines.Add(new PreviewRenderer.Stroke(b,b-d.Unit()*n+side*n*.5,r,g,30));lines.Add(new PreviewRenderer.Stroke(b,b-d.Unit()*n-side*n*.5,r,g,30));
        }
        static void Tube(List<PreviewRenderer.Stroke> lines,Vec a,Vec b,double radius,bool invalid)
        {
            Vec d=b-a;if(d.Length<1e-8)return;var f=new RoutingFrame(d,false);
            byte r=invalid?(byte)255:(byte)60,g=invalid?(byte)50:(byte)205,blue=invalid?(byte)60:(byte)230;
            lines.Add(new PreviewRenderer.Stroke(a,b,r,g,blue));
            for(int i=0;i<12;i++)
            {
                double t=i*Math.PI/6,next=(i+1)*Math.PI/6;Vec v=(f.V*Math.Cos(t)+f.W*Math.Sin(t))*radius,w=(f.V*Math.Cos(next)+f.W*Math.Sin(next))*radius;
                lines.Add(new PreviewRenderer.Stroke(a+v,a+w,r,g,blue));lines.Add(new PreviewRenderer.Stroke(b+v,b+w,r,g,blue));if(i%3==0)lines.Add(new PreviewRenderer.Stroke(a+v,b+v,r,g,blue));
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            app.Idling-=OnIdling;input?.Dispose();radial?.Close();renderer?.Dispose();external?.Dispose();
            if(session.State!=RoutingState.Completed)session.Cancel();
            if(Document.IsValidObject && app.ActiveUIDocument!=null && app.ActiveUIDocument.Document.Equals(Document))try{ui.RefreshActiveView();}catch(Autodesk.Revit.Exceptions.InvalidOperationException){}
            if(RoutingCommand.Current==this)RoutingCommand.Current=null;
        }
    }
}


