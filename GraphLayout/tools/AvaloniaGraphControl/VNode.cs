using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Drawing;
using Edge = Microsoft.Msagl.Drawing.Edge;
using Ellipse = Microsoft.Msagl.Core.Geometry.Curves.Ellipse;
using LineSegment = Microsoft.Msagl.Core.Geometry.Curves.LineSegment;
using Node = Microsoft.Msagl.Drawing.Node;
using Point = Microsoft.Msagl.Core.Geometry.Point;
using Polyline = Microsoft.Msagl.Core.Geometry.Curves.Polyline;
using Shape = Microsoft.Msagl.Drawing.Shape;
using Size = Avalonia.Size;

namespace Microsoft.Msagl.AvaloniaGraphControl {
    public class VNode : IViewerNode, IInvalidatable {
        internal Path BoundaryPath;
        internal Control ControlOfNodeForLabel;
        readonly Func<Edge, VEdge> _funcFromDrawingEdgeToVEdge;
        Subgraph _subgraph;
        Node _node;
        Border _collapseButtonBorder;
        Rectangle _topMarginRect;
        Path _collapseSymbolPath;
        readonly IBrush _collapseSymbolPathInactive = Brushes.Silver;

        internal int ZIndex {
            get {
                var geomNode = Node.GeometryNode;
                if (geomNode == null)
                    return 0;
                return geomNode.AllClusterAncestors.Count();
            }
        }

        public Node Node {
            get { return _node; }
            private set {
                _node = value;
                _subgraph = _node as Subgraph;
            }
        }


        internal VNode(Node node, Control frameworkElementOfNodeForLabelOfLabel, LayoutAlgorithmSettings settings,
            Func<Edge, VEdge> funcFromDrawingEdgeToVEdge, Func<double> pathStrokeThicknessFunc, bool createToolTipForNodes)
        {
            PathStrokeThicknessFunc = pathStrokeThicknessFunc;
            Node = node;
            ControlOfNodeForLabel = frameworkElementOfNodeForLabelOfLabel;
            _funcFromDrawingEdgeToVEdge = funcFromDrawingEdgeToVEdge;

            CreateNodeBoundaryPath(createToolTipForNodes);

            if (ControlOfNodeForLabel != null)
            {
                ControlOfNodeForLabel.Tag = this; //get a backpointer to the VNode
                Common.PositionControl(ControlOfNodeForLabel, GetLabelPosition(node), 1);
                ControlOfNodeForLabel.ZIndex = BoundaryPath.ZIndex + 1;
            }

            SetupSubgraphDrawing(settings);
            Node.Attr.VisualsChanged += (a, b) => Invalidate();
            Node.IsVisibleChanged += obj =>
            {
                foreach (var frameworkElement in Controls)
                {
                    frameworkElement.IsVisible = Node.IsVisible;
                }
            };
        }

        internal IEnumerable<Control> Controls {
            get {
                if (ControlOfNodeForLabel != null) yield return ControlOfNodeForLabel;
                if (BoundaryPath != null) yield return BoundaryPath;
                if (_collapseButtonBorder != null) {
                    yield return _collapseButtonBorder;
                    yield return _topMarginRect;
                    yield return _collapseSymbolPath;
                }
            }
        }

        void SetupSubgraphDrawing(LayoutAlgorithmSettings settings) {
            if (_subgraph == null) return;

            SetupTopMarginBorder();
            SetupCollapseSymbol();

            // Fix missing margins around label right after the launch https://github.com/microsoft/automatic-graph-layout/pull/313#issuecomment-1130468914
            var cluster = (Cluster)_subgraph.GeometryObject;
            cluster.CalculateBoundsFromChildren(settings.ClusterMargin);
        }

        void SetupTopMarginBorder() {
            var cluster = (Cluster) _subgraph.GeometryObject;
            _topMarginRect = new Rectangle {
                Fill = Brushes.Transparent,
                Width = Node.Width,
                Height = cluster.RectangularBoundary.TopMargin
            };
            PositionTopMarginBorder(cluster);
            SetZIndexAndMouseInteractionsForTopMarginRect();
        }

        void PositionTopMarginBorder(Cluster cluster) {
            var box = cluster.BoundaryCurve.BoundingBox;

            Common.PositionControl(_topMarginRect,
                box.LeftTop + new Point(_topMarginRect.Width/2, -_topMarginRect.Height/2), 1);
        }

        void SetZIndexAndMouseInteractionsForTopMarginRect() {
            _topMarginRect.PointerEntered +=
                (
                    (a, b) => {
                        _collapseButtonBorder.Background =
                            Common.BrushFromMsaglColor(_subgraph.CollapseButtonColorActive);
                        _collapseSymbolPath.Stroke = Brushes.Black;
                    }
                    );

            _topMarginRect.PointerExited +=
                (a, b) => {
                    _collapseButtonBorder.Background = Common.BrushFromMsaglColor(_subgraph.CollapseButtonColorInactive);
                    _collapseSymbolPath.Stroke = Brushes.Silver;
                };
            _topMarginRect.ZIndex = int.MaxValue;
        }

