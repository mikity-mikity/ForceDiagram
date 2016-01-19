using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Rhino.Geometry;



using ShoNS.Array;
using Grasshopper.Kernel;

namespace mikity.ghComponents
{
    public partial class ForceDiagram : Grasshopper.Kernel.GH_Component
    {
        public class buttonA : System.Windows.Forms.Button
        {
            public buttonA()
            {
                this.Text = "Compute Force Diagram";
            }
            public void notReady()
            {
                this.Text = "Compute Force Diagram";
            }
        }
        public class buttonB : System.Windows.Forms.Button
        {
            public buttonB()
            {
                this.Text = "Compute Shell Shape";
            }
            public void notReady()
            {
                this.Text = "Compute Shell Shape";
            }
        }
        public class controlPanel : System.Windows.Forms.UserControl
        {
            public enum _state { beforeStart, forceDiagramReady, computingForceDiagram,computingShellShape}
            public _state state;
            public buttonA buttonA;
            public buttonB buttonB;
            //public resetButton resetButton;
            public controlPanel()
            {
                state = _state.beforeStart;
                buttonA = new buttonA();
                buttonB = new buttonB();
                //resetButton = new resetButton();
                this.Controls.Add(buttonA);
                this.Controls.Add(buttonB);
                //this.Controls.Add(resetButton);
                buttonA.Left = 2;
                buttonB.Left = buttonA.Left + buttonA.Width + 2;
                this.Height = buttonA.Height + 2;
                this.Width = buttonA.Width + 4;
                buttonB.Visible = false;
            }
            public void notReady()
            {
                this.state = _state.beforeStart;
                buttonA.notReady();
                buttonB.notReady();
            }
        }
        public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            Menu_AppendCustomItem(menu, myControlPanel);
            //myControlPanel.buttonA.Click -= buttonA_Click;
            myControlPanel.buttonA.Click += buttonA_Click;
            //myControlPanel.resetButton.Click -= resetButton_Click;
            //myControlPanel.resetButton.Click += resetButton_Click;
            //Menu_AppendSeparator(menu);
        }

        private void buttonA_Click(object sender, EventArgs e)
        {
            buttonA obj = sender as buttonA;
            controlPanel cP = obj.Parent as controlPanel;
            if (cP.state == controlPanel._state.forceDiagramReady)
            {
                cP.state = controlPanel._state.computingForceDiagram;
                if (task == null)
                {
                    task = new Task(() => { computeF(); obj.Enabled = true; this.ExpirePreview(true); });
                    task.Start();
                }
                if (task.IsCompleted)
                {
                    task = new Task(() => { computeF(); obj.Enabled = true; this.ExpirePreview(true); });
                    task.Start();
                }
                obj.Enabled = false;
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("not ready");
            }
        }
        private void buttonB_Click(object sender, EventArgs e)
        {
            buttonB obj = sender as buttonB;
            controlPanel cP = obj.Parent as controlPanel;
        }
        public class node
        {
            public double x,y,z,X,Y,Z;
            public double airyHeight;
            public int N;
            public List<leaf> shareL = new List<leaf>();
            public List<int> numberLU = new List<int>();
            public List<int> numberLV = new List<int>();
            public int varOffset;
            public int conOffset;
            public bool compare(Point3d P)
            {
                double dx = P.X - x;
                double dy = P.Y - y;
                double dz = P.Z - z;
                if ((dx * dx + dy * dy + dz * dz) < 0.0000001) return true;
                return false;
            }
            public bool compare2(Point3d P)
            {
                double dx = P.X - X;
                double dy = P.Y - Y;
                double dz = P.Z - Z;
                if ((dx * dx + dy * dy + dz * dz) < 0.0000001) return true;
                return false;
            }
            public enum type
            {
                fr, fx
            }
            public type formNodeType,forceNodeType;

            public node()
            {
                N = 0;
                shareL.Clear();
                numberLU.Clear();
                numberLV.Clear();
                forceNodeType = type.fr;
                formNodeType = type.fr;
            }
        }

        public class leaf
        {
            public int varOffset;
            public int conOffset;
            //public range range;
            public Minilla3D.Objects.masonry myMasonry;
            public NurbsSurface formSrf;
            public NurbsSurface forceSrf;
            public NurbsSurface shellSrf;
            public int  r;  //Number of tuples.
            public int nU, nV;
            public int uDim, vDim;
            public int uDdim, vDdim;
            public int nUelem;
            public int nVelem;
            public int NN;  //NN*NN tuples in one element 
            public double scaleU, scaleV, originU, originV;
            public Interval domU, domV;
            public tuple_ex[] tuples;
            public tuple_ex[] edgeTuples;
            public int[] globalIndex;
        }

