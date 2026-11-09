using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
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
        public static Vec OutwardDirection(Connector connector)
        {
            var pipe=connector.Owner as Pipe;var curve=(pipe?.Location as LocationCurve)?.Curve;
            if(curve!=null)
            {
                bool first=connector.Origin.DistanceTo(curve.GetEndPoint(0))<=connector.Origin.DistanceTo(curve.GetEndPoint(1));
                var tangent=curve.ComputeDerivatives(first?0:1,true).BasisX;
                return PreviewRenderer.FromXyz(first?-tangent:tangent).Unit();
            }
            return PreviewRenderer.FromXyz(connector.CoordinateSystem.BasisZ).Unit();
        }
        public static ConnectorChoice Snapshot(Connector c) => new ConnectorChoice {
            OwnerId=c.Owner.Id.IntegerValue,OwnerUniqueId=c.Owner.UniqueId,ConnectorId=c.Id,
            Origin=PreviewRenderer.FromXyz(c.Origin),Direction=OutwardDirection(c),Diameter=c.Radius*2,
            Label="Connector #"+c.Id
        };
        public static IEnumerable<Connector> Get(Element e)
        {
            var manager=(e as MEPCurve)?.ConnectorManager ?? (e as FamilyInstance)?.MEPModel?.ConnectorManager;
            return manager==null?Enumerable.Empty<Connector>():manager.Connectors.Cast<Connector>();
        }
        public static bool Valid(Connector c)=>c.Domain==Domain.DomainPiping && c.ConnectorType==ConnectorType.End && c.Shape==ConnectorProfileType.Round && !c.IsConnected;
    }
    public sealed class RoutingSettings
    {
        public int SystemId,PipeTypeId,LevelId; public double DiameterMm;
    }
    public static class PipeDefaults
    {
        static readonly ConditionalWeakTable<Document,RoutingSettings> remembered=new ConditionalWeakTable<Document,RoutingSettings>();
        static RoutingSettings Copy(RoutingSettings value)=>new RoutingSettings {SystemId=value.SystemId,PipeTypeId=value.PipeTypeId,LevelId=value.LevelId,DiameterMm=value.DiameterMm};
        public static void Remember(Document doc,RoutingSettings settings) {remembered.Remove(doc);remembered.Add(doc,Copy(settings));}
        public static List<SystemOption> Systems(Document doc)=>new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().Select(s=>new SystemOption(s.Id.IntegerValue,s.Name,s.SystemClassification.ToString())).OrderBy(s=>s.Name,StringComparer.OrdinalIgnoreCase).ThenBy(s=>s.Id).ToList();
        public static RoutingSettings ResolveFree(Document doc,Vec point)
        {
            RoutingSettings previous;
            if(remembered.TryGetValue(doc,out previous) && doc.GetElement(new ElementId(previous.SystemId)) is PipingSystemType && doc.GetElement(new ElementId(previous.PipeTypeId)) is PipeType && doc.GetElement(new ElementId(previous.LevelId)) is Level)return Copy(previous);
            var pipe=new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>().Where(p=>!p.IsPlaceholder).OrderBy(p=>((LocationCurve)p.Location).Curve.Distance(PreviewRenderer.ToXyz(point))).FirstOrDefault();
            if(pipe==null)
            {
                var result=Resolve(doc,null);
                var level=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>Math.Abs(l.Elevation-point.Z)).FirstOrDefault();
                if(level!=null)result.LevelId=level.Id.IntegerValue;
                return result;
            }
            var connector=ConnectorSelectionService.Get(pipe).First(c=>c.ConnectorType==ConnectorType.End);
            return Resolve(doc,ConnectorSelectionService.Snapshot(connector));
        }
        public static RoutingSettings Resolve(Document doc,ConnectorChoice source)
        {
            var owner=source==null?null:doc.GetElement(source.OwnerUniqueId);
            var pipe=owner as Pipe;
            var type=pipe==null?doc.GetDefaultElementTypeId(ElementTypeGroup.PipeType):pipe.GetTypeId();
            if(!(doc.GetElement(type) is PipeType))type=new FilteredElementCollector(doc).OfClass(typeof(PipeType)).FirstElementId();
            var systems=new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().ToList();
            var system=pipe?.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
            if(!(doc.GetElement(system??ElementId.InvalidElementId) is PipingSystemType))
            {
                var connector=owner==null?null:ConnectorSelectionService.Get(owner).FirstOrDefault(c=>c.Id==source.ConnectorId);
                var choice=DefaultSystemSelection.Choose(systems.Select(s=>new SystemOption(s.Id.IntegerValue,s.Name,s.SystemClassification.ToString())),classification:connector==null?null:connector.PipeSystemType.ToString());
                if(choice==null)throw new InvalidOperationException(connector==null?"The project has no Piping System Type. Add a piping system type before drawing.":"No Piping System Type matches the source connector classification: "+connector.PipeSystemType+".");
                system=new ElementId(choice.Id);
            }
            var level=owner?.LevelId??ElementId.InvalidElementId;
            if(!(doc.GetElement(level) is Level))level=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>Math.Abs(l.Elevation-(source?.Origin.Z??0))).FirstOrDefault()?.Id;
            if(type==ElementId.InvalidElementId || system==null || level==null)throw new InvalidOperationException("The project needs a Pipe Type, Piping System Type and Level.");
            return new RoutingSettings {PipeTypeId=type.IntegerValue,SystemId=system.IntegerValue,LevelId=level.IntegerValue,DiameterMm=source==null?100:source.Diameter*304.8};
        }
    }
    public sealed class CreatedRoute
    {
        public ICollection<ElementId> Ids;public ConnectorChoice End;
    }
    public static class PipeCreationService
    {
        public static CreatedRoute Create(Document doc,RouteSession session,RoutingSettings settings,ConnectorChoice source)
        {
            var error=session.ValidateAll();if(error!=null)throw new InvalidOperationException(error);
            Connector start=ResolveSource(doc,session,source);
            var pipeType=doc.GetElement(new ElementId(settings.PipeTypeId)) as PipeType;
            if(pipeType==null || !(doc.GetElement(new ElementId(settings.LevelId)) is Level))throw new InvalidOperationException("Pipe Type or Level no longer exists.");
            var ids=new HashSet<ElementId>();ConnectorChoice end=null;int segmentIndex=0;
            using(var tx=new Transaction(doc,"Radial Pipe - create and connect"))
            {
                tx.Start();var failure=new PipeFailures();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failure).SetClearAfterRollback(true));
                try
                {
                    foreach(var segment in session.Segments)
                    {
                        segmentIndex++;
                        var previousOwner=start?.Owner;
                        var kind=start==null?ConnectionKind.Straight:ConnectionGeometry.Classify(ConnectorSelectionService.OutwardDirection(start),segment.End-segment.Start);
                        if(kind==ConnectionKind.Reverse)throw new InvalidOperationException("The direction goes back into the source pipe. Choose a forward or turning direction.");
                        bool needsElbow=kind==ConnectionKind.Elbow;
                        // Connector-based Pipe.Create may connect two angled pipes directly
                        // without inserting a fitting. Turns therefore use an explicit elbow.
                        var pipe=start==null || needsElbow
                            ?Pipe.Create(doc,new ElementId(settings.SystemId),pipeType.Id,new ElementId(settings.LevelId),PreviewRenderer.ToXyz(segment.Start),PreviewRenderer.ToXyz(segment.End))
                            :Pipe.Create(doc,pipeType.Id,new ElementId(settings.LevelId),start,PreviewRenderer.ToXyz(segment.End));
                        ids.Add(pipe.Id);
                        if((previousOwner==null || needsElbow) && !pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(settings.DiameterMm/304.8))throw new InvalidOperationException("Cannot set pipe diameter.");
                        doc.Regenerate();
                        if(needsElbow)
                        {
                            var newStart=pipe.ConnectorManager.Connectors.Cast<Connector>().Where(c=>c.ConnectorType==ConnectorType.End).OrderBy(c=>c.Origin.DistanceTo(PreviewRenderer.ToXyz(segment.Start))).First();
                            var elbow=doc.Create.NewElbowFitting(start,newStart);
                            if(!IsElbow(elbow))throw new InvalidOperationException("Revit did not create an Elbow fitting for this turn.");
                            ids.Add(elbow.Id);doc.Regenerate();
                            var connectors=ConnectorSelectionService.Get(elbow).ToList();
                            if(!connectors.Any(c=>c.IsConnectedTo(start)) || !connectors.Any(c=>c.IsConnectedTo(newStart)))throw new InvalidOperationException("Elbow does not connect both pipe ends.");
                            RoutingLog.Info("Elbow created: id="+elbow.Id.IntegerValue+", family="+elbow.Symbol.FamilyName);
                        }
                        if(previousOwner!=null && !ConnectedThroughFittings(doc,pipe,previousOwner,ids,needsElbow))throw new InvalidOperationException(needsElbow?"Turn has no connected Elbow fitting; rejected.":"Revit created an unconnected pipe; connection rejected.");
                        start=pipe.ConnectorManager.Connectors.Cast<Connector>().Where(ConnectorSelectionService.Valid).OrderBy(c=>c.Origin.DistanceTo(PreviewRenderer.ToXyz(segment.End))).FirstOrDefault();
                        if(start==null || start.Origin.DistanceTo(PreviewRenderer.ToXyz(segment.End))>1e-5)throw new InvalidOperationException("Cannot find the new pipe's free end.");
                        end=ConnectorSelectionService.Snapshot(start);
                    }
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException(failure.Message??"Revit rolled back the pipe connection.");
                    return new CreatedRoute {Ids=ids.ToList(),End=end};
                }
                catch(Exception ex)
                {
                    if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();
                    int elbows=pipeType.RoutingPreferenceManager.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
                    throw new InvalidOperationException("Cannot create/connect segment "+segmentIndex+". Pipe Type: "+pipeType.Name+", diameter "+settings.DiameterMm.ToString("0.##")+" mm, elbow rules: "+elbows+". "+(failure.Message??ex.Message)+" The attempted segment was rolled back.",ex);
                }
            }
        }
        static Connector ResolveSource(Document doc,RouteSession session,ConnectorChoice source)
        {
            if(source==null)return null;
            var owner=doc.GetElement(source.OwnerUniqueId);
            var start=owner==null?null:ConnectorSelectionService.Get(owner).FirstOrDefault(c=>c.Id==source.ConnectorId);
            if(start==null || !ConnectorSelectionService.Valid(start) || start.Origin.DistanceTo(PreviewRenderer.ToXyz(session.Source))>1e-6 || Math.Abs(start.Radius*2-source.Diameter)>1e-6 || ConnectorSelectionService.OutwardDirection(start).Dot(source.Direction)<1-1e-6)
                throw new InvalidOperationException("Source connector changed. Escape and select the connector again.");
            return start;
        }
        static bool IsElbow(Element element)=>((element as FamilyInstance)?.MEPModel as MechanicalFitting)?.PartType==PartType.Elbow;
        static IEnumerable<int> PhysicalNeighbours(Element element)
        {
            foreach(var connector in ConnectorSelectionService.Get(element).Where(c=>c.ConnectorType==ConnectorType.End && c.Domain==Domain.DomainPiping))
                foreach(Connector other in connector.AllRefs)
                {
                    if(other.ConnectorType==ConnectorType.Logical || other.Owner.Id==element.Id || !connector.IsConnectedTo(other))continue;
                    yield return other.Owner.Id.IntegerValue;
                }
        }
        static bool ConnectedThroughFittings(Document doc,Pipe pipe,Element target,HashSet<ElementId> ids,bool requireElbow)
        {
            var path=ConnectionGraph.FindPath(pipe.Id.IntegerValue,target.Id.IntegerValue,
                id=>PhysicalNeighbours(doc.GetElement(new ElementId(id))),
                id=>doc.GetElement(new ElementId(id))?.Category?.Id.IntegerValue==(int)BuiltInCategory.OST_PipeFitting,
                id=>IsElbow(doc.GetElement(new ElementId(id))),requireElbow);
            if(path==null)return false;
            for(int i=1;i<path.Count-1;i++)ids.Add(new ElementId(path[i]));
            return true;
        }
        sealed class PipeFailures:IFailuresPreprocessor
        {
            public string Message;
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                var errors=accessor.GetFailureMessages().Where(f=>f.GetSeverity()==FailureSeverity.Error).ToList();
                if(errors.Count==0)return FailureProcessingResult.Continue;
                Message=string.Join("; ",errors.Select(f=>f.GetDescriptionText()));return FailureProcessingResult.ProceedWithRollBack;
            }
        }
    }
}
