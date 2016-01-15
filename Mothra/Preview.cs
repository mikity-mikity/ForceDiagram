using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mikity.ghComponents
{
    public partial class ForceDiagram : Grasshopper.Kernel.GH_Component
    {
        public Rhino.Geometry.Transform zDown_eq = Rhino.Geometry.Transform.Translation(0, 0, 25d);
        public Rhino.Geometry.Transform zDown = Rhino.Geometry.Transform.Translation(0, 0, 15d);
        public Rhino.Geometry.Transform zScale = Rhino.Geometry.Transform.Scale(Rhino.Geometry.Plane.WorldXY, 1, 1, 1d);
        public override void DrawViewportWires(Grasshopper.Kernel.IGH_PreviewArgs args)
        {
            if (Hidden)
            {
                return;
            }
            if (listArrow != null)
            {
                args.Display.DrawLines(listArrow, System.Drawing.Color.Red);
            }
            //eigenvectors
            if (crossCyan != null)
            {
                args.Display.DrawLines(crossCyan, System.Drawing.Color.Cyan);
            }
            if (crossMagenta != null)
            {
                args.Display.DrawLines(crossMagenta, System.Drawing.Color.Magenta);
            }
            foreach (var leaf in listLeaf)
            {
                if (leaf.forceSrf != null)
                {
                    var srf = leaf.forceSrf.Duplicate() as Rhino.Geometry.NurbsSurface;
                    //srf.Transform(zScale);
                    args.Display.DrawSurface(srf, System.Drawing.Color.Aqua, 2);
                    if (leaf.tuples != null)
                    {
                        foreach (var tup in leaf.tuples)
                        {
                            var P = leaf.formSrf.PointAt(tup.u, tup.v);
                            var Q = leaf.forceSrf.PointAt(tup.u, tup.v);
                            Rhino.Geometry.Vector3d D = new Rhino.Geometry.Vector3d(Q.X, Q.Y, Q.Z);
                            D.Unitize();
                            D *= 0.3;
                            args.Display.DrawLine(new Rhino.Geometry.Line(P, P + D), System.Drawing.Color.Magenta);
                        }
                    }
                }
            }
            foreach (var leaf in listLeaf)
            {

                /*
                if (leaf.shellSrf != null)
                {
                    var srf = leaf.shellSrf.Duplicate() as Rhino.Geometry.NurbsSurface;
                    srf.Transform(zDown_eq);
                    args.Display.DrawSurface(srf, System.Drawing.Color.Brown, 3);
                }*/
            }
        }
    }
}