        public class tuple_ex:Minilla3D.Elements.nurbsElement.tuple
        {
            public tuple_ex(/*int _N, */double _ou, double _ov, double _u, double _v, int _index, double _loU, double _loV, double _area)
                : base(/*_N,*/ _ou, _ov, _u, _v, _index, _loU, _loV, _area)
            {}
            
            public void init(NurbsSurface S,double scaleU,double scaleV)
            {
                Point3d P;
                Vector3d[] V;
                S.Evaluate(u, v, 1, out P, out V);
                x = P.X;
                y = P.Y;
                gi2[0][0] = V[0].X * scaleU;
                gi2[0][1] = V[0].Y * scaleU;
                gi2[0][2] = 0;
                gi2[1][0] = V[1].X * scaleV;
                gi2[1][1] = V[1].Y * scaleV;
                gi2[1][2] = 0;
                gij2[0, 0] = gi2[0][0] * gi2[0][0] + gi2[0][1] * gi2[0][1];
                gij2[1, 0] = gi2[1][0] * gi2[0][0] + gi2[1][1] * gi2[0][1];
                gij2[0, 1] = gi2[0][0] * gi2[1][0] + gi2[0][1] * gi2[1][1];
                gij2[1, 1] = gi2[1][0] * gi2[1][0] + gi2[1][1] * gi2[1][1];
                double det = gij2[0, 0] * gij2[1, 1] - gij2[0, 1] * gij2[1, 0];
                Gij2[0, 0] = gij2[1, 1] / det;
                Gij2[1, 1] = gij2[0, 0] / det;
                Gij2[0, 1] = -gij2[0, 1] / det;
                Gij2[1, 0] = -gij2[1, 0] / det;
                Gi2[0][0]=Gij2[0,0]*gi2[0][0]+Gij2[1,0]*gi2[1][0];
                Gi2[0][1]=Gij2[0,0]*gi2[0][1]+Gij2[1,0]*gi2[1][1];
                Gi2[0][2]=0;
                Gi2[1][0]=Gij2[0,1]*gi2[0][0]+Gij2[1,1]*gi2[1][0];
                Gi2[1][1]=Gij2[0,1]*gi2[0][1]+Gij2[1,1]*gi2[1][1];
                Gi2[1][2]=0;
                S.Evaluate(u, v, 2, out P, out V);
                second2[0, 0][0] = V[2][0] * scaleU * scaleU;
                second2[0, 0][1] = V[2][1] * scaleU * scaleU;
                second2[0, 0][2] = 0;
                second2[1, 1][0] = V[4][0] * scaleV * scaleV;
                second2[1, 1][1] = V[4][1] * scaleV * scaleV;
                second2[1, 1][2] = 0;
                second2[0, 1][0] = V[3][0] * scaleU * scaleV;
                second2[0, 1][1] = V[3][1] * scaleU * scaleV;
                second2[0, 1][2] = 0;
                second2[1, 0][0] = V[3][0] * scaleV * scaleU;
                second2[1, 0][1] = V[3][1] * scaleV * scaleU;
                second2[1, 0][2] = 0;
                Gammaijk2[0, 0, 0] = second2[0, 0][0] * Gi2[0][0] + second2[0, 0][1] * Gi2[0][1];
                Gammaijk2[0, 0, 1] = second2[0, 0][0] * Gi2[1][0] + second2[0, 0][1] * Gi2[1][1];
                Gammaijk2[0, 1, 0] = second2[0, 1][0] * Gi2[0][0] + second2[0, 1][1] * Gi2[0][1];
                Gammaijk2[0, 1, 1] = second2[0, 1][0] * Gi2[1][0] + second2[0, 1][1] * Gi2[1][1];
                Gammaijk2[1, 0, 0] = second2[1, 0][0] * Gi2[0][0] + second2[1, 0][1] * Gi2[0][1];
                Gammaijk2[1, 0, 1] = second2[1, 0][0] * Gi2[1][0] + second2[1, 0][1] * Gi2[1][1];
                Gammaijk2[1, 1, 0] = second2[1, 1][0] * Gi2[0][0] + second2[1, 1][1] * Gi2[0][1];
                Gammaijk2[1, 1, 1] = second2[1, 1][0] * Gi2[1][0] + second2[1, 1][1] * Gi2[1][1];
            }
        }
        //ControlBox myControlBox = new ControlBox();
        List<Surface> _listSrf1;
        List<Surface> _listSrf2;
        List<Curve> _listCrv;
        List<Point3d> _listPnt;
        List<leaf> listLeaf;
        List<node> listNode;
        List<Line> listArrow;
        public List<Point3d> listPnt;
        List<Line> crossCyan;
        List<Line> crossMagenta;
        controlPanel myControlPanel;
        System.Threading.Tasks.Task task = null;

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            base.DocumentContextChanged(document, context);
            if (context == GH_DocumentContext.Loaded)
            {
                myControlPanel = new controlPanel();
            }
        }

        private void init()
        {
            ready = false;
            crossCyan = new List<Line>();
            crossMagenta = new List<Line>();
        }
        public ForceDiagram()
            : base("ForceDiagram", "ForceDiagram", "ForceDiagram", "Kapybara3D", "Computation")
        {
        }
        public override Guid ComponentGuid
        {
            get { return new Guid("43317AF5-2BFB-4C77-AACD-90F9FE623ACD"); }
        }
        protected override void RegisterInputParams(Grasshopper.Kernel.GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddSurfaceParameter("listSurface1", "lstSrf1", "list of surfaces1", Grasshopper.Kernel.GH_ParamAccess.list);
            pManager.AddSurfaceParameter("listSurface2", "lstSrf2", "list of surfaces2", Grasshopper.Kernel.GH_ParamAccess.list);
            pManager.AddCurveParameter("fixedBoundary", "listCrv", "list of boundary curves(force diagram!)", GH_ParamAccess.list);
            pManager.AddPointParameter("fixedPoints", "listPnt", "list of fixed points (form diagram!)", GH_ParamAccess.list);
        }
        protected override void RegisterOutputParams(Grasshopper.Kernel.GH_Component.GH_OutputParamManager pManager)
        {
        }
        public override void AddedToDocument(Grasshopper.Kernel.GH_Document document)
        {
            base.AddedToDocument(document);
        }
        bool ready = false;
        void computeF()
        {
            int globalNN = 2;
            System.Windows.Forms.MessageBox.Show("A");

            foreach (var leaf in listLeaf)
            {
                leaf.NN = globalNN * (leaf.uDdim)-2;
                double area = 1d / ((double)leaf.NN) / ((double)leaf.NN);
                //setup tuples
                //internal tuples
                leaf.r = leaf.nUelem * leaf.nVelem * leaf.NN * leaf.NN;
                leaf.tuples = new tuple_ex[leaf.r];
                for (int vv = 0; vv < leaf.NN * leaf.nVelem; vv++)
                {
                    for (int uu = 0; uu < leaf.NN*leaf.nUelem; uu++)
                    {
                        int num = uu + vv * (leaf.NN * leaf.nUelem);
                        double centerU = (uu + 0.5) / leaf.NN;
                        double centerV = (vv + 0.5) / leaf.NN;
                        //element index
                        int uNum = (int)centerU;
                        int vNum = (int)centerV;
                        int index = uNum + vNum * leaf.nUelem;                            
                        //local coordinates
                        double localU = centerU - uNum;
                        double localV = centerV - vNum;
                        leaf.tuples[num] = new tuple_ex(centerU, centerV, centerU * leaf.scaleU + leaf.originU, centerV * leaf.scaleV + leaf.originV, index, localU, localV, area);
                        leaf.tuples[num].init(leaf.formSrf, leaf.scaleU, leaf.scaleV);                            
                    }
                }
                createNurbsElements(leaf);
                double[,] x;
                double[,] _x;
                x = new double[leaf.nU * leaf.nV, 3];
                _x = new double[leaf.nU * leaf.nV, 3];
                Nurbs2x(leaf.formSrf, x);
                Nurbs2x(leaf.forceSrf, _x);
                leaf.myMasonry.setupNodesFromList(x);
                leaf.myMasonry.computeGlobalCoord();
                foreach (var e in leaf.myMasonry.elemList)
                {
                    e.precompute();
                    e.computeBaseVectors();
                }
                foreach (var tup in leaf.tuples)
                {
                    leaf.myMasonry.elemList[tup.index].precompute(tup);
                }
            }
            System.Windows.Forms.MessageBox.Show("G");
            computeForceDiagram(listLeaf, listNode);

            ready = true;
            this.ExpirePreview(true);
        }
        public int ttt = 0;
        protected override void SolveInstance(Grasshopper.Kernel.IGH_DataAccess DA)
        {
            init();
            _listSrf1 = new List<Surface>();
            _listSrf2 = new List<Surface>();
            _listCrv = new List<Curve>();
            _listPnt = new List<Point3d>();
            if (!DA.GetDataList(0, _listSrf1)) { return; }
            if (!DA.GetDataList(1, _listSrf2)) { return; }
            if (!DA.GetDataList(2, _listCrv)) { _listCrv = new List<Curve>(); }
            if (!DA.GetDataList(3, _listPnt)) { _listPnt=new List<Point3d>(); }
            listNode = new List<node>();
            listLeaf = new List<leaf>();
            for (int i = 0; i < _listSrf1.Count; i++)
            {
                var formSrf = _listSrf1[i];
                var forceSrf = _listSrf2[i];
                var leaf = new leaf();
                listLeaf.Add(leaf);
                leaf.formSrf = formSrf as NurbsSurface;
                leaf.forceSrf = forceSrf as NurbsSurface;
                leaf.nU = leaf.formSrf.Points.CountU;
                leaf.nV = leaf.formSrf.Points.CountV;
                leaf.domU = leaf.formSrf.Domain(0);
                leaf.domV = leaf.formSrf.Domain(1);
                leaf.uDim = leaf.formSrf.OrderU;
                leaf.vDim = leaf.formSrf.OrderV;
                leaf.uDdim = leaf.formSrf.OrderU - 1;
                leaf.vDdim = leaf.formSrf.OrderV - 1;
                leaf.nUelem = leaf.nU - leaf.uDdim;
                leaf.nVelem = leaf.nV - leaf.vDdim;
                leaf.scaleU = (leaf.domU.T1 - leaf.domU.T0) / leaf.nUelem;
                leaf.scaleV = (leaf.domV.T1 - leaf.domV.T0) / leaf.nVelem;
                leaf.originU = leaf.domU.T0;
                leaf.originV = leaf.domV.T0;
                var domainU = leaf.formSrf.Domain(0);
                var domainV = leaf.formSrf.Domain(1);
                var key = "leaf";
            }
            // Connect nodes
            foreach (var leaf in listLeaf)
            {
                leaf.globalIndex = new int[leaf.formSrf.Points.CountU * leaf.formSrf.Points.CountV];
                for (int j = 0; j < leaf.formSrf.Points.CountV; j++)
                {
                    for (int i = 0; i < leaf.formSrf.Points.CountU; i++)
                    {
                        var P = leaf.formSrf.Points.GetControlPoint(i, j).Location;
                        var Q = leaf.forceSrf.Points.GetControlPoint(i, j).Location;
                        bool flag = false;
                        foreach (var node in listNode)
                        {
                            if (node.compare2(Q))
                            {
                                flag = true;
                                node.N++;
                                node.shareL.Add(leaf);
                                node.numberLU.Add(i);
                                node.numberLV.Add(j);
                                leaf.globalIndex[i + j * leaf.nU] = listNode.IndexOf(node);
                                break;
                            }
                        }
                        if (!flag)
                        {
                            var newNode = new node();
                            newNode.forceNodeType = node.type.fr;
                            newNode.formNodeType = node.type.fr;
                            listNode.Add(newNode);
                            newNode.N = 1;
                            newNode.x = P.X;
                            newNode.y = P.Y;
                            newNode.z = P.Z;
                            newNode.X = Q.X;
                            newNode.Y = Q.Y;
                            newNode.Z = Q.Z;

                            newNode.shareL.Add(leaf);
                            newNode.numberLU.Add(i);
                            newNode.numberLV.Add(j);
                            leaf.globalIndex[i + j * leaf.nU] = listNode.IndexOf(newNode);
                        }
                    }
                }
            }
            //judge which node is fixed...
            foreach (var node in listNode)
            {
                foreach (var _crv in _listCrv)
                {
                    var crv = _crv as NurbsCurve;
                    for (int i = 0; i < crv.Points.Count; i++)
                    {
                        if (node.compare2(crv.Points[i].Location))
                        {
                            node.forceNodeType = node.type.fx;
                        }
                    }
                }
                foreach (var P in _listPnt)
                {
                    if (node.compare2(P))
                    {
                            node.forceNodeType = node.type.fx;
                    }
                }
            }
            myControlPanel.state = controlPanel._state.forceDiagramReady;
        }
    }
}
