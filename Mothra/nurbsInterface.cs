using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mikity.ghComponents
{
    public partial class ForceDiagram : Grasshopper.Kernel.GH_Component
    {
        void Nurbs2x(Rhino.Geometry.NurbsSurface srf, double[,] _x)
        {
            for (int i = 0; i < srf.Points.CountV; i++)
            {
                for (int j = 0; j < srf.Points.CountU; j++)
                {
                    _x[(i * srf.Points.CountU + j), 0] = srf.Points.GetControlPoint(j, i).Location.X;
                    _x[(i * srf.Points.CountU + j), 1] = srf.Points.GetControlPoint(j, i).Location.Y;
                    _x[(i * srf.Points.CountU + j), 2] = srf.Points.GetControlPoint(j, i).Location.Z;
                }
            }
        }

        void createNurbsElements(leaf leaf)
        {
            double[] uKnot;
            double[] vKnot;

            int N = leaf.nU * leaf.nV;
            int uDim = leaf.formSrf.OrderU;
            int vDim = leaf.formSrf.OrderV;
            int uDdim = leaf.formSrf.OrderU - 1;
            int vDdim = leaf.formSrf.OrderV - 1;


            uKnot = new double[leaf.nU - uDdim + 1 + uDdim * 2];
            vKnot = new double[leaf.nV - vDdim + 1 + vDdim * 2];
            for (int i = 0; i < uDdim; i++)
            {
                uKnot[i] = 0;
            }
            for (int i = 0; i < vDdim; i++)
            {
                vKnot[i] = 0;
            }
            for (int i = 0; i < leaf.nU - uDdim + 1; i++)
            {
                uKnot[i + uDdim] = i;
            }
            for (int i = 0; i < leaf.nV - vDdim + 1; i++)
            {
                vKnot[i + vDdim] = i;
            }
            for (int i = 0; i < uDdim; i++)
            {
                uKnot[i + leaf.nU + 1] = leaf.nU - uDdim;
            }
            for (int i = 0; i < vDdim; i++)
            {
                vKnot[i + leaf.nV + 1] = leaf.nV - vDdim;
            }
            leaf.myMasonry = new Minilla3D2.Objects.masonry();
            for (int j = 1; j < leaf.nV - vDdim + 1; j++)
            {
                for (int i = 1; i < leaf.nU - uDdim + 1; i++)
                {
                    int[] index = new int[uDim * vDim];
                    for (int k = 0; k < vDim; k++)
                    {
                        for (int l = 0; l < uDim; l++)
                        {
                            index[k * uDim + l] = (j - 1 + k) * leaf.nU + i - 1 + l;
                        }
                    }
                    leaf.myMasonry.elemList.Add(new Minilla3D2.Elements.nurbsElement(uDim, vDim, index, i, j, uKnot, vKnot));
                }
            }
        }
    }
}
