using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using MEP_Radial_Routing_ver1.Core;
using MEP_Radial_Routing_ver1.UI;

namespace MEP_Radial_Routing_ver1.Revit
{
    // PI remains native. Only an explicit Ctrl gesture requests a handoff.
    public sealed class NativeRoutingHost:IDisposable
    {
        readonly UIControlledApplication application;
        readonly RevitRequestHandler handler;
        readonly ExternalEvent external;
        readonly RevitCommandId pipeCommand;
        readonly DispatcherTimer handoffTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(3)};
        AddInCommandBinding binding;
        InputController input;
        List<ConnectorChoice> choices=new List<ConnectorChoice>();
        Projection projection;
        Document contextDocument;int contextView;
        ConnectorChoice latestNativeEnd;
        bool nativePipe,available,pending,disposed;
        int nativeEscapeCount;
        CtrlGesture gesture;
        public NativeRoutingHost(UIControlledApplication app)
        {
            application=app;
            handler=new RevitRequestHandler {Error=Report};external=ExternalEvent.Create(handler);
            pipeCommand=RevitCommandId.LookupPostableCommandId(PostableCommand.Pipe);
            try {binding=app.CreateAddInCommandBinding(pipeCommand);binding.BeforeExecuted+=BeforePipe;}
            catch(Exception ex) {RoutingLog.Write("Native Pipe observer",ex);}
            app.Idling+=Idling;
            app.ControlledApplication.DocumentChanged+=DocumentChanged;
            app.ControlledApplication.DocumentClosing+=DocumentClosing;
            app.ViewActivated+=ViewActivated;
            handoffTimer.Tick+=(s,e)=>
            {
                handoffTimer.Stop();if(!pending)return;
                handler.Clear();pending=false;gesture=null;ResetObservation();
                RoutingLog.Info("Ctrl handoff timed out; pending work discarded without further key messages.");
            };
            RoutingLog.Info("Loaded "+typeof(NativeRoutingHost).Assembly.GetName().Version+" / "+typeof(NativeRoutingHost).Assembly.Location);
        }
        public static bool Supported(UIDocument ui)=>ui!=null && !ui.Document.IsFamilyDocument && ui.ActiveView is View3D && !((View3D)ui.ActiveView).IsPerspective;
        void BeforePipe(object sender,BeforeExecutedEventArgs e)
        {
            var app=new UIApplication(e.ActiveDocument.Application);
            // Do not cancel or replace PI, including in unsupported radial views.
            if(!Supported(app.ActiveUIDocument)) {ResetObservation();return;}
            if(RoutingCommand.Current!=null)RoutingCommand.Current.Dispose();
            nativePipe=true;nativeEscapeCount=0;latestNativeEnd=null;
            Snapshot(app.ActiveUIDocument);EnsureInput(app.MainWindowHandle);
            RoutingLog.Info("PI started natively; no handoff until Ctrl.");
        }
        void Snapshot(UIDocument ui)
        {
            contextDocument=ui.Document;contextView=ui.ActiveView.Id.IntegerValue;
            projection=Capture(ui);
            choices=ui.Selection.GetElementIds().Select(ui.Document.GetElement).SelectMany(ConnectorSelectionService.Get).Where(ConnectorSelectionService.Valid).Select(ConnectorSelectionService.Snapshot).ToList();
            available=projection!=null && (nativePipe || choices.Count>0);
        }
        void EnsureInput(IntPtr handle)
        {
            if(input!=null)return;
            input=new InputController(handle);
            input.Enabled=()=>!disposed && RoutingCommand.Current==null && (available || (pending && gesture!=null));
            input.InViewport=(x,y)=>projection!=null && projection.Contains(x,y) && (nativePipe || pending || choices.Any(c=>ScreenDistance(projection,c.Origin,x,y)<=32*32));
            input.CanCommit=()=>false;input.CaptureRoutingKeys=false;
            input.Move=(x,y)=>{if(gesture!=null)gesture.Move(x,y);};
            input.Key=(key,x,y)=>
            {
                if(key==-17 && gesture!=null) {gesture.Release(x,y);return;}
                if(key!=17 || pending)return;
                var source=nativePipe?latestNativeEnd:null;
                if(source==null)source=choices.Where(c=>!nativePipe || ScreenDistance(projection,c.Origin,x,y)<=32*32).OrderBy(c=>ScreenDistance(projection,c.Origin,x,y)).FirstOrDefault();
                Queue(source,new CtrlGesture(x,y));
            };
            input.ObserveKey=(key,down)=>
            {
                if(!down || !nativePipe || pending)return;
                if(key==27) {latestNativeEnd=null;if(++nativeEscapeCount>=2)ResetObservation();}
                else nativeEscapeCount=0;
            };
            input.Error=Report;input.Start();
        }
        void Queue(ConnectorChoice source,CtrlGesture requestedGesture)
        {
            if(disposed || pending)return;
            bool endNative=nativePipe;
            if(endNative && (input==null || !input.TryEndNativeCommand())) {RoutingLog.Info("Ctrl handoff skipped: Revit is not foreground or cancellation could not be posted.");return;}
            var document=contextDocument;int view=contextView;
            gesture=requestedGesture;pending=true;available=false;
            handoffTimer.Start();
            RoutingLog.Info("Ctrl requested radial mode; end native="+endNative);
            handler.Enqueue(app=>
            {
                handoffTimer.Stop();
                var transfer=gesture;
                try
                {
                    if(disposed || !Supported(app.ActiveUIDocument) || !app.ActiveUIDocument.Document.Equals(document) || app.ActiveUIDocument.ActiveView.Id.IntegerValue!=view)return;
                    if(RoutingCommand.Current!=null)return;
                    if(input==null || !input.Focused) {RoutingLog.Info("Ctrl handoff discarded after focus loss.");return;}
                    if(source!=null)
                    {
                        var owner=document.GetElement(source.OwnerUniqueId);
                        var connector=owner==null?null:ConnectorSelectionService.Get(owner).FirstOrDefault(c=>c.Id==source.ConnectorId && ConnectorSelectionService.Valid(c));
                        source=connector==null?null:ConnectorSelectionService.Snapshot(connector);
                    }
                    input?.Dispose();input=null;nativePipe=false;latestNativeEnd=null;
                    var controller=new RoutingSessionController(app);RoutingCommand.Current=controller;
                    controller.BeginDirect(source,false);
                    // Native first-point preview is not exposed by Revit API. If a start
                    // had to be picked again, use a fresh Ctrl gesture after the pick.
                    if(source!=null)controller.AcceptCtrlGesture(transfer);
                }
                finally {pending=false;gesture=null;}
            });
            var raised=external.Raise();
            if(raised!=ExternalEventRequest.Accepted && raised!=ExternalEventRequest.Pending)
            {handoffTimer.Stop();handler.Clear();pending=false;gesture=null;available=false;Report(new InvalidOperationException("Revit could not switch to radial mode. Select a connector and hold Ctrl again."));}
        }
        void DocumentChanged(object sender,Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
        {
            if(!nativePipe || pending || RoutingCommand.Current!=null || contextDocument==null || !e.GetDocument().Equals(contextDocument))return;
            try
            {
                var app=new UIApplication(e.GetDocument().Application);var ui=app.ActiveUIDocument;
                if(!Supported(ui) || ui.ActiveView.Id.IntegerValue!=contextView)return;
                projection=Capture(ui);var cursor=InputController.Cursor;
                var pipe=e.GetAddedElementIds().Concat(e.GetModifiedElementIds()).Distinct().Select(e.GetDocument().GetElement).OfType<Autodesk.Revit.DB.Plumbing.Pipe>().Where(p=>!p.IsPlaceholder).OrderByDescending(p=>p.Id.IntegerValue).FirstOrDefault();
                if(pipe==null)return;
                nativeEscapeCount=0;
                latestNativeEnd=ConnectorSelectionService.Get(pipe).Where(ConnectorSelectionService.Valid).OrderBy(c=>ScreenDistance(projection,PreviewRenderer.FromXyz(c.Origin),cursor.X,cursor.Y)).Select(ConnectorSelectionService.Snapshot).FirstOrDefault();
                if(latestNativeEnd!=null) {PipeDefaults.Remember(e.GetDocument(),PipeDefaults.Resolve(e.GetDocument(),latestNativeEnd));RoutingLog.Info("Native Pipe end captured: "+latestNativeEnd.OwnerUniqueId);}
            }
            catch(Exception ex) {latestNativeEnd=null;RoutingLog.Write("Observe native pipe result",ex);}
        }
        void Idling(object sender,IdlingEventArgs args)
        {
            if(disposed || pending)return;
            try
            {
                var app=(UIApplication)sender;var ui=app.ActiveUIDocument;
                if(!Supported(ui) || RoutingCommand.Current!=null) {ResetObservation();return;}
                // Idling returns API control after native tools finish. A fresh PI event
                // arms native observation again; other tools never inherit that state.
                nativePipe=false;latestNativeEnd=null;
                Snapshot(ui);
                if(available)EnsureInput(app.MainWindowHandle);
                else {input?.Dispose();input=null;}
            }
            catch(Exception ex) {ResetObservation();RoutingLog.Write("Background connector detection",ex);}
        }
        void ResetObservation()
        {
            available=false;nativePipe=false;latestNativeEnd=null;
            if(!pending) {input?.Dispose();input=null;gesture=null;contextDocument=null;projection=null;choices.Clear();}
        }
        void DocumentClosing(object sender,Autodesk.Revit.DB.Events.DocumentClosingEventArgs e)
        {if(contextDocument!=null && contextDocument.Equals(e.Document)) {handoffTimer.Stop();handler.Clear();pending=false;ResetObservation();}}
        void ViewActivated(object sender,ViewActivatedEventArgs e) {if(!pending)ResetObservation();}
        public static Projection Capture(UIDocument ui)
        {
            var view=ui.ActiveView;var viewport=ui.GetOpenUIViews().FirstOrDefault(v=>v.ViewId==view.Id);
            if(viewport==null)return null;
            var rect=viewport.GetWindowRectangle();var corners=viewport.GetZoomCorners();
            var right=PreviewRenderer.FromXyz(view.RightDirection);var up=PreviewRenderer.FromXyz(view.UpDirection);var delta=PreviewRenderer.FromXyz(corners[1]-corners[0]);
            return new Projection {Center=PreviewRenderer.FromXyz((corners[0]+corners[1])*.5),Right=right,Up=up,Left=rect.Left,Top=rect.Top,Width=rect.Right-rect.Left,Height=rect.Bottom-rect.Top,ModelWidth=Math.Abs(delta.Dot(right)),ModelHeight=Math.Abs(delta.Dot(up))};
        }
        public static double ScreenDistance(Projection projection,Vec point,double x,double y)
        {
            double px,py;return projection!=null && projection.TryProject(point,out px,out py)?(px-x)*(px-x)+(py-y)*(py-y):double.MaxValue;
        }
        static void Report(Exception ex) {if(ex is Autodesk.Revit.Exceptions.OperationCanceledException)return;RoutingLog.Write("Routing",ex);TaskDialog.Show("Pipe Routing",ex.Message);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;application.Idling-=Idling;
            handoffTimer.Stop();
            application.ControlledApplication.DocumentChanged-=DocumentChanged;application.ControlledApplication.DocumentClosing-=DocumentClosing;application.ViewActivated-=ViewActivated;
            input?.Dispose();handler.Clear();external.Dispose();
            if(binding!=null) {binding.BeforeExecuted-=BeforePipe;application.RemoveAddInCommandBinding(pipeCommand);binding=null;}
        }
    }
    internal static class RoutingLog
    {
        internal static void Info(string message)
        {
            try
            {
                var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MEP_Radial_Routing");Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder,"routing.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine);
            }
            catch { /* Diagnostic IO must not interrupt routing. */ }
        }
        internal static void Write(string context,Exception error)
        {
            Info(context+Environment.NewLine+error);
            try {File.AppendAllText(Path.Combine(Path.GetTempPath(),"MEP_Radial_Routing.log"),DateTime.Now.ToString("s")+" "+context+Environment.NewLine+error+Environment.NewLine);}
            catch { /* Logging must not interrupt Revit. */ }
        }
    }
}
