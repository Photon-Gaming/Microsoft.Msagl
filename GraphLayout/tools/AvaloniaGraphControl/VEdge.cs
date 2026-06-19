/*
Microsoft Automatic Graph Layout,MSAGL

Copyright (c) Microsoft Corporation

All rights reserved.

MIT License

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
""Software""), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.DebugHelpers;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.Layout.LargeGraphLayout;
using Microsoft.Msagl.Routing;
using Color = Microsoft.Msagl.Drawing.Color;
using Edge = Microsoft.Msagl.Drawing.Edge;
using Ellipse = Microsoft.Msagl.Core.Geometry.Curves.Ellipse;
using LineSegment = Microsoft.Msagl.Core.Geometry.Curves.LineSegment;
using Point = Microsoft.Msagl.Core.Geometry.Point;
using Polyline = Microsoft.Msagl.Core.Geometry.Curves.Polyline;
using Rectangle = Microsoft.Msagl.Core.Geometry.Rectangle;
using Size = Avalonia.Size;

namespace Microsoft.Msagl.AvaloniaGraphControl {
    internal class VEdge : IViewerEdge, IInvalidatable {

        internal Control LabelControl;

        public VEdge(Edge edge, Control labelControl) {
            Edge = edge;
            CurvePath = new Path {
                Data = GetICurveAvaloniaGeometry(edge.GeometryEdge.Curve),
                Tag = this
            };

            EdgeAttrClone = edge.Attr.Clone();

            if (edge.Attr.ArrowAtSource)
                SourceArrowHeadPath = new Path {
                    Data = DefiningSourceArrowHead(),
                    Tag = this
                };
            if (edge.Attr.ArrowAtTarget)
                TargetArrowHeadPath = new Path {
                    Data = DefiningTargetArrowHead(Edge.GeometryEdge.EdgeGeometry, PathStrokeThickness),
                    Tag = this
                };

            SetPathStroke();

            if (labelControl != null) {
                LabelControl = labelControl;
                Common.PositionControl(LabelControl, edge.Label.Center, 1);
            }
            edge.Attr.VisualsChanged += (a, b) => Invalidate();

            edge.IsVisibleChanged += obj => {
                foreach (var frameworkElement in Controls) {
                    frameworkElement.IsVisible = edge.IsVisible;
                }
            };
        }

        internal IEnumerable<Control> Controls {
            get {
                if (SourceArrowHeadPath != null)
                    yield return this.SourceArrowHeadPath;
                if (TargetArrowHeadPath != null)
                    yield return TargetArrowHeadPath;

                if (CurvePath != null)
                    yield return CurvePath;

                if (
                    LabelControl != null)
                    yield return
                        LabelControl;
            }
        }


        internal EdgeAttr EdgeAttrClone { get; set; }

        internal static Geometry DefiningTargetArrowHead(EdgeGeometry edgeGeometry, double thickness) {
            if (edgeGeometry.TargetArrowhead == null || edgeGeometry.Curve==null)
                return null;
            var streamGeometry = new StreamGeometry();
            using (StreamGeometryContext context = streamGeometry.Open()) {
                AddArrow(context, edgeGeometry.Curve.End,
                         edgeGeometry.TargetArrowhead.TipPosition, thickness);
                return streamGeometry;
            }
        }

        Geometry DefiningSourceArrowHead() {
            var streamGeometry = new StreamGeometry();
            using (StreamGeometryContext context = streamGeometry.Open()) {
                AddArrow(context, Edge.GeometryEdge.Curve.Start, Edge.GeometryEdge.EdgeGeometry.SourceArrowhead.TipPosition, PathStrokeThickness);
                return streamGeometry;
            }
        }


        double PathStrokeThickness { get {
            return PathStrokeThicknessFunc != null ? PathStrokeThicknessFunc() : this.Edge.Attr.LineWidth;
        } }

        internal Path CurvePath { get; set; }
        internal Path SourceArrowHeadPath { get; set; }
        internal Path TargetArrowHeadPath { get; set; }

        static internal Geometry GetICurveAvaloniaGeometry(ICurve curve) {
            var streamGeometry = new StreamGeometry();
            using (StreamGeometryContext context = streamGeometry.Open()) {
                FillStreamGeometryContext(context, curve);
                return streamGeometry;
            }
        }

        static void FillStreamGeometryContext(StreamGeometryContext context, ICurve curve) {
            if (curve == null)
                return;
            FillContextForICurve(context, curve);
        }

        static internal void FillContextForICurve(StreamGeometryContext context,ICurve iCurve) {

            context.BeginFigure(Common.AvaloniaPoint(iCurve.Start),false);

            var c = iCurve as Curve;
            if(c != null)
                FillContexForCurve(context,c);
            else {
                var cubicBezierSeg = iCurve as CubicBezierSegment;
                if(cubicBezierSeg != null)
                    context.CubicBezierTo(Common.AvaloniaPoint(cubicBezierSeg.B(1)),Common.AvaloniaPoint(cubicBezierSeg.B(2)),
                        Common.AvaloniaPoint(cubicBezierSeg.B(3)),true);
                else {
                    var ls = iCurve as LineSegment;
                    if(ls != null)
                        context.LineTo(Common.AvaloniaPoint(ls.End),true);
                    else {
                        var rr = iCurve as Core.Geometry.Curves.RoundedRect;
                        if(rr != null)
                            FillContexForCurve(context,rr.Curve);
                        else {
                            var poly = iCurve as Polyline;
                            if (poly != null)
                                FillContexForPolyline(context, poly);
                            else
                            {
                                var ellipse = iCurve as Ellipse;
                                if (ellipse != null) {
                                    //       context.LineTo(Common.AvaloniaPoint(ellipse.End),true,false);
                                    double sweepAngle = EllipseSweepAngle(ellipse);
                                    bool largeArc = Math.Abs(sweepAngle) >= Math.PI;
                                    Rectangle box = ellipse.FullBox();
                                    context.ArcTo(Common.AvaloniaPoint(ellipse.End),
                                                  new Size(box.Width/2, box.Height/2),
                                                  sweepAngle,
                                                  largeArc,
                                                  sweepAngle < 0
                                                      ? SweepDirection.CounterClockwise
                                                      : SweepDirection.Clockwise,
                                                  true);
                                } else {
                                    throw new NotImplementedException();
                                }
                            }
                        }
                    }
                }
            }
        }

        static void FillContexForPolyline(StreamGeometryContext context,Polyline poly) {
            for(PolylinePoint pp = poly.StartPoint.Next;pp != null;pp = pp.Next)
                context.LineTo(Common.AvaloniaPoint(pp.Point),true);
        }

        static void FillContexForCurve(StreamGeometryContext context,Curve c) {
            foreach(ICurve seg in c.Segments) {
                var bezSeg = seg as CubicBezierSegment;
                if(bezSeg != null) {
                    context.CubicBezierTo(Common.AvaloniaPoint(bezSeg.B(1)),
                                     Common.AvaloniaPoint(bezSeg.B(2)),Common.AvaloniaPoint(bezSeg.B(3)),true);
                } else {
                    var ls = seg as LineSegment;
                    if(ls != null)
                        context.LineTo(Common.AvaloniaPoint(ls.End),true);
                    else {
                        var ellipse = seg as Ellipse;
                        if(ellipse != null) {
                            //       context.LineTo(Common.AvaloniaPoint(ellipse.End),true,false);
                            double sweepAngle = EllipseSweepAngle(ellipse);
                            bool largeArc = Math.Abs(sweepAngle) >= Math.PI;
                            Rectangle box = ellipse.FullBox();
                            context.ArcTo(Common.AvaloniaPoint(ellipse.End),
                                          new Size(box.Width / 2,box.Height / 2),
                                          sweepAngle,
                                          largeArc,
                                          sweepAngle < 0
                                              ? SweepDirection.CounterClockwise
                                              : SweepDirection.Clockwise,
                                          true);
                        } else
                            throw new NotImplementedException();
                    }
                }
            }
        }

        public static double EllipseSweepAngle(Ellipse ellipse) {
            double sweepAngle = ellipse.ParEnd - ellipse.ParStart;
            return ellipse.OrientedCounterclockwise() ? sweepAngle : -sweepAngle;
        }


        static void AddArrow(StreamGeometryContext context,Point start,Point end, double thickness) {
            if(thickness > 1) {
                Point dir = end - start;
                Point h = dir;
                double dl = dir.Length;
                if(dl < 0.001)
                    return;
                dir /= dl;

                var s = new Point(-dir.Y,dir.X);
                double w = 0.5 * thickness;
                Point s0 = w * s;

                s *= h.Length * HalfArrowAngleTan;
                s += s0;

                double rad = w / HalfArrowAngleCos;

                context.BeginFigure(Common.AvaloniaPoint(start + s),true);
                context.LineTo(Common.AvaloniaPoint(start - s),true);
                context.LineTo(Common.AvaloniaPoint(end - s0),true);
                context.ArcTo(Common.AvaloniaPoint(end + s0),new Size(rad,rad),
                              Math.PI - ArrowAngle,false,SweepDirection.Clockwise,true);
            } else {
                Point dir = end - start;
                double dl = dir.Length;
                //take into account the widths
                double delta = Math.Min(dl / 2, thickness + thickness / 2);
                dir *= (dl - delta) / dl;
                end = start + dir;
                dir = dir.Rotate(Math.PI / 2);
                Point s = dir * HalfArrowAngleTan;

                context.BeginFigure(Common.AvaloniaPoint(start + s),true);
                context.LineTo(Common.AvaloniaPoint(end),true);
                context.LineTo(Common.AvaloniaPoint(start - s),true);
            }
            context.EndFigure(true);
        }

        static readonly double HalfArrowAngleTan = Math.Tan(ArrowAngle * 0.5 * Math.PI / 180.0);
        static readonly double HalfArrowAngleCos = Math.Cos(ArrowAngle * 0.5 * Math.PI / 180.0);
        const double ArrowAngle = 30.0; //degrees

        #region Implementation of IViewerObject

        public DrawingObject DrawingObject {
            get { return Edge; }
        }

        public bool MarkedForDragging { get; set; }

#pragma warning disable 67
        public event EventHandler MarkForDraggingEvent;

        public event EventHandler UnmarkForDraggingEvent;
#pragma warning restore 67

        #endregion

        #region Implementation of IViewerEdge

        public Edge Edge { get; private set; }
        public IViewerNode Source { get; private set; }
        public IViewerNode Target { get; private set; }
        public double RadiusOfPolylineCorner { get; set; }

        public VLabel VLabel { get; set; }

        #endregion

        internal void Invalidate(Control fe, Rail rail, byte edgeTransparency) {
            var path = fe as Path;
            if (path != null)
                SetPathStrokeToRailPath(rail, path, edgeTransparency);
        }
        public void Invalidate() {
            var vis = Edge.IsVisible;
            foreach (var fe in Controls) fe.IsVisible = vis;
            if (!vis)
                return;
            CurvePath.Data = GetICurveAvaloniaGeometry(Edge.GeometryEdge.Curve);
            if (Edge.Attr.ArrowAtSource)
                SourceArrowHeadPath.Data = DefiningSourceArrowHead();
            if (Edge.Attr.ArrowAtTarget)
                TargetArrowHeadPath.Data = DefiningTargetArrowHead(Edge.GeometryEdge.EdgeGeometry, PathStrokeThickness);
            SetPathStroke();
            if (VLabel != null)
                ((IInvalidatable) VLabel).Invalidate();
        }

        void SetPathStroke() {
            SetPathStrokeToPath(CurvePath);
            if (SourceArrowHeadPath != null) {
                SourceArrowHeadPath.Stroke = SourceArrowHeadPath.Fill = Common.BrushFromMsaglColor(Edge.Attr.Color);
                SourceArrowHeadPath.StrokeThickness = PathStrokeThickness;
            }
            if (TargetArrowHeadPath != null) {
                TargetArrowHeadPath.Stroke = TargetArrowHeadPath.Fill = Common.BrushFromMsaglColor(Edge.Attr.Color);
                TargetArrowHeadPath.StrokeThickness = PathStrokeThickness;
            }
        }

        void SetPathStrokeToRailPath(Rail rail, Path path, byte transparency) {

            path.Stroke = SetStrokeColorForRail(transparency, rail);
            path.StrokeThickness = PathStrokeThickness;

            foreach (var style in Edge.Attr.Styles) {
                if (style == Drawing.Style.Dotted) {
                    path.StrokeDashArray = new AvaloniaList<double>() {1, 1};
                } else if (style == Drawing.Style.Dashed) {
                    var f = DashSize();
                    path.StrokeDashArray = new AvaloniaList<double>() {f, f};
                    //CurvePath.StrokeDashOffset = f;
                }
            }
        }

        IBrush SetStrokeColorForRail(byte transparency, Rail rail) {
            return rail.IsHighlighted == false
                ? new SolidColorBrush(new Avalonia.Media.Color(transparency, Edge.Attr.Color.R, Edge.Attr.Color.G,
                    Edge.Attr.Color.B))
                : Brushes.Red;
        }

        void SetPathStrokeToPath(Path path) {
            path.Stroke = Common.BrushFromMsaglColor(Edge.Attr.Color);
            path.StrokeThickness = PathStrokeThickness;

            foreach (var style in Edge.Attr.Styles) {
                if (style == Drawing.Style.Dotted) {
                    path.StrokeDashArray = new AvaloniaList<double>() {1, 1};
                } else if (style == Drawing.Style.Dashed) {
                    var f = DashSize();
                    path.StrokeDashArray = new AvaloniaList<double>() {f, f};
                    //CurvePath.StrokeDashOffset = f;
                }
            }
        }

        public override string ToString() {
            return Edge.ToString();
        }

        internal static double _dashSize = 0.05; //inches
        internal Func<double> PathStrokeThicknessFunc;

        public VEdge(Edge edge, LgLayoutSettings lgSettings) {
            Edge = edge;
            EdgeAttrClone = edge.Attr.Clone();
        }

        internal double DashSize()
        {
            var w = PathStrokeThickness;
            var dashSizeInPoints = _dashSize * GraphViewer.DpiXStatic;
            return dashSizeInPoints / w;
        }

        internal void RemoveItselfFromCanvas(Canvas graphCanvas) {
            if(CurvePath!=null)
                graphCanvas.Children.Remove(CurvePath);

            if (SourceArrowHeadPath != null)
                graphCanvas.Children.Remove(SourceArrowHeadPath);

            if (TargetArrowHeadPath != null)
                graphCanvas.Children.Remove(TargetArrowHeadPath);

            if(VLabel!=null)
                graphCanvas.Children.Remove(VLabel.Control );

        }

        public Control CreateControlForRail(Rail rail, byte edgeTransparency) {
            var iCurve = rail.Geometry as ICurve;
            Path fe;
            if (iCurve != null) {
                fe = (Path)CreateControlForRailCurve(rail, iCurve, edgeTransparency);
            }
            else {
                var arrowhead = rail.Geometry as Arrowhead;
                if (arrowhead != null) {
                    fe = (Path)CreateControlForRailArrowhead(rail, arrowhead, rail.CurveAttachmentPoint, edgeTransparency);
                }
                else
                    throw new InvalidOperationException();
            }
            fe.Tag = rail;
            return fe;
        }

        Control CreateControlForRailArrowhead(Rail rail, Arrowhead arrowhead, Point curveAttachmentPoint, byte edgeTransparency) {
            var streamGeometry = new StreamGeometry();

            using (StreamGeometryContext context = streamGeometry.Open()) {
                AddArrow(context, curveAttachmentPoint, arrowhead.TipPosition,
                         PathStrokeThickness);

            }

            var path=new Path
            {
                Data = streamGeometry,
                Tag = this
            };

            SetPathStrokeToRailPath(rail, path,edgeTransparency);
            return path;
        }

        Control CreateControlForRailCurve(Rail rail, ICurve iCurve, byte transparency) {
            var path = new Path
            {
                Data = GetICurveAvaloniaGeometry(iCurve),
            };
            SetPathStrokeToRailPath(rail, path, transparency);

            return path;
        }
    }
}
