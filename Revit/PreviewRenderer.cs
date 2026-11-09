using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using MEP_Radial_Routing_ver1.Core;

namespace MEP_Radial_Routing_ver1.Revit
{
    public sealed class PreviewRenderer : IDirectContext3DServer, IDisposable
    {
        public sealed class Stroke
        {
            public Vec A,B; public byte R,G,Bb;
            public Stroke(Vec a,Vec b,byte r,byte g,byte blue) { A=a;B=b;R=r;G=g;Bb=blue; }
        }
        readonly Guid id=Guid.NewGuid();
        readonly Document document; readonly int viewId;
        volatile Stroke[] strokes=new Stroke[0];
        MultiServerService service;
        public string LastError {get;private set;}
        public PreviewRenderer(Document doc,int view) { document=doc;viewId=view; }
        public void Register()
        {
            service=(MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
            service.AddServer(this); var ids=service.GetActiveServerIds(); ids.Add(id); service.SetActiveServers(ids);
        }
        public void Set(IEnumerable<Stroke> value) { strokes=value.ToArray(); }
        public Guid GetServerId()=>id;
        public ExternalServiceId GetServiceId()=>ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName()=>"MEP Radial Routing Preview";
        public string GetVendorId()=>"HLME";
        public string GetDescription()=>"Temporary routing geometry";
        public string GetApplicationId()=>"MEP_Radial_Routing_ver1";
        public string GetSourceId()=>"RouteSession";
        public bool UsesHandles()=>false;
        public bool UseInTransparentPass(View view)=>false;
        public bool CanExecute(View view)=>view.Document.Equals(document) && view.Id.IntegerValue==viewId && view is View3D && !((View3D)view).IsPerspective;
        public Outline GetBoundingBox(View view)
        {
            var data=strokes; if(data.Length==0)return null;
            var points=data.SelectMany(s=>new[]{s.A,s.B}).ToArray();
            return new Outline(new XYZ(points.Min(p=>p.X)-.01,points.Min(p=>p.Y)-.01,points.Min(p=>p.Z)-.01),new XYZ(points.Max(p=>p.X)+.01,points.Max(p=>p.Y)+.01,points.Max(p=>p.Z)+.01));
        }
        public void RenderScene(View view,DisplayStyle style)
        {
            var snapshot=strokes;
            // Keep each index buffer below the 16-bit vertex-index limit.
            for(int offset=0;offset<snapshot.Length;offset+=10000)
                RenderBatch(snapshot.Skip(offset).Take(10000).ToArray());
        }
        void RenderBatch(Stroke[] data)
        {
            try
            {
                using(var vb=new VertexBuffer(data.Length*2*VertexPositionColored.GetSizeInFloats()))
                using(var ib=new IndexBuffer(data.Length*IndexLine.GetSizeInShortInts()))
                using(var vf=new VertexFormat(VertexFormatBits.PositionColored))
                using(var effect=new EffectInstance(VertexFormatBits.PositionColored))
                {
                    vb.Map(data.Length*2*VertexPositionColored.GetSizeInFloats());
                    var vs=vb.GetVertexStreamPositionColored();
                    foreach(var s in data)
                    {
                        var c=new ColorWithTransparency(s.R,s.G,s.Bb,0);
                        vs.AddVertex(new VertexPositionColored(ToXyz(s.A),c)); vs.AddVertex(new VertexPositionColored(ToXyz(s.B),c));
                    }
                    vb.Unmap(); ib.Map(data.Length*IndexLine.GetSizeInShortInts()); var ix=ib.GetIndexStreamLine();
                    for(int i=0;i<data.Length;i++)ix.AddLine(new IndexLine(i*2,i*2+1));
                    ib.Unmap();
                    DrawContext.FlushBuffer(vb,data.Length*2,ib,data.Length*2,vf,effect,PrimitiveType.LineList,0,data.Length);
                }
            }
            catch(Exception ex) { LastError=ex.Message; }
        }
        public static XYZ ToXyz(Vec v)=>new XYZ(v.X,v.Y,v.Z);
        public static Vec FromXyz(XYZ v)=>new Vec(v.X,v.Y,v.Z);
        public void Dispose()
        {
            strokes=new Stroke[0];
            if(service!=null) { var ids=service.GetActiveServerIds(); ids.Remove(id); service.SetActiveServers(ids); service.RemoveServer(id);service=null; }
        }
    }
}

