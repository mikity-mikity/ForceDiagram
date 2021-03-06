﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ShoNS.Array;
using Rhino.Geometry;
using Accord.Math;
namespace mikity.ghComponents
{
    
    class msgclass : mosek.Stream
    {
        string prefix;
        public msgclass(string prfx)
        {
            prefix = prfx;
        }

        public override void streamCB(string msg)
        {
            Console.Write("{0}{1}", prefix, msg);
        }
    }
    
    public partial class ForceDiagram : Grasshopper.Kernel.GH_Component
    {
        public void init(List<leaf> _listLeaf)
        {
            foreach (var leaf in _listLeaf)
            {
                double[,] X;
                double[,] _X;
                X = new double[leaf.nU * leaf.nV, 3];
                _X = new double[leaf.nU * leaf.nV, 3];
                Nurbs2x(leaf.formSrf, X);
                Nurbs2x(leaf.forceSrf, _X);
                leaf.myMasonry.setupRefNodesFromList(X);
                leaf.myMasonry.setupNodesFromList(_X);
                foreach (var tup in leaf.tuples)
                {
                    leaf.myMasonry.elemList[tup.index].precompute(tup);
                }
            }

        }
        public void computeForceDiagram(List<leaf> _listLeaf, List<node> _listNode)
        {
            //variable settings
            int numvar = _listNode.Count * 2;
            int numcon = 0;
            double infinity = 0;
            numcon += _listNode.Count;  //finite element matrix

            foreach (var leaf in _listLeaf)
            {
                leaf.conOffset = numcon;
                leaf.varOffset = numvar;
                numcon += leaf.tuples.Length * 1; //grad(detF)dx>-detF
            }
            mosek.boundkey[] bkx = new mosek.boundkey[numvar];
            double[] blx = new double[numvar];
            double[] bux = new double[numvar];
            for (int i = 0; i < _listNode.Count; i++)
            {
                if (_listNode[i].forceNodeType== node.type.fx)
                {
                    bkx[i * 2 + 0] = mosek.boundkey.fx;
                    blx[i * 2 + 0] = 0;// _listNode[i].X;
                    bux[i * 2 + 0] = 0;//_listNode[i].X;
                    bkx[i * 2 + 1] = mosek.boundkey.fx;
                    blx[i * 2 + 1] = 0;//_listNode[i].Y;
                    bux[i * 2 + 1] = 0;//_listNode[i].Y;
                }
                else
                {
                    bkx[i * 2 + 0] = mosek.boundkey.fr;
                    blx[i * 2 + 0] = -infinity;
                    bux[i * 2 + 0] = infinity;
                    bkx[i * 2 + 1] = mosek.boundkey.fr;
                    blx[i * 2 + 1] = -infinity;
                    bux[i * 2 + 1] = infinity;
                }
            }
            for (int t = 0; t < 10; t++)
            {
                init(_listLeaf);
                double[] x = new double[_listNode.Count * 2];
                foreach (var leaf in _listLeaf)
                {
                    for (int j = 0; j < leaf.nV; j++)
                    {
                        for (int i = 0; i < leaf.nU; i++)
                        {
                            int indexX = leaf.globalIndex[i + j * leaf.nU] * 2 + 0;
                            int indexY = leaf.globalIndex[i + j * leaf.nU] * 2 + 1;
                            var Q = leaf.forceSrf.Points.GetControlPoint(i, j);
                            x[indexX] = Q.Location.X;
                            x[indexY] = Q.Location.Y;
                        }
                    }

                }
                using (mosek.Env env = new mosek.Env())
                {
                    // Create a task object.
                    using (mosek.Task task = new mosek.Task(env, 0, 0))
                    {
                        // Directs the log task stream to the user specified
                        // method msgclass.streamCB
                        task.set_Stream(mosek.streamtype.log, new msgclass(""));
                        task.appendcons(numcon);
                        task.appendvars(numvar);
                        for (int j = 0; j < numvar; ++j)
                        {
                            task.putvarbound(j, bkx[j], blx[j], bux[j]);
                        }
                        
                        ShoNS.Array.SparseDoubleArray mat = new SparseDoubleArray(_listNode.Count, _listNode.Count * 2);
                        
                        
                        foreach (var leaf in _listLeaf)
                        {
                            foreach (var tup in leaf.tuples)
                            {
                                for(int i = 0; i < tup.nNode; i++)
                                {
                                    int indexI = leaf.globalIndex[tup.internalIndex[i]];
                                    for (int j = 0; j < tup.nNode; j++)
                                    {
                                        int indexJx = leaf.globalIndex[tup.internalIndex[j]] * 2 + 0;
                                        int indexJy = leaf.globalIndex[tup.internalIndex[j]] * 2 + 1;
                                        var d0 = tup.d0;
                                        var d1 = tup.d1;
                                        var G1 = tup.refGi[0];
                                        var G2 = tup.refGi[1];
                                        var val0 = (d0[j] * d1[0][i] * G1[1] + d0[j] * d1[1][i] * G2[1]) * tup.refDv * tup.area;
                                        var val1 = -(d0[j] * d1[0][i] * G1[0] + d0[j] * d1[1][i] * G2[0]) * tup.refDv * tup.area;
                                        mat[indexI, indexJx] += val0;
                                        mat[indexI, indexJy] += val1;
                                    }
                                }
                            }
                        }
                        
                        for (int i = 0; i < _listNode.Count; i++)
                        {
                            var val = 0d;
                            for (int j = 0; j < _listNode.Count * 2; j++)
                            {
                                val += mat[i, j] * x[j];
                            }
                            task.putconbound(i, mosek.boundkey.fx, -val, -val);
                            for (int j = 0; j < _listNode.Count*2; j++)
                            {
                                task.putaij(i, j, mat[i, j]);
                            }
                        }
                        
                        foreach (var leaf in _listLeaf)
                        {
                            int offsetC = 0;
                            foreach (var tup in leaf.tuples)
                            {
                                //compute current detF
                                //x,y; referece, X,Y; forceDiagram
                                var FXx = tup.gi[0][0] * tup.refGi[0][0] + tup.gi[1][0] * tup.refGi[1][0];
                                var FYy = tup.gi[0][1] * tup.refGi[0][1] + tup.gi[1][1] * tup.refGi[1][1];
                                var FXy = tup.gi[0][0] * tup.refGi[0][1] + tup.gi[1][0] * tup.refGi[1][1];
                                var FYx = tup.gi[0][1] * tup.refGi[0][0] + tup.gi[1][1] * tup.refGi[1][0];
                                var detF = FXx * FYy- FXy * FYx;
                                task.putconbound(leaf.conOffset + offsetC, mosek.boundkey.lo, -detF,infinity);
                                var G1 = tup.refGi[0];
                                var G2 = tup.refGi[1];
                                for (int i = 0; i < tup.nNode; i++)
                                {
                                    int indexX = leaf.globalIndex[tup.internalIndex[i]] * 2 + 0;
                                    int indexY = leaf.globalIndex[tup.internalIndex[i]] * 2 + 1;

                                    double val1 = (tup.d1[0][i] * G1[0] + tup.d1[1][i] * G2[0]) * FYy;
                                    double val2 = (tup.d1[0][i] * G1[1] + tup.d1[1][i] * G2[1]) * FYx;

                                    task.putaij(leaf.conOffset + offsetC, indexX, -val2);
                                    val1 = (tup.d1[0][i] * G1[1] + tup.d1[1][i] * G2[1]) * FXx;
                                    val2 = (tup.d1[0][i] * G1[0] + tup.d1[1][i] * G2[0]) * FXy;
                                    task.putaij(leaf.conOffset + offsetC, indexY, val1- val2);
                                }
                                offsetC ++;
                            }
                        }
                        for (int i = 0; i < _listNode.Count; i++)
                        {
                            task.putqobjij(i * 2 + 0, i * 2 + 0, 1);
                            task.putqobjij(i * 2 + 1, i * 2 + 1, 1);
                        }
                        task.optimize();
                        // Print a summary containing information
                        //   about the solution for debugging purposes
                        task.solutionsummary(mosek.streamtype.msg);

                        mosek.solsta solsta;
                        task.getsolsta(mosek.soltype.itr, out solsta);

                        double[] dx = new double[numvar];

                        task.getxx(mosek.soltype.itr, // Basic solution.     
                                        dx);

                        switch (solsta)
                        {
                            case mosek.solsta.optimal:
                                System.Windows.Forms.MessageBox.Show("Optimal primal solution\n");
                                break;
                            case mosek.solsta.near_optimal:
                                System.Windows.Forms.MessageBox.Show("Near Optimal primal solution\n");
                                break;
                            case mosek.solsta.dual_infeas_cer:
                                System.Windows.Forms.MessageBox.Show("Primal or dual infeasibility.\n");
                                break;
                            case mosek.solsta.prim_infeas_cer:
                                System.Windows.Forms.MessageBox.Show("Primal or dual infeasibility.\n");
                                break;
                            case mosek.solsta.near_dual_infeas_cer:
                                System.Windows.Forms.MessageBox.Show("Primal or dual infeasibility.\n");
                                break;
                            case mosek.solsta.near_prim_infeas_cer:
                                System.Windows.Forms.MessageBox.Show("Primal or dual infeasibility.\n");
                                break;
                            case mosek.solsta.unknown:
                                System.Windows.Forms.MessageBox.Show("Unknown solution status\n");
                                break;
                            default:
                                System.Windows.Forms.MessageBox.Show("Other solution status\n");
                                break;

                        }
                        foreach (var leaf in _listLeaf)
                        {
                            for (int j = 0; j < leaf.nV; j++)
                            {
                                for (int i = 0; i < leaf.nU; i++)
                                {
                                    int indexX = leaf.globalIndex[i + j * leaf.nU] * 2 + 0;
                                    int indexY = leaf.globalIndex[i + j * leaf.nU] * 2 + 1;
                                    var Q = leaf.forceSrf.Points.GetControlPoint(i, j);
                                    var P = new Point3d(Q.Location.X + dx[indexX] * 1d, Q.Location.Y + dx[indexY] * 1d, 0);
                                    leaf.forceSrf.Points.SetControlPoint(i, j, P);
                                }
                            }
                        }
                    }
                }
                ExpirePreview(true);
            }
        }
        /*         
        public void computeForceDiagram(List<leaf> _listLeaf,List<node> _listNode)
        {           //variable settings
                    ShoNS.Array.SparseDoubleArray mat = new SparseDoubleArray(_listNode.Count * 2, _listNode.Count * 2);
            ShoNS.Array.SparseDoubleArray F = new SparseDoubleArray(_listNode.Count * 2, 1);
            ShoNS.Array.SparseDoubleArray xx = new SparseDoubleArray(_listNode.Count * 2, 1);
            ShoNS.Array.SparseDoubleArray shift = new SparseDoubleArray(_listNode.Count * 2, _listNode.Count * 2);
            System.Windows.Forms.MessageBox.Show("1");
            for (int k = 0; k < _listNode.Count;k++ )
            {
                var node = _listNode[k];
                xx[k * 2 + 0, 0] = node.X;
                xx[k * 2 + 1, 0] = node.Y;
                if (node.forceNodeType == node.type.fx)
                {
                    var leaf = node.shareL[0];
                    var indexU = node.numberLU[0];
                    var indexV = node.numberLV[0];
                    node.x = leaf.forceSrf.Points.GetControlPoint(indexU, indexV).Location.X;
                    node.y = leaf.forceSrf.Points.GetControlPoint(indexU, indexV).Location.Y;
                    xx[k * 2 + 0, 0] = node.x;
                    xx[k * 2 + 1, 0] = node.y;
                }
            }
            
            List<int> series=new List<int>();
            List<Point3d> origin = new List<Point3d>();
            for(int i=0;i<_listNode.Count;i++)
            {
                series.Add(i);
            }
            int L1=0;
            int L2=_listNode.Count;
            for(int i=0;i<_listNode.Count;i++)
            {
                var node=_listNode[i];
                if(node.forceNodeType==node.type.fx)
                {
                    L2--;
                    series[i]=L2;
                }else{
                    series[i]=L1;
                    origin.Add(new Point3d(node.x, node.y, 0));
                    L1++;
                }
            }
            for (int i = 0; i < _listNode.Count; i++)
            {
                shift[i * 2 + 0, series[i] * 2 + 0] = 1;
                shift[i * 2 + 1, series[i] * 2 + 1] = 1;
            }
            System.Windows.Forms.MessageBox.Show("2");

            foreach (var leaf in _listLeaf)
            {
                foreach (var tup in leaf.tuples)
                {
                    //var det = tup.SPK[0, 0] * tup.SPK[1, 1] - tup.SPK[0, 1] * tup.SPK[0, 1];
                    //if (det > 0)
                    //{
                    for (int i = 0; i < tup.nNode; i++)
                    {
                        for (int j = 0; j < tup.nNode; j++)
                        {
                            //var f=new ShoNS.Array.Eigen()
                            var d0 = tup.d0;
                            var d1 = tup.d1;
                            var G1 = tup.Gi[0];
                            var G2 = tup.Gi[1];
                            //var val0 = d0[j] * (d1[0][i] * G1[0] + d1[1][i] * G2[0]);
                            //var val1 = -d0[j] * (d1[0][i] * G1[1] + d1[1][i] * G2[1]);
                            var val00 = (d1[0][i] * G1[0] + d1[1][i] * G2[0]) * (d1[0][j] * G1[0] + d1[1][j] * G2[0]);
                            var val10 = (d1[0][i] * G1[0] + d1[1][i] * G2[0]) * (d1[0][j] * G1[1] + d1[1][j] * G2[1]);
                            var val01 = (d1[0][i] * G1[1] + d1[1][i] * G2[1]) * (d1[0][j] * G1[0] + d1[1][j] * G2[0]);
                            var val11 = (d1[0][i] * G1[1] + d1[1][i] * G2[1]) * (d1[0][j] * G1[1] + d1[1][j] * G2[1]);
                            mat[leaf.globalIndex[tup.internalIndex[i]] * 2 + 0, leaf.globalIndex[tup.internalIndex[j]] * 2 + 0] += val00 * tup.refDv * tup.area;
                            mat[leaf.globalIndex[tup.internalIndex[i]] * 2 + 0, leaf.globalIndex[tup.internalIndex[j]] * 2 + 1] += val01 * tup.refDv * tup.area;
                            mat[leaf.globalIndex[tup.internalIndex[i]] * 2 + 1, leaf.globalIndex[tup.internalIndex[j]] * 2 + 0] += val10 * tup.refDv * tup.area;
                            mat[leaf.globalIndex[tup.internalIndex[i]] * 2 + 1, leaf.globalIndex[tup.internalIndex[j]] * 2 + 1] += val11 * tup.refDv * tup.area;
                            //val0 = d0[j] * (d1[0][i] * G1[1] + d1[1][i] * G2[1]);
                            //val1 = d0[j] * (d1[0][i] * G1[0] + d1[1][i] * G2[0]);
                            //mat[leaf.globalIndex[tup.internalIndex[i]] * 2 + 1, leaf.globalIndex[tup.internalIndex[j]] * 2 + 0] += val0 * tup.refDv * tup.area;
                            //mat[leaf.globalIndex[tup.internalIndex[i]] * 2 + 1, leaf.globalIndex[tup.internalIndex[j]] * 2 + 1] += val1 * tup.refDv * tup.area;
                        }
                    }
                    //}
                }
            }
            System.Windows.Forms.MessageBox.Show("3");
            var solve = new ShoNS.Array.Eigen(DoubleArray.From(mat), DoubleArray.Id(mat.GetLength(0), mat.GetLength(1)));
            var sol = solve.V as DoubleArray;

            //exsolsolve.V[]
            //System.Windows.Forms.MessageBox.Show(max1.ToString() + ".-." + max2.ToString());

            
            var exSol = new SparseDoubleArray(sol.GetLength(0),1);

            //var exSol = new SparseDoubleArray(sol.GetLength(0)+fx.GetLength(0),1);
            for (int i = 0; i < L1; i++)
            {
                exSol[i * 2 + 0, 0] = sol[i * 2 + 0, 0] * 50d;
                exSol[i * 2 + 1, 0] = sol[i * 2 + 1, 0] * 50d;
            }
            //exSol = shift.Multiply(exSol) as SparseDoubleArray;
            System.Windows.Forms.MessageBox.Show("4");

            foreach (var leaf in _listLeaf)
            {
                //leaf.shellSrf = leaf.forceSrf.Duplicate() as NurbsSurface;
                for (int i = 0; i < leaf.nU; i++)
                {
                    for (int j = 0; j < leaf.nV; j++)
                    {
                        var P = leaf.forceSrf.Points.GetControlPoint(i, j).Location;
                        //{
                        leaf.forceSrf.Points.SetControlPoint(i, j, new ControlPoint(exSol[leaf.globalIndex[i + j * leaf.nU] * 2 + 0, 0], exSol[leaf.globalIndex[i + j * leaf.nU] * 2 + 1, 0], 0));
                        //}
                        //if you don't want to allow movements of x and y coordinates, use the following instead of the above. 
                        //leaf.shellSrf.Points.SetControlPoint(i, j, new ControlPoint(P.X, P.Y, exSol[leaf.globalIndex[i + j * leaf.nU] * 3 + 2, 0]));
                    }
                }
            }

        }
*/
        /*
            public void mosek1(List<leaf> _listLeaf, List<branch> _listBranch, Dictionary<string, slice> _listSlice,Dictionary<string,range>_listRange,Dictionary<string,range>_listRangeOpen,Dictionary<string,range> _listRangeLeaf, bool obj, double allow)
            {
                // Since the value infinity is never used, we define
                // 'infinity' symbolic purposes only
                double infinity = 0;
                int[] csub = new int[3];// for cones
                int numvar = 0;
                int numcon = 0;
                foreach (var leaf in _listLeaf)
                {
                    leaf.varOffset = numvar;
                    leaf.conOffset = numcon;
                    numvar += (leaf.nU * leaf.nV) + leaf.r * 4;  //z,H11,H22,H12 mean curvature
                    numcon += leaf.r * 4;// H11,H22,H12
                    if (obj) numvar += leaf.r * 3; //z,target_z, z-_z
                    if (obj) numcon += leaf.r * 2; //z, z-target_z
                }

                foreach (var branch in _listBranch)
                {
                    branch.varOffset = numvar;
                    branch.conOffset = numcon;
                    numvar += branch.N + branch.tuples.Count(); //z,D
                    if (branch.branchType == branch.type.kink)
                    {
                        numcon += 2 * branch.N;//branch->left and right sides
                    }
                    else if (branch.branchType == branch.type.reinforce||branch.branchType==branch.type.open)
                    {
                        numcon += 1 * branch.N; //z=-ax-by-d
                        numcon += 1 * branch.N; //branch->edge(target) 
                    }
                    else//free
                    {
                        numcon += 1 * branch.N; //branch->edge(target)
                    }
                    numcon += branch.tuples.Count();// D(kink angle)
                }

                foreach (var slice in _listSlice.Values)
                {
                    slice.varOffset = numvar;
                    slice.conOffset = numcon;
                    numvar += 3;  //a,b,d
                    if (slice.sliceType == slice.type.fx)
                    {
                        numcon++;
                    }
                }

                if (obj)
                {
                    numvar++;
                }
                //variable settings
                mosek.boundkey[] bkx = new mosek.boundkey[numvar];
                double[] blx = new double[numvar];
                double[] bux = new double[numvar];
                foreach (var leaf in _listLeaf)
                {
                    //z
                    for (int i = 0; i < leaf.nU * leaf.nV; i++)
                    {
                        bkx[i + leaf.varOffset] = mosek.boundkey.fr;
                        blx[i + leaf.varOffset] = -infinity;
                        bux[i + leaf.varOffset] = infinity;
                    }
                    //H11,H22,H12
                    for (int i = 0; i < leaf.r; i++)
                    {
                        int n = i * 3 + (leaf.nU * leaf.nV);
                        bkx[n + leaf.varOffset] = mosek.boundkey.fr;
                        blx[n + leaf.varOffset] = -infinity;
                        bux[n + leaf.varOffset] = infinity;
                        bkx[n + 1 + leaf.varOffset] = mosek.boundkey.fr;
                        blx[n + 1 + leaf.varOffset] = -infinity;
                        bux[n + 1 + leaf.varOffset] = infinity;
                        bkx[n + 2 + leaf.varOffset] = mosek.boundkey.fr;
                        blx[n + 2 + leaf.varOffset] = -infinity;
                        bux[n + 2 + leaf.varOffset] = infinity;
                    }
                    //later mean curvature will be added here
                    //
                    for (int i = 0; i < leaf.r; i++)
                    {
                        int n = i + leaf.r*3+(leaf.nU * leaf.nV);
                        if (leaf.range.rangeType == range.type.lo)
                        {
                            bkx[n + leaf.varOffset] = mosek.boundkey.lo;
                            blx[n + leaf.varOffset] = leaf.range.lb;
                            bux[n + leaf.varOffset] = 0;
                        }
                        else if (leaf.range.rangeType == range.type.up)
                        {
                            bkx[n + leaf.varOffset] = mosek.boundkey.up;
                            blx[n + leaf.varOffset] = 0;
                            bux[n + leaf.varOffset] = leaf.range.ub;
                        }
                        else
                        {
                            bkx[n + leaf.varOffset] = mosek.boundkey.ra;
                            blx[n + leaf.varOffset] = leaf.range.lb;
                            bux[n + leaf.varOffset] = leaf.range.ub;
                        }

                    }

                    ////////////////
                    //target z
                    if (obj)
                    {
                        //z
                        for (int i = 0; i < leaf.r; i++)
                        {
                            bkx[i + (leaf.nU * leaf.nV) + 4 * leaf.r + leaf.varOffset] = mosek.boundkey.fr;
                            blx[i + (leaf.nU * leaf.nV) + 4 * leaf.r + leaf.varOffset] = 0;
                            bux[i + (leaf.nU * leaf.nV) + 4 * leaf.r + leaf.varOffset] = 0;
                        }
                        //target_z
                        for (int i = 0; i < leaf.r; i++)
                        {
                            bkx[i + (leaf.nU * leaf.nV) + 5 * leaf.r + leaf.varOffset] = mosek.boundkey.fx;
                            //reference multiquadric surface
                            blx[i + (leaf.nU * leaf.nV) + 5 * leaf.r + leaf.varOffset] = globalFunc(leaf.tuples[i].x, leaf.tuples[i].y);
                            bux[i + (leaf.nU * leaf.nV) + 5 * leaf.r + leaf.varOffset] = globalFunc(leaf.tuples[i].x, leaf.tuples[i].y);
                        }
                        //z-target_z
                        for (int i = 0; i < leaf.r; i++)
                        {
                            bkx[i + (leaf.nU * leaf.nV) + 6 * leaf.r + leaf.varOffset] = mosek.boundkey.fr;
                            blx[i + (leaf.nU * leaf.nV) + 6 * leaf.r + leaf.varOffset] = 0;
                            bux[i + (leaf.nU * leaf.nV) + 6 * leaf.r + leaf.varOffset] = 0;
                        }
                    }
                }
                foreach(var branch in _listBranch)
                {
                    if (branch.branchType == branch.type.reinforce )
                    {
                        for (int i = 0; i < branch.N; i++)
                        {
                            bkx[i + branch.varOffset] = mosek.boundkey.fr;
                            blx[i + branch.varOffset] = 0;
                            bux[i + branch.varOffset] = 0;
                        }
                        //kink angle parameter
                        for (int i = 0; i < branch.tuples.Count(); i++)
                        {
                            bkx[branch.N + i + branch.varOffset] = mosek.boundkey.lo;
                            blx[branch.N + i + branch.varOffset] = 0.0;
                            bux[branch.N + i + branch.varOffset] = 0;
                        }
                    }
                    else if (branch.branchType == branch.type.open)
                    {
                        for (int i = 0; i < branch.N; i++)
                        {
                            bkx[i + branch.varOffset] = mosek.boundkey.fr;
                            blx[i + branch.varOffset] = 0;
                            bux[i + branch.varOffset] = 0;
                        }
                        //kink angle parameter
                        for (int i = 0; i < branch.tuples.Count(); i++)
                        {
                            if (branch.range.rangeType == range.type.lo)
                            {
                                bkx[branch.N + i + branch.varOffset] = mosek.boundkey.lo;
                                blx[branch.N + i + branch.varOffset] = branch.range.lb;
                                bux[branch.N + i + branch.varOffset] = 0;
                            }
                            else if (branch.range.rangeType == range.type.up)
                            {
                                bkx[branch.N + i + branch.varOffset] = mosek.boundkey.up;
                                blx[branch.N + i + branch.varOffset] = 0;
                                bux[branch.N + i + branch.varOffset] = branch.range.ub;
                            }
                            else
                            {
                                bkx[branch.N + i + branch.varOffset] = mosek.boundkey.ra;
                                blx[branch.N + i + branch.varOffset] = branch.range.lb;
                                bux[branch.N + i + branch.varOffset] = branch.range.ub;
                            }
                            //bkx[branch.N + i + branch.varOffset] = mosek.boundkey.ra;
                            //blx[branch.N + i + branch.varOffset] = 0;
                            //bux[branch.N + i + branch.varOffset] = 0;
                        }
                    }
                    else if (branch.branchType == branch.type.kink)
                    {
                        for (int i = 0; i < branch.N; i++)
                        {
                            bkx[i + branch.varOffset] = mosek.boundkey.fr;
                            blx[i + branch.varOffset] = -infinity;
                            bux[i + branch.varOffset] = infinity;
                        }
                        //kink angle parameter
                        for (int i = 0; i < branch.tuples.Count(); i++)
                        {
                            if (branch.range.rangeType == range.type.lo)
                            {
                                bkx[branch.N + i + branch.varOffset] = mosek.boundkey.lo;
                                blx[branch.N + i + branch.varOffset] = branch.range.lb;
                                bux[branch.N + i + branch.varOffset] = 0;
                            }
                            else if(branch.range.rangeType == range.type.up)
                            {
                                bkx[branch.N + i + branch.varOffset] = mosek.boundkey.up;
                                blx[branch.N + i + branch.varOffset] = 0;
                                bux[branch.N + i + branch.varOffset] = branch.range.ub;
                            }
                            else
                            {
                                bkx[branch.N + i + branch.varOffset] = mosek.boundkey.ra;
                                blx[branch.N + i + branch.varOffset] = branch.range.lb;
                                bux[branch.N + i + branch.varOffset] = branch.range.ub;
                            }
                        }
                    }
                    else//free
                    {
                        for (int i = 0; i < branch.N; i++)
                        {
                            bkx[i + branch.varOffset] = mosek.boundkey.fr;
                            blx[i + branch.varOffset] = -infinity;
                            bux[i + branch.varOffset] = infinity;
                        }
                        //kink angle parameter
                        for (int i = 0; i < branch.tuples.Count(); i++)
                        {
                            bkx[branch.N + i + branch.varOffset] = mosek.boundkey.fr;
                            blx[branch.N + i + branch.varOffset] = -infinity;
                            bux[branch.N + i + branch.varOffset] = infinity;
                        }
                    }
                }
                foreach (var slice in _listSlice.Values)
                {
                    if (slice.sliceType == slice.type.fx)
                    {
                        //add something!
                        bkx[slice.varOffset] = mosek.boundkey.fx;
                        blx[slice.varOffset] = slice.a;
                        bux[slice.varOffset] = slice.a;
                        bkx[slice.varOffset + 1] = mosek.boundkey.fx;
                        blx[slice.varOffset + 1] = slice.b;
                        bux[slice.varOffset + 1] = slice.b;
                        bkx[slice.varOffset + 2] = mosek.boundkey.fx;
                        blx[slice.varOffset + 2] = slice.d;
                        bux[slice.varOffset + 2] = slice.d;
                    }
                    else
                    {
                        bkx[slice.varOffset] = mosek.boundkey.fr;
                        blx[slice.varOffset] = -infinity;
                        bux[slice.varOffset] = infinity;
                        bkx[slice.varOffset + 1] = mosek.boundkey.fr;
                        blx[slice.varOffset + 1] = -infinity;
                        bux[slice.varOffset + 1] = infinity;
                        bkx[slice.varOffset + 2] = mosek.boundkey.fr;
                        blx[slice.varOffset + 2] = -infinity;
                        bux[slice.varOffset + 2] = infinity;
                    }
                }
                if (obj)
                {
                    bkx[numvar - 1] = mosek.boundkey.fx;
                    blx[numvar - 1] = allow;
                    bux[numvar - 1] = allow;

                    //bkx[numvar - 1] = mosek.boundkey.fr;
                    //blx[numvar - 1] = -infinity;
                    //bux[numvar - 1] = infinity;
                }

                // Make mosek environment.
                using (mosek.Env env = new mosek.Env())
                {
                    // Create a task object.
                    using (mosek.Task task = new mosek.Task(env, 0, 0))
                    {
                        // Directs the log task stream to the user specified
                        // method msgclass.streamCB
                        task.set_Stream(mosek.streamtype.log, new msgclass(""));

                        task.appendcons(numcon);

                        task.appendvars(numvar);

                        for (int j = 0; j < numvar; ++j)
                        {
                            task.putvarbound(j, bkx[j], blx[j], bux[j]);
                        }
                        double root2 = Math.Sqrt(2);
                        foreach (var leaf in listLeaf)
                        {

                            double[] grad = new double[leaf.tuples[0].nNode];
                            double[] grad0 = new double[leaf.tuples[0].nNode];
                            double[] grad1i = new double[leaf.tuples[0].nNode];
                            double[] grad1j = new double[leaf.tuples[0].nNode];
                            //define H11,H12,H22
                            for (int i = 0; i < leaf.r; i++)
                            {
                                int N11 = i * 3; //condition number
                                int N22 = i * 3 + 1;
                                int N12 = i * 3 + 2;
                                int target = i * 3 + (leaf.nU * leaf.nV) + leaf.varOffset;   //variable number
                                task.putaij(N11+leaf.conOffset, target, -1);
                                task.putconbound(N11 + leaf.conOffset, mosek.boundkey.fx, 0, 0);
                                task.putaij(N22 + leaf.conOffset, target + 1, -1);
                                task.putconbound(N22 + leaf.conOffset, mosek.boundkey.fx, 0, 0);
                                task.putaij(N12 + leaf.conOffset, target + 2, -1);
                                task.putconbound(N12 + leaf.conOffset, mosek.boundkey.fx, 0, 0);
                                //N11
                                leaf.tuples[i].d2[0, 0].CopyTo(grad, 0);
                                leaf.tuples[i].d0.CopyTo(grad0, 0);
                                leaf.tuples[i].d1[0].CopyTo(grad1i, 0);
                                leaf.tuples[i].d1[0].CopyTo(grad1j, 0);
                                for (int k = 0; k < leaf.tuples[i].nNode; k++)
                                {
                                    for (int j = 0; j < leaf.tuples[i].elemDim; j++)
                                    {
                                        grad[k] -= leaf.tuples[i].Gammaijk[0, 0, j] * leaf.tuples[i].d1[j][k];
                                    }
                                    double val = 0;
                                    val += grad[k];
                                    task.putaij(N11 + leaf.conOffset, leaf.tuples[i].internalIndex[k] + leaf.varOffset, -val / root2);
                                }
                                //N22
                                leaf.tuples[i].d2[1, 1].CopyTo(grad, 0);
                                leaf.tuples[i].d0.CopyTo(grad0, 0);
                                leaf.tuples[i].d1[1].CopyTo(grad1i, 0);
                                leaf.tuples[i].d1[1].CopyTo(grad1j, 0);
                                for (int k = 0; k < leaf.tuples[i].nNode; k++)
                                {
                                    for (int j = 0; j < leaf.tuples[i].elemDim; j++)
                                    {
                                        grad[k] -= leaf.tuples[i].Gammaijk[1, 1, j] * leaf.tuples[i].d1[j][k];
                                    }
                                    double val = 0;
                                    val += grad[k];
                                    task.putaij(N22 + leaf.conOffset, leaf.tuples[i].internalIndex[k] + leaf.varOffset, -val / root2);
                                }
                                //N12
                                leaf.tuples[i].d2[0, 1].CopyTo(grad, 0);
                                leaf.tuples[i].d0.CopyTo(grad0, 0);
                                leaf.tuples[i].d1[0].CopyTo(grad1i, 0);
                                leaf.tuples[i].d1[1].CopyTo(grad1j, 0);
                                for (int k = 0; k < leaf.tuples[i].nNode; k++)
                                {
                                    for (int j = 0; j < leaf.tuples[i].elemDim; j++)
                                    {
                                        grad[k] -= leaf.tuples[i].Gammaijk[0, 1, j] * leaf.tuples[i].d1[j][k];
                                    }
                                    double val = 0;
                                    val += grad[k];
                                    task.putaij(N12 + leaf.conOffset, leaf.tuples[i].internalIndex[k] + leaf.varOffset, -val);
                                }
                            }
                            // mean curvature will be added here
                            //
                            //
                            for (int i = 0; i < leaf.r; i++)
                            {
                                int target = i * 3 + (leaf.nU * leaf.nV) + leaf.varOffset;   //variable number
                                int target2 = i + leaf.r*3+(leaf.nU * leaf.nV) + leaf.varOffset;   //variable number

                                int NH= leaf.r*3+i; //condition number
                                task.putaij(NH + leaf.conOffset, target, 1);
                                task.putaij(NH + leaf.conOffset, target + 1, 1);
                                task.putaij(NH + leaf.conOffset, target2, -1);
                                task.putconbound(NH+leaf.conOffset, mosek.boundkey.fx, 0, 0);

                            }

                            //if (leaf.leafType == leaf.type.convex)
                            for (int i = 0; i < leaf.r; i++)
                            {
                                int N11 = i * 3 + (leaf.nU * leaf.nV); //variable number
                                int N22 = i * 3 + 1 + (leaf.nU * leaf.nV);
                                int N12 = i * 3 + 2 + (leaf.nU * leaf.nV);

                                csub[0] = N11 + leaf.varOffset;
                                csub[1] = N22 + leaf.varOffset;
                                csub[2] = N12 + leaf.varOffset;
                                task.appendcone(mosek.conetype.rquad,
                                                0.0, // For future use only, can be set to 0.0 
                                                csub);
                            }
                            if (obj)
                            {
                                double[] grad00 = new double[leaf.tuples[0].nNode];
                                for (int i = 0; i < leaf.r; i++)
                                {
                                    leaf.tuples[i].d0.CopyTo(grad00, 0);
                                    for (int k = 0; k < leaf.tuples[i].nNode; k++)
                                    {
                                        task.putaij(leaf.conOffset + leaf.r * 4 + i, leaf.varOffset + leaf.tuples[i].internalIndex[k], grad00[k]);
                                    }
                                    task.putaij(leaf.conOffset + leaf.r * 4 + i, leaf.varOffset + leaf.nU*leaf.nV+leaf.r*4+i,-1);
                                    task.putconbound(leaf.conOffset + leaf.r * 4 + i, mosek.boundkey.fx, 0, 0);
                                }
                                for (int i = 0; i < leaf.tuples.Count(); i++)
                                {
                                    task.putaij(leaf.conOffset + leaf.r * 5 + i, leaf.varOffset + leaf.nU * leaf.nV + leaf.r * 4 + i, 1);
                                    task.putaij(leaf.conOffset + leaf.r * 5 + i, leaf.varOffset + leaf.nU * leaf.nV + leaf.r * 5 + i, -1);
                                    task.putaij(leaf.conOffset + leaf.r * 5 + i, leaf.varOffset + leaf.nU * leaf.nV + leaf.r * 6 + i, -1);
                                    task.putconbound(leaf.conOffset + leaf.r * 5 + i, mosek.boundkey.fx, 0, 0);
                                }
                            }
                        }

                        if (obj)
                        {
                            List<int> dsub=new List<int>();
                            dsub.Add(numvar-1);
                            foreach (var leaf in _listLeaf)
                            {
                                for (int i = 0; i < leaf.r; i++)
                                {
                                    dsub.Add(leaf.varOffset + leaf.nU * leaf.nV + leaf.r * 6 + i);
                                }
                            }
                            task.appendcone(mosek.conetype.quad, 0.0, dsub.ToArray());
                        }
                        foreach (var branch in _listBranch)
                        {
                            if (branch.branchType == branch.type.kink)
                            {
                                tieBranchD1(branch, branch.left, task, 2, 0);
                                tieBranchD1(branch, branch.right, task, 2, 1);
                                defineKinkAngle2(branch,branch.left,branch.right,task, branch.conOffset + branch.N*2, branch.varOffset + branch.N);
                            }
                            else if (branch.branchType == branch.type.reinforce || branch.branchType == branch.type.open)
                            {
                                int iA = _listSlice[branch.sliceKey].varOffset;
                                int iB = _listSlice[branch.sliceKey].varOffset + 1;
                                int iD = _listSlice[branch.sliceKey].varOffset + 2;
                                //height parameter
                                for (int i = 0; i < branch.N; i++)
                                {
                                    double x = branch.crv.Points[i].Location.X;
                                    double y = branch.crv.Points[i].Location.Y;
                                    task.putconbound(branch.conOffset + branch.N + branch.tuples.Count() + i, mosek.boundkey.fx, 0, 0);
                                    task.putaij(branch.conOffset + branch.N + branch.tuples.Count() + i, branch.varOffset + i, 1);//z
                                    task.putaij(branch.conOffset + branch.N + branch.tuples.Count() + i, iA, x);//ax
                                    task.putaij(branch.conOffset + branch.N + branch.tuples.Count() + i, iB, y);//by
                                    task.putaij(branch.conOffset + branch.N + branch.tuples.Count() + i, iD, 1);//d
                                }
                                tieBranchD1(branch, branch.target, task, 1, 0);
                                defineKinkAngleC(branch, branch.target, task, branch.conOffset + branch.N, branch.varOffset + branch.N);
                            }
                            else
                            {
                                tieBranchD1(branch, branch.target, task, 1, 0);
                                defineKinkAngle(branch,branch.target, task, branch.conOffset + branch.N, branch.varOffset + branch.N);
                            }
                        }
                        //task.putcj(numvar - 1, 1);
                        task.putintparam(mosek.iparam.intpnt_max_iterations, 200000000);//20000000
                        task.putintparam(mosek.iparam.intpnt_solve_form, mosek.solveform.dual);
                        task.putobjsense(mosek.objsense.minimize);
                        //task.writedata("c:/out/mosek_task_dump.opf");


                        task.optimize();
                        // Print a summary containing information
                        //   about the solution for debugging purposes
                        task.solutionsummary(mosek.streamtype.msg);

                        mosek.solsta solsta;
                        task.getsolsta(mosek.soltype.itr, out solsta);

                        double[] xx = new double[numvar];

                        task.getxx(mosek.soltype.itr, // Basic solution.     
                                        xx);

                        switch (solsta)
                        {
                            case mosek.solsta.optimal:
                                System.Windows.Forms.MessageBox.Show("Optimal primal solution\n");
                                break;
                            case mosek.solsta.near_optimal:
                                System.Windows.Forms.MessageBox.Show("Near Optimal primal solution\n");
                                break;
                            case mosek.solsta.dual_infeas_cer:
                            case mosek.solsta.prim_infeas_cer:
                            case mosek.solsta.near_dual_infeas_cer:
                            case mosek.solsta.near_prim_infeas_cer:
                                Console.WriteLine("Primal or dual infeasibility.\n");
                                break;
                            case mosek.solsta.unknown:
                                System.Windows.Forms.MessageBox.Show("Unknown solution status\n");
                                break;
                            default:
                                System.Windows.Forms.MessageBox.Show("Other solution status\n");
                                break;

                        }
                        //store airy potential
                        System.Windows.Forms.MessageBox.Show(string.Format("error={0}", xx[numvar - 1]));
                        foreach (var leaf in listLeaf)
                        {
                            double[] x = new double[leaf.nU * leaf.nV];
                            for (int j = 0; j < leaf.nV; j++)
                            {
                                for (int i = 0; i < leaf.nU; i++)
                                {
                                    x[i + j * leaf.nU] = xx[i + j * leaf.nU + leaf.varOffset];
                                }
                            }
                            leaf.myMasonry.setupAiryPotentialFromList(x);
                        }
                        foreach (var leaf in listLeaf)
                        {
                            foreach (var tup in leaf.tuples)
                            {
                                leaf.myMasonry.elemList[tup.index].computeStressFunction(tup);
                            }
                        }
                        foreach (var branch in _listBranch)
                        {
                            double[] x = new double[branch.N];
                            for (int i = 0; i < branch.N; i++)
                            {
                                x[i] = xx[i + branch.varOffset];
                            }
                            branch.myArch.setupAiryPotentialFromList(x);
                        }
                        foreach (var slice in _listSlice.Values)
                        {
                            slice.a = xx[slice.varOffset];
                            slice.b = xx[slice.varOffset + 1];
                            slice.d = xx[slice.varOffset + 2];
                            double norm = Math.Sqrt(slice.a * slice.a + slice.b * slice.b + 1);
                            var pl = new Rhino.Geometry.Plane(slice.a, slice.b, 1d, slice.d / norm);
                            slice.update(pl);
                        }
                        foreach (var branch in listBranch)
                        {
                            foreach (var tup in branch.tuples)
                            {
                                if (branch.branchType == branch.type.kink)
                                {
                                    branch.left.myMasonry.elemList[tup.left.index].computeTangent(tup.left);
                                    branch.right.myMasonry.elemList[tup.right.index].computeTangent(tup.right);
                                }
                                else if (branch.branchType == branch.type.fix)
                                {
                                    branch.target.myMasonry.elemList[tup.target.index].computeTangent(tup.target);
                                }
                                else
                                {

                                    branch.target.myMasonry.elemList[tup.target.index].computeTangent(tup.target);
                                    var vars = branch.slice.pl.GetPlaneEquation();
                                    branch.target.myMasonry.elemList[tup.target.index].computeTangent(tup.target, vars[0], vars[1], vars[2], vars[3]); //valDc
                                }
                            }
                        }
                        foreach (var leaf in _listLeaf)
                        {
                            for (int i = 0; i < leaf.r; i++)
                            {
                                int target = i + leaf.r*3+(leaf.nU * leaf.nV) + leaf.varOffset;
                                leaf.tuples[i].NH = xx[target];
                            }
                        }
                        foreach (var branch in _listBranch)
                        {
                            branch.airyCrv = branch.crv.Duplicate() as NurbsCurve;
                            for (int j = 0; j < branch.N; j++)
                            {
                                var P = branch.crv.Points[j];
                                branch.airyCrv.Points.SetPoint(j, new Point3d(P.Location.X, P.Location.Y, xx[j + branch.varOffset]));
                            }
                            for (int i = 0; i < branch.tuples.Count(); i++)
                            {
                                //branch.tuples[i].z = branch.airyCrv.PointAt(branch.tuples[i].t).Z;
                                //int D = i + branch.N;
                                if (branch.branchType == branch.type.open)
                                {
                                    branch.tuples[i].H[0, 0] = branch.tuples[i].target.valD - branch.tuples[i].target.valDc;
                                }
                                else if (branch.branchType == branch.type.reinforce)
                                {
                                    branch.tuples[i].H[0, 0] = branch.tuples[i].target.valD - branch.tuples[i].target.valDc;
                                }
                                else if (branch.branchType == branch.type.fix)
                                {
                                    branch.tuples[i].H[0, 0] = 0;
                                }
                                else
                                {
                                    //afterwards, check why these two values do not match.
                                    //branch.tuples[i].H[0, 0] = branch.tuples[i].left.valD + branch.tuples[i].right.valD;
                                    branch.tuples[i].H[0, 0] = xx[branch.N + i + branch.varOffset];
                                }
                            }
                        }
                        foreach (var range in _listRangeLeaf.Values)
                        {
                            double min = 10000d, max = -10000d;
                            foreach (var leaf in range.lL)
                            {
                                for (int i = 0; i < leaf.tuples.Count(); i++)
                                {
                                    if (leaf.tuples[i].NH > max) max = leaf.tuples[i].NH;
                                    if (leaf.tuples[i].NH < min) min = leaf.tuples[i].NH;
                                }
                            }
                            range.lastMin = min;
                            range.lastMax = max;
                            range.firstPathDone = true;
                        }
                        foreach (var range in _listRange.Values)
                        {
                            double min=10000d,max=-10000d;
                            foreach (var branch in range.lB)
                            {
                                for(int i=0;i<branch.tuples.Count();i++)
                                {
                                    if (branch.tuples[i].H[0, 0] > max) max = branch.tuples[i].H[0, 0];
                                    if (branch.tuples[i].H[0, 0] < min) min = branch.tuples[i].H[0, 0];
                                }
                            }
                            range.lastMin = min;
                            range.lastMax = max;
                            range.firstPathDone = true;
                        }
                        foreach (var range in _listRangeOpen.Values)
                        {
                            double min = 10000d, max = -10000d;
                            foreach (var branch in range.lB)
                            {
                                for (int i = 0; i < branch.tuples.Count(); i++)
                                {
                                    if (branch.tuples[i].H[0, 0] > max) max = branch.tuples[i].H[0, 0];
                                    if (branch.tuples[i].H[0, 0] < min) min = branch.tuples[i].H[0, 0];
                                }
                            }
                            range.lastMin = min;
                            range.lastMax = max;
                            range.firstPathDone = true;
                        }

                        foreach (var leaf in _listLeaf)
                        {
                            leaf.airySrf = leaf.srf.Duplicate() as NurbsSurface;
                            for (int j = 0; j < leaf.nV; j++)
                            {
                                for (int i = 0; i < leaf.nU; i++)
                                {
                                    var P = leaf.srf.Points.GetControlPoint(i, j);
                                    leaf.airySrf.Points.SetControlPoint(i, j, new ControlPoint(P.Location.X, P.Location.Y, xx[i + j * leaf.nU + leaf.varOffset]));
                                }
                            }
                        }
                    }
                }
            }
            void hodgeStar(List<leaf> _listLeaf, List<branch> _listBranch, Func<double, double> coeff,double sScale)
            {
                foreach (var branch in _listBranch)
                {
                    for (int i = 0; i < branch.tuples.Count(); i++)
                    {
                        double g = branch.tuples[i].gij[0, 0];
                        double val = coeff(g);
                        branch.tuples[i].SPK[0, 0] = branch.tuples[i].H[0, 0] * val*sScale;
                        if(branch.tuples[i].SPK[0,0]<0)
                        {
                            branch.tuples[i].SPK[0, 0] = 0d;
                        }
                    }
                }
                foreach (var leaf in _listLeaf)
                {
                    for (int j = 0; j < leaf.r; j++)
                    {
                        //Hodge star
                        double g = leaf.tuples[j].refDv * leaf.tuples[j].refDv;

                        leaf.tuples[j].SPK[0, 0] = leaf.tuples[j].H[1, 1] / g;
                        leaf.tuples[j].SPK[1, 1] = leaf.tuples[j].H[0, 0] / g;
                        leaf.tuples[j].SPK[0, 1] = -leaf.tuples[j].H[0, 1] / g;
                        leaf.tuples[j].SPK[1, 0] = -leaf.tuples[j].H[1, 0] / g;
                        leaf.tuples[j].computeEigenVectors();
                        var tup = leaf.tuples[j];
                        var det = tup.SPK[0, 0] * tup.SPK[1, 1] - tup.SPK[0, 1] * tup.SPK[1, 0];
                        if (tup.eigenValues[0] < 0 || tup.eigenValues[1] < 0)
                        {
                            if (tup.eigenValues[0] < 0) tup.eigenValues[0] = 0;
                            if (tup.eigenValues[1] < 0) tup.eigenValues[1] = 0;
                            //P
                            double A11 = tup.eigenVectorsB[0][0];
                            double A12 = tup.eigenVectorsB[0][1];
                            double A21 = tup.eigenVectorsB[1][0];
                            double A22 = tup.eigenVectorsB[1][1];
                            double det2 = A11 * A22 - A12 * A21;
                            //P^-1
                            double B11 = A22 / det2;
                            double B22 = A11 / det2;
                            double B12 = -A12 / det2;
                            double B21 = -A21 / det2;
                            double C11 = B11 * tup.eigenValues[0];
                            double C12 = B12 * tup.eigenValues[1];
                            double C21 = B21 * tup.eigenValues[0];
                            double C22 = B22 * tup.eigenValues[1];
                            double D11 = C11 * A11 + C12 * A21;
                            double D12 = C11 * A12 + C12 * A22;
                            double D21 = C21 * A11 + C22 * A21;
                            double D22 = C21 * A12 + C22 * A22;
                            tup.SPK[0, 0] = D11 * tup.Gij[0, 0] + D12 * tup.Gij[1, 0];
                            tup.SPK[0, 1] = D11 * tup.Gij[0, 1] + D12 * tup.Gij[1, 1];
                            tup.SPK[1, 0] = D21 * tup.Gij[0, 0] + D22 * tup.Gij[1, 0];
                            tup.SPK[1, 1] = D21 * tup.Gij[1, 0] + D22 * tup.Gij[1, 1];
                        }
                        tup.SPK[0, 0] *= sScale;
                        tup.SPK[1, 0] *= sScale;
                        tup.SPK[0, 1] *= sScale;
                        tup.SPK[1, 1] *= sScale;
                    }
                }
                //For visualization
                crossMagenta.Clear();
                crossCyan.Clear();
                foreach (var leaf in listLeaf)
                {
                    foreach (var tuple in leaf.tuples)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            if (tuple.eigenValues[i] < 0)
                            {
                                double s = tuple.eigenValues[i]*sScale;
                                //double s = 0.1;
                                Point3d S = new Point3d(tuple.x - tuple.eigenVectors[i][0] * s, tuple.y - tuple.eigenVectors[i][1] * s, tuple.z - tuple.eigenVectors[i][2] * s);
                                Point3d E = new Point3d(tuple.x + tuple.eigenVectors[i][0] * s, tuple.y + tuple.eigenVectors[i][1] * s, tuple.z + tuple.eigenVectors[i][2] * s);
                                Line line = new Line(S, E);
                                line.Transform(zDown);
                                crossCyan.Add(line);
                            }
                            else
                            {
                                double s = tuple.eigenValues[i]*sScale;
                                //double s = 0.1;
                                Point3d S = new Point3d(tuple.x - tuple.eigenVectors[i][0] * s, tuple.y - tuple.eigenVectors[i][1] * s, tuple.z - tuple.eigenVectors[i][2] * s);
                                Point3d E = new Point3d(tuple.x + tuple.eigenVectors[i][0] * s, tuple.y + tuple.eigenVectors[i][1] * s, tuple.z + tuple.eigenVectors[i][2] * s);
                                Line line = new Line(S, E);
                                line.Transform(zDown);
                                crossMagenta.Add(line);
                            }
                        }
                    }
                }

            }*/
    }
}