        void SetupCollapseSymbol() {
            var collapseBorderSize = GetCollapseBorderSymbolSize();
            Debug.Assert(collapseBorderSize > 0);
            _collapseButtonBorder = new Border {
                Background = Common.BrushFromMsaglColor(_subgraph.CollapseButtonColorInactive),
                Width = collapseBorderSize,
                Height = collapseBorderSize,
                CornerRadius = new CornerRadius(collapseBorderSize/2)
            };

            _collapseButtonBorder.ZIndex = BoundaryPath.ZIndex + 1;


            var collapseButtonCenter = GetCollapseButtonCenter(collapseBorderSize);
            Common.PositionControl(_collapseButtonBorder, collapseButtonCenter, 1);

            double w = collapseBorderSize*0.4;
            _collapseSymbolPath = new Path {
                Data = CreateCollapseSymbolPath(collapseButtonCenter + new Point(0, -w/2), w),
                Stroke = _collapseSymbolPathInactive,
                StrokeThickness = 1
            };

            _collapseSymbolPath.ZIndex = _collapseButtonBorder.ZIndex + 1;
            _topMarginRect.PointerPressed += TopMarginRectPointerPressed;
        }


        /// <summary>
        /// </summary>
        public event Action<IViewerNode> IsCollapsedChanged;

        void InvokeIsCollapsedChanged() {
            if (IsCollapsedChanged != null)
                IsCollapsedChanged(this);
        }

        void TopMarginRectPointerPressed(object sender, PointerPressedEventArgs e) {
            if (e.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) {
                return;
            }
            var pos = e.GetPosition(_collapseButtonBorder);
            if (pos.X <= _collapseButtonBorder.Width && pos.Y <= _collapseButtonBorder.Height && pos.X >= 0 &&
                pos.Y >= 0) {
                e.Handled = true;
                var cluster = (Cluster) _subgraph.GeometryNode;
                cluster.IsCollapsed = !cluster.IsCollapsed;
                InvokeIsCollapsedChanged();
            }
        }

        double GetCollapseBorderSymbolSize() {
            return ((Cluster) _subgraph.GeometryNode).RectangularBoundary.TopMargin -
                   PathStrokeThickness/2 - 0.5;
        }

        Point GetCollapseButtonCenter(double collapseBorderSize) {
            var box = _subgraph.GeometryNode.BoundaryCurve.BoundingBox;
            //cannot trust subgraph.GeometryNode.BoundingBox for a cluster
            double offsetFromBoundaryPath = PathStrokeThickness/2 + 0.5;
            var collapseButtonCenter = box.LeftTop + new Point(collapseBorderSize/2 + offsetFromBoundaryPath,
                -collapseBorderSize/2 - offsetFromBoundaryPath);
            return collapseButtonCenter;
        }


/*
        void FlipCollapsePath() {
            var size = GetCollapseBorderSymbolSize();
            var center = GetCollapseButtonCenter(size);

            if (collapsePathFlipped) {
                collapsePathFlipped = false;
                collapseSymbolPath.RenderTransform = null;
            }
            else {
                collapsePathFlipped = true;
                collapseSymbolPath.RenderTransform = new RotateTransform(180, center.X, center.Y);
            }
        }
*/

        Geometry CreateCollapseSymbolPath(Point center, double width) {
            var pathGeometry = new PathGeometry();
            var pathFigure = new PathFigure {StartPoint = Common.AvaloniaPoint(center + new Point(-width, width))};

            pathFigure.Segments.Add(new Avalonia.Media.LineSegment() {
                Point = Common.AvaloniaPoint(center),
                IsStroked = true
            });
            pathFigure.Segments.Add(
                new Avalonia.Media.LineSegment() {
                    Point = Common.AvaloniaPoint(center + new Point(width, width)),
                    IsStroked = true
                });

            pathGeometry.Figures.Add(pathFigure);
            return pathGeometry;
        }

        internal void CreateNodeBoundaryPath(bool setNodeToolTips) {
            if (ControlOfNodeForLabel != null) {
                // ControlOfNode.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var center = Node.GeometryNode.Center;
                var margin = 2*Node.Attr.LabelMargin;
                var bc = NodeBoundaryCurves.GetNodeBoundaryCurve(Node,
                    ControlOfNodeForLabel
                        .Width + margin,
                    ControlOfNodeForLabel
                        .Height + margin);
                bc.Translate(center);
            }
            BoundaryPath = new Path {Data = CreatePathFromNodeBoundary(), Tag = this};
            BoundaryPath.ZIndex = ZIndex;
            SetFillAndStroke();
            if (setNodeToolTips && (
                Node.Label != null
                && !string.IsNullOrEmpty(Node.LabelText))) {
                ToolTip.SetTip(BoundaryPath, Node.LabelText);
                if (ControlOfNodeForLabel != null)
                    ToolTip.SetTip(ControlOfNodeForLabel, Node.LabelText);
            }
        }

