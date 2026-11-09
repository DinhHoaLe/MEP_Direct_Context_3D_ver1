using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using MEP_Radial_Routing_ver1.Core;

namespace MEP_Radial_Routing_ver1.Revit
{
    public sealed class ConnectorChoice
    {
        public int OwnerId,ConnectorId; public string OwnerUniqueId,Label; public Vec Origin,Direction; public double Diameter;
        public override string ToString()=>Label;
    }
    public static class ConnectorSelectionService
    {
        public static IEnumerable<Connector> Get(Element e)
        {
            var manager=(e as MEPCurve)?.ConnectorManager ?? (e as FamilyInstance)?.MEPModel?.ConnectorManager;
            return manager==null?Enumerable.Empty<Connector>():manager.Connectors.Cast<Connector>();
        }
        public static List<ConnectorChoice> Pick(UIDocument ui)
        {
            var reference=ui.Selection.PickObject(ObjectType.Element,new PipeSourceFilter(),"Select a pipe or equipment with an open round piping connector");
            var e=ui.Document.GetElement(reference);
            return Get(e).Where(Valid).OrderBy(c=>c.Id).Select(c=>new ConnectorChoice {
                OwnerId=e.Id.IntegerValue,OwnerUniqueId=e.UniqueId,ConnectorId=c.Id,
                Origin=PreviewRenderer.FromXyz(c.Origin),Direction=PreviewRenderer.FromXyz(c.CoordinateSystem.BasisZ),Diameter=c.Radius*2,
                Label=string.Format("#{0}  ({1:F0}, {2:F0}, {3:F0}) mm / Ø{4:F0}",c.Id,c.Origin.X*304.8,c.Origin.Y*304.8,c.Origin.Z*304.8,c.Radius*609.6)
            }).ToList();
        }
        public static bool Valid(Connector c)=>c.Domain==Domain.DomainPiping && c.ConnectorType==ConnectorType.End && c.Shape==ConnectorProfileType.Round && !c.IsConnected;
        sealed class PipeSourceFilter:ISelectionFilter
        {
            public bool AllowElement(Element e)=>Get(e).Any(Valid);
            public bool AllowReference(Reference r,XYZ p)=>false;
        }
    }
    public sealed class RoutingSettings
    {
        public int SystemId,PipeTypeId,LevelId; public double DiameterMm;
    }
    public static class PlaceholderCreationService
    {
        sealed class RollbackFailures:IFailuresPreprocessor
        {
            public string Message;
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                var failures=a.GetFailureMessages().Where(f=>f.GetSeverity()==FailureSeverity.Error).ToList();
                if(failures.Count==0)return FailureProcessingResult.Continue;
                Message=string.Join("; ",failures.Select(f=>f.GetDescriptionText())); return FailureProcessingResult.ProceedWithRollBack;
            }
        }
        public static ICollection<ElementId> Create(Document doc,RouteSession session,RoutingSettings settings,ConnectorChoice source)
        {
            string error=session.ValidateAll(); if(error!=null)throw new InvalidOperationException(error);
            var owner=doc.GetElement(source.OwnerUniqueId);
            var connector=owner==null?null:ConnectorSelectionService.Get(owner).FirstOrDefault(c=>c.Id==source.ConnectorId);
            if(connector==null || !ConnectorSelectionService.Valid(connector) || connector.Origin.DistanceTo(PreviewRenderer.ToXyz(session.Source))>1e-6)
                throw new InvalidOperationException("Source connector changed, was deleted or is now connected. Restart routing.");
            if(!(doc.GetElement(new ElementId(settings.SystemId)) is PipingSystemType) || !(doc.GetElement(new ElementId(settings.PipeTypeId)) is PipeType) || !(doc.GetElement(new ElementId(settings.LevelId)) is Level))throw new InvalidOperationException("Invalid pipe settings");
            if(settings.DiameterMm<=0 || double.IsNaN(settings.DiameterMm) || double.IsInfinity(settings.DiameterMm))throw new InvalidOperationException("Invalid diameter");
            var ids=new List<ElementId>();
            using(var tx=new Transaction(doc,"MEP Radial Routing - Pipe Placeholders"))
            {
                tx.Start(); var failures=new RollbackFailures();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                try
                {
                    foreach(var s in session.Segments)
                    {
                        XYZ a=PreviewRenderer.ToXyz(s.Start),b=PreviewRenderer.ToXyz(s.End);
                        if(!XYZ.IsWithinLengthLimits(a)||!XYZ.IsWithinLengthLimits(b))throw new InvalidOperationException("Point outside Revit design limits");
                        var pipe=Pipe.CreatePlaceholder(doc,new ElementId(settings.SystemId),new ElementId(settings.PipeTypeId),new ElementId(settings.LevelId),a,b);
                        if(!pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(settings.DiameterMm/304.8))throw new InvalidOperationException("Could not set diameter");
                        ids.Add(pipe.Id);
                    }
                    doc.Regenerate();
                    for(int i=1;i<ids.Count;i++)
                    {
                        Vec a=(session.Segments[i-1].End-session.Segments[i-1].Start).Unit(),b=(session.Segments[i].End-session.Segments[i].Start).Unit();
                        if(a.Dot(b)>1-1e-8)
                        {
                            var previous=(Pipe)doc.GetElement(ids[i-1]); var next=(Pipe)doc.GetElement(ids[i]);
                            var c1=previous.ConnectorManager.Connectors.Cast<Connector>().OrderBy(c=>c.Origin.DistanceTo(PreviewRenderer.ToXyz(session.Segments[i].Start))).First();
                            var c2=next.ConnectorManager.Connectors.Cast<Connector>().OrderBy(c=>c.Origin.DistanceTo(c1.Origin)).First(); c1.ConnectTo(c2);
                        }
                        else if(!PlumbingUtils.ConnectPipePlaceholdersAtElbow(doc,ids[i-1],ids[i]))throw new InvalidOperationException("Revit could not link adjacent placeholders at segment "+(i+1));
                    }
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException(failures.Message??"Transaction rolled back");
                    return ids;
                }
                catch { if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack(); throw; }
            }
        }
    }
}
