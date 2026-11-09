using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
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
        public Result OnStartup(UIControlledApplication app)
        {
            var panel=app.CreateRibbonPanel("MEP Radial Routing");
            panel.AddItem(new PushButtonData("MEPRadialRouting","Pipe Radial\nRouting",typeof(RoutingApplication).Assembly.Location,typeof(RoutingCommand).FullName));
            app.ControlledApplication.DocumentClosing+=DocumentClosing;app.ApplicationClosing+=ApplicationClosing;
            return Result.Succeeded;
        }
        void DocumentClosing(object sender,DocumentClosingEventArgs e) { if(RoutingCommand.Current?.Document.Equals(e.Document)==true)RoutingCommand.Current.Dispose(); }
        void ApplicationClosing(object sender,Autodesk.Revit.UI.Events.ApplicationClosingEventArgs e)=>RoutingCommand.Current?.Dispose();
        public Result OnShutdown(UIControlledApplication app)
        {
            RoutingCommand.Current?.Dispose();app.ControlledApplication.DocumentClosing-=DocumentClosing;app.ApplicationClosing-=ApplicationClosing;return Result.Succeeded;
        }
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class RoutingCommand:IExternalCommand
    {
        public static RoutingSessionController Current;
        public Result Execute(ExternalCommandData data,ref string message,ElementSet elements)
        {
            if(Current!=null) {Current.Show();return Result.Succeeded;}
            var ui=data.Application.ActiveUIDocument;
            if(ui==null || ui.Document.IsFamilyDocument || !(ui.ActiveView is View3D) || ((View3D)ui.ActiveView).IsPerspective)
            { message="Open an orthographic 3D view in a project document.";return Result.Failed; }
            try { Current=new RoutingSessionController(data.Application); Current.Show();return Result.Succeeded; }
            catch(Exception ex) {Current?.Dispose();Current=null;message=ex.ToString();return Result.Failed;}
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
        readonly SettingsWindow window;readonly RadialWindow radial;readonly InputController input;readonly PreviewRenderer renderer;
        readonly RevitRequestHandler handler;readonly ExternalEvent external;
        readonly RouteSession session=new RouteSession();
        RoutingFrame frame;RoutingSettings settings;ConnectorChoice source;
        Projection projection;bool dirty=true,disposed,menuOpen,pending,explicitLength,closeRequested;
        string creationError;
        int plane,savedPlane;double angle;Vec hoverDirection;bool viewAvailable;
        public RoutingSessionController(UIApplication application)
        {
            app=application;ui=app.ActiveUIDocument;Document=ui.Document;viewId=ui.ActiveView.Id.IntegerValue;
            window=new SettingsWindow();new WindowInteropHelper(window).Owner=app.MainWindowHandle;
            radial=new RadialWindow(app.MainWindowHandle);input=new InputController(app.MainWindowHandle);
            renderer=new PreviewRenderer(Document,viewId);
            handler=new RevitRequestHandler {Error=ex=>{pending=false;window.Status(ex.Message,true);}};external=ExternalEvent.Create(handler);
            try
            {
                renderer.Register();
                Load(window.Systems,typeof(PipingSystemType));Load(window.Types,typeof(PipeType));Load(window.Levels,typeof(Level));
                window.SelectSource=()=>Request(PickSource);
                window.Start=Start;window.Finish=Finish;window.Cancel=()=>Request(a=>Dispose());
                window.ApplyLength=ExactLength;window.ApplyAngle=CustomAngle;
                window.Connectors.SelectionChanged+=(s,e)=>{source=window.Connectors.SelectedItem as ConnectorChoice;if(source!=null)window.Diameter.Text=(source.Diameter*304.8).ToString("0.###");dirty=true;};
                window.Closed+=(s,e)=>{if(!disposed){closeRequested=true;Request(a=>Dispose());}};
                input.Enabled=()=>!disposed && !closeRequested && !pending && viewAvailable && (session.State==RoutingState.ChoosingDirection || session.State==RoutingState.SettingLength);
                input.InViewport=(x,y)=>projection!=null && projection.Contains(x,y);
                input.CanCommit=()=>session.State==RoutingState.SettingLength;
                input.Key=OnKey;input.Move=OnMove;input.Click=Commit;input.Wheel=delta=>{if(menuOpen){plane=(plane+delta+3)%3;UpdateMenu();}};
                input.FocusLost=CloseMenu;input.Error=ex=>{window.Status("Input error: "+ex.Message,true);Request(a=>Dispose());};app.Idling+=OnIdling;
                window.Status("Select a source element, choose an open connector, then Start Routing.");
            }
            catch {Dispose();throw;}
        }
        void Load(System.Windows.Controls.ComboBox box,Type type)
        {
            foreach(var e in new FilteredElementCollector(Document).OfClass(type).OrderBy(e=>e.Name))box.Items.Add(new Option {Id=e.Id.IntegerValue,Name=e.Name});
            if(box.Items.Count>0)box.SelectedIndex=0;
        }
        public void Show() {window.Show();window.Activate();}
        void Request(Action<UIApplication> action)
        {
            if(disposed || pending)return;pending=true;
            handler.Enqueue(a=>{pending=false;if(!disposed)action(a);});
            var result=external.Raise();
            if(result!=ExternalEventRequest.Accepted && result!=ExternalEventRequest.Pending) {handler.Clear();pending=false;window.Status("Revit rejected the request; try again when Revit is ready.",true);}
        }
        void PickSource(UIApplication a)
        {
            EnsureContext();window.Hide();
            try
            {
                var choices=ConnectorSelectionService.Pick(ui);
                if(choices.Count==0)throw new InvalidOperationException("No open round piping connector found.");
                window.Connectors.ItemsSource=choices;window.Connectors.SelectedIndex=0;source=choices[0];
                window.Diameter.Text=(source.Diameter*304.8).ToString("0.###");
                var owner=Document.GetElement(new ElementId(source.OwnerId));
                var pipe=owner as Pipe;
                if(pipe!=null)
                {
                    Select(window.Types,pipe.GetTypeId().IntegerValue);
                    var system=pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);if(system!=null)Select(window.Systems,system.AsElementId().IntegerValue);
                }
                if(owner.LevelId!=ElementId.InvalidElementId)Select(window.Levels,owner.LevelId.IntegerValue);
                else
                {
                    var nearest=new FilteredElementCollector(Document).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>Math.Abs(l.Elevation-source.Origin.Z)).FirstOrDefault();
                    if(nearest!=null)Select(window.Levels,nearest.Id.IntegerValue);
                }
                window.Status("Choose connector in list. Red marker = selected; gray markers = alternatives.");dirty=true;
            }
            catch(Autodesk.Revit.Exceptions.OperationCanceledException) {window.Status("Source selection cancelled.");}
            finally {window.Show();window.Activate();}
        }
        static void Select(System.Windows.Controls.ComboBox box,int id) {foreach(Option item in box.Items)if(item.Id==id){box.SelectedItem=item;return;}}
        void Start()
        {
            if(source==null) {window.Status("Select a source connector first.",true);return;}
            RoutingSettings chosen;if(!window.TrySettings(out chosen))return;
            bool project=window.Frame.SelectedIndex==0;
            Request(a=>
            {
                EnsureContext();settings=chosen;frame=new RoutingFrame(source.Direction,project);
                session.BeginSelection();session.Start(source.Origin,Math.Max(a.Application.ShortCurveTolerance,.1/12)+1e-7);
                input.Start();window.FreezeSetup();dirty=true;input.ActivateRevit();
            });
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
                menuOpen=true;savedPlane=plane;radial.Open(x,y);UpdateMenu();
            }
            else if(key==-17 && menuOpen)
            {
                int hit=radial.Hit(x,y);
                if(hit>=0) {angle=hit*45;session.Lock(frame.Direction(plane,angle));explicitLength=false;menuOpen=false;radial.Hide();dirty=true;}
                else CloseMenu();
            }
            else if(key==27) {if(menuOpen)CloseMenu();else {session.Escape();explicitLength=false;dirty=true;}}
            else if(key==8) {CloseMenu();session.Undo();explicitLength=false;dirty=true;}
            else if(key==13 && !menuOpen)Commit();
        }
        void OnMove(double x,double y)
        {
            if(menuOpen)
            {
                int hit=radial.Hit(x,y);if(hit!=radial.Menu.Selected){radial.Menu.Selected=hit;UpdateMenu();}return;
            }
            if(session.State!=RoutingState.SettingLength || explicitLength || projection==null || !projection.Contains(x,y))return;
            double length;
            if(!projection.TryLength(session.Origin,session.Direction,x,y,out length))
            {if(session.Length!=0){session.Length=0;dirty=true;}window.Status("Direction is nearly along the view. Enter exact length or change view/direction.",true);return;}
            if(Math.Abs(session.Length-length)>1e-6) {session.Length=length;dirty=true;}
        }
        string PlaneName=>window.Frame.SelectedIndex==0?new[]{"XY","XZ","YZ"}[plane]:new[]{"UV","UW","VW"}[plane];
        void UpdateMenu()
        {
            radial.Menu.Plane=PlaneName;radial.Menu.InvalidateVisual();
            if(radial.Menu.Selected>=0)hoverDirection=frame.Direction(plane,radial.Menu.Selected*45);dirty=true;
        }
        void CloseMenu()
        {
            if(!menuOpen)return;menuOpen=false;plane=savedPlane;radial.Hide();dirty=true;
        }
        void ExactLength()
        {
            if(session.State!=RoutingState.SettingLength){window.Status("Lock a direction first.",true);return;}
            double mm;if(!SettingsWindow.Number(window.Length.Text,out mm) || mm<=0){window.Status("Enter a positive length in mm.",true);return;}
            session.Length=mm/304.8;explicitLength=true;dirty=true;input.ActivateRevit();
        }
        void CustomAngle()
        {
            if(frame==null || pending || (session.State!=RoutingState.ChoosingDirection && session.State!=RoutingState.SettingLength))return;
            double degrees;if(!SettingsWindow.Number(window.Angle.Text,out degrees)){window.Status("Enter a finite angle.",true);return;}
            creationError=null;CloseMenu();angle=RadialHitTest.Normalize(degrees);session.Lock(frame.Direction(plane,angle));explicitLength=false;dirty=true;input.ActivateRevit();
        }
        void Commit()
        {
            if(pending || session.State!=RoutingState.SettingLength)return;
            string error=session.CommitSegment();if(error!=null)window.Status(error,true);else explicitLength=false;dirty=true;
        }
        void Finish()
        {
            if(frame==null || pending)return;
            if(session.State==RoutingState.SettingLength) {window.Status("Commit or discard the current preview before Finish.",true);return;}
            var error=session.ValidateAll();if(error!=null){window.Status(error,true);return;}
            creationError=null;CloseMenu();Request(a=>
            {
                EnsureContext();session.BeginCommit();
                try
                {
                    var ids=PlaceholderCreationService.Create(Document,session,settings,source);session.Complete();
                    ui.Selection.SetElementIds(ids);Dispose();
                    TaskDialog.Show("MEP Radial Routing","Created "+ids.Count+" pipe placeholders. Adjacent placeholders linked.\nSource connector is NOT connected. Conversion to real pipes / fitting feasibility has not been verified.");
                }
                catch(Exception ex) {if(session.State==RoutingState.Committing)session.CommitFailed();creationError="Creation failed; route retained. "+ex.Message;window.Status(creationError,true);dirty=true;}
            });
        }
        void OnIdling(object sender,IdlingEventArgs e)
        {
            if(disposed)return;
            if(closeRequested){Dispose();return;}
            try
            {
                viewAvailable=app.ActiveUIDocument!=null && app.ActiveUIDocument.Document.Equals(Document) && ui.ActiveView.Id.IntegerValue==viewId && ui.ActiveView is View3D && !((View3D)ui.ActiveView).IsPerspective;
                if(!viewAvailable){projection=null;CloseMenu();return;}
                var view=ui.ActiveView;var uv=ui.GetOpenUIViews().FirstOrDefault(v=>v.ViewId.IntegerValue==viewId);
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
                        window.Status(string.Format("{0} | {1} | {2:0.###}° | {3} segments\nL = {4:0.0} mm\n{5}",session.State,PlaneName,angle,session.Segments.Count,session.Length*304.8,problem??"Ready"),problem!=null);
                    }
                }
                if(renderer.LastError!=null)window.Status("Graphics error: "+renderer.LastError,true);
            }
            catch(Exception ex) {window.Status(ex.Message,true);Dispose();}
        }
        void BuildPreview()
        {
            var strokes=new List<PreviewRenderer.Stroke>();
            if(frame==null)
            {
                var list=window.Connectors.ItemsSource as IEnumerable<ConnectorChoice>;
                if(list!=null)foreach(var c in list)Marker(strokes,c.Origin,c==source);
            }
            else
            {
                foreach(var s in session.Segments)Tube(strokes,s.Start,s.End,settings.DiameterMm/609.6,false);
                Vec o=session.Origin;
                strokes.Add(new PreviewRenderer.Stroke(o,o+frame.U*2,235,70,70));strokes.Add(new PreviewRenderer.Stroke(o,o+frame.V*2,80,205,110));strokes.Add(new PreviewRenderer.Stroke(o,o+frame.W*2,85,150,245));
                Vec a=plane==2?frame.V:frame.U,b=plane==0?frame.V:frame.W;
                var p=new[]{o+a*.6+b*.6,o-a*.6+b*.6,o-a*.6-b*.6,o+a*.6-b*.6};
                for(int i=0;i<4;i++)strokes.Add(new PreviewRenderer.Stroke(p[i],p[(i+1)%4],110,110,130));
                if(menuOpen && radial.Menu.Selected>=0)Arrow(strokes,o,o+hoverDirection*3,false);
                else if(session.State==RoutingState.SettingLength)
                {
                    Vec end=o+session.Direction*session.Length;bool invalid=session.PreviewError!=null;
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
            app.Idling-=OnIdling;input?.Dispose();radial?.Close();window?.Close();renderer?.Dispose();external?.Dispose();
            if(session.State!=RoutingState.Completed)session.Cancel();
            if(Document.IsValidObject && app.ActiveUIDocument!=null && app.ActiveUIDocument.Document.Equals(Document))try{ui.RefreshActiveView();}catch(Autodesk.Revit.Exceptions.InvalidOperationException){}
            if(RoutingCommand.Current==this)RoutingCommand.Current=null;
        }
    }
}