        internal Func<double> PathStrokeThicknessFunc;

        double PathStrokeThickness {
            get { return PathStrokeThicknessFunc != null ? PathStrokeThicknessFunc() : Node.Attr.LineWidth; }
        }

        byte GetTransparency(byte t) {
            return t;
        }

        void SetFillAndStroke() {
            byte trasparency = GetTransparency(Node.Attr.Color.A);
            BoundaryPath.Stroke =
                Common.BrushFromMsaglColor(new Drawing.Color(trasparency, Node.Attr.Color.R, Node.Attr.Color.G,
                    Node.Attr.Color.B));
            SetBoundaryFill();
            BoundaryPath.StrokeThickness = PathStrokeThickness;

            var textBlock = ControlOfNodeForLabel as TextBlock;
            if (textBlock != null) {
                var col = Node.Label.FontColor;
                textBlock.Foreground =
                    Common.BrushFromMsaglColor(new Drawing.Color(GetTransparency(col.A), col.R, col.G, col.B));
            }
        }

        void SetBoundaryFill() {
            BoundaryPath.Fill = Common.BrushFromMsaglColor(Node.Attr.FillColor);
        }

        Geometry DoubleCircle() {
            var box = Node.BoundingBox;
            double w = box.Width;
            double h = box.Height;
            var pathGeometry = new CombinedGeometry();
            var r = new Rect(box.Left, box.Bottom, w, h);
            pathGeometry.Geometry1 = new EllipseGeometry(r);
            var inflation = Math.Min(5.0, Math.Min(w/3, h/3));
            r.Inflate(new Thickness(-inflation, -inflation));
            pathGeometry.Geometry2 = new EllipseGeometry(r);
            return pathGeometry;
        }

        Geometry CreatePathFromNodeBoundary() {
            Geometry geometry;
            switch (Node.Attr.Shape) {
                case Shape.Box:
                case Shape.House:
                case Shape.InvHouse:
                case Shape.Diamond:
                case Shape.Octagon:
                case Shape.Hexagon:

                    geometry = CreateGeometryFromMsaglCurve(Node.GeometryNode.BoundaryCurve);
                    break;

                case Shape.DoubleCircle:
                    geometry = DoubleCircle();
                    break;


                default:
                    geometry = GetEllipseGeometry();
                    break;
            }

            return geometry;
        }

        Geometry CreateGeometryFromMsaglCurve(ICurve iCurve) {
            var pathGeometry = new PathGeometry();
            var pathFigure = new PathFigure {
                IsClosed = true,
                IsFilled = true,
                StartPoint = Common.AvaloniaPoint(iCurve.Start)
            };

            var curve = iCurve as Curve;
            if (curve != null) {
                AddCurve(pathFigure, curve);
            }
            else {
                var rect = iCurve as Core.Geometry.Curves.RoundedRect;
                if (rect != null)
                    AddCurve(pathFigure, rect.Curve);
                else {
                    var ellipse = iCurve as Ellipse;
                    if (ellipse != null) {
                        return new EllipseGeometry() {
                            Center = Common.AvaloniaPoint(ellipse.Center),
                            RadiusX = ellipse.AxisA.Length,
                            RadiusY = ellipse.AxisB.Length
                        };
                    }
                    var poly = iCurve as Polyline;
                    if (poly != null) {
                        var p = poly.StartPoint.Next;
                        do {
                            pathFigure.Segments.Add(new Avalonia.Media.LineSegment() {
                                Point = Common.AvaloniaPoint(p.Point),
                                IsStroked = true
                            });

                            p = p.NextOnPolyline;
                        } while (p != poly.StartPoint);
                    }
                }
            }


            pathGeometry.Figures.Add(pathFigure);

            return pathGeometry;
        }


        static void AddCurve(PathFigure pathFigure, Curve curve) {
            foreach (ICurve seg in curve.Segments) {
                var ls = seg as LineSegment;
                if (ls != null)
                    pathFigure.Segments.Add(new Avalonia.Media.LineSegment() {
                        Point = Common.AvaloniaPoint(ls.End),
                        IsStroked = true
                    });
                else {
                    var ellipse = seg as Ellipse;
                    if (ellipse != null)
                        pathFigure.Segments.Add(new ArcSegment() {
                            Point = Common.AvaloniaPoint(ellipse.End),
                            Size = new Size(ellipse.AxisA.Length, ellipse.AxisB.Length),
                            RotationAngle = Point.Angle(new Point(1, 0), ellipse.AxisA),
                            IsLargeArc = ellipse.ParEnd - ellipse.ParEnd >= Math.PI,
                            SweepDirection = !ellipse.OrientedCounterclockwise()
                                ? SweepDirection.CounterClockwise
                                : SweepDirection.Clockwise,
                            IsStroked = true
                        });
                }
            }
        }

        Geometry GetEllipseGeometry() {
            return new EllipseGeometry() {
                Center = Common.AvaloniaPoint(Node.BoundingBox.Center),
                RadiusX = Node.BoundingBox.Width/2,
                RadiusY = Node.BoundingBox.Height/2
            };
        }

        #region Implementation of IViewerObject

        public DrawingObject DrawingObject {
            get { return Node; }
        }

        bool markedForDragging;

        /// <summary>
        /// Implements a property of an interface IEditViewer
        /// </summary>
        public bool MarkedForDragging
        {
            get
            {
                return markedForDragging;
            }
            set
            {
                markedForDragging = value;
                if (value)
                {
                    MarkForDraggingEvent?.Invoke(this, null);
                }
                else
                {
                    UnmarkForDraggingEvent?.Invoke(this, null);
                }
            }
        }

        public event EventHandler MarkForDraggingEvent;
        public event EventHandler UnmarkForDraggingEvent;

        #endregion

        public IEnumerable<IViewerEdge> InEdges {
            get { return Node.InEdges.Select(e => _funcFromDrawingEdgeToVEdge(e)); }
        }

        public IEnumerable<IViewerEdge> OutEdges {
            get { return Node.OutEdges.Select(e => _funcFromDrawingEdgeToVEdge(e)); }
        }

        public IEnumerable<IViewerEdge> SelfEdges {
            get { return Node.SelfEdges.Select(e => _funcFromDrawingEdgeToVEdge(e)); }
        }
        public void Invalidate() {
            if (!Node.IsVisible) {
                foreach (var fe in Controls)
                    fe.IsVisible = false;
                return;
            }

            BoundaryPath.Data = CreatePathFromNodeBoundary();

            Common.PositionControl(ControlOfNodeForLabel, GetLabelPosition(Node), 1);


            SetFillAndStroke();
            if (_subgraph == null) return;
            PositionTopMarginBorder((Cluster) _subgraph.GeometryNode);
            double collapseBorderSize = GetCollapseBorderSymbolSize();
            var collapseButtonCenter = GetCollapseButtonCenter(collapseBorderSize);
            Common.PositionControl(_collapseButtonBorder, collapseButtonCenter, 1);
            double w = collapseBorderSize*0.4;
            _collapseSymbolPath.Data = CreateCollapseSymbolPath(collapseButtonCenter + new Point(0, -w/2), w);
            _collapseSymbolPath.RenderTransform = ((Cluster) _subgraph.GeometryNode).IsCollapsed
                ? new RotateTransform(180, collapseButtonCenter.X,
                    collapseButtonCenter.Y)
                : null;

            _topMarginRect.IsVisible =
                _collapseSymbolPath.IsVisible =
                    _collapseButtonBorder.IsVisible = true;

        }

        Point GetLabelPosition(Node node)
        {
            var box = node.BoundingBox;

            if (node.Label.Owner is Subgraph subgraph) {
                var buttonRadius = subgraph.DiameterOfOpenCollapseButton / 2;

                if (_subgraph.GeometryNode is Cluster c && c.IsCollapsed)
                    return box.Center + new Point(buttonRadius, 0);

                var text =
                    GraphViewer.MeasureText(
                        node.LabelText,
                        new FontFamily(node.Label.FontName),
                        node.Label.FontSize,
                        ControlOfNodeForLabel);    // without this NullReferenceException in VisualTreeHelper.GetDpi

                double x = 0;
                double y = 0;

                switch (subgraph.Attr.ClusterLabelMargin) {
                    case LabelPlacement.Top:
                        x = buttonRadius;   // shift only for Top since CollapseButton is at top left
                        y = box.Height / 2 - text.Height / 2;
                        break;
                    case LabelPlacement.Bottom:
                        y = - box.Height / 2 + text.Height / 2;
                        break;
                    case LabelPlacement.Left:
                        x = - box.Width / 2 + text.Width / 2;
                        break;
                    case LabelPlacement.Right:
                        x = box.Width / 2 - text.Width / 2;
                        break;
                }

                return box.Center + new Point(x, y);
            }
            return box.Center;
        }

        public override string ToString() {
            return Node.Id;
        }

        internal void DetouchFromCanvas(Canvas graphCanvas) {
            if (BoundaryPath != null)
                graphCanvas.Children.Remove(BoundaryPath);
            if (ControlOfNodeForLabel != null)
                graphCanvas.Children.Remove(ControlOfNodeForLabel);
        }
    }
}
