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
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Msagl.Core;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.Layout.LargeGraphLayout;
using Microsoft.Msagl.Miscellaneous;
using Microsoft.Msagl.Miscellaneous.LayoutEditing;
using DrawingEdge = Microsoft.Msagl.Drawing.Edge;
using ILabeledObject = Microsoft.Msagl.Drawing.ILabeledObject;
using Label = Microsoft.Msagl.Drawing.Label;
using ModifierKeys = Microsoft.Msagl.Drawing.ModifierKeys;
using Node = Microsoft.Msagl.Core.Layout.Node;
using Point = Microsoft.Msagl.Core.Geometry.Point;
using Rectangle = Microsoft.Msagl.Core.Geometry.Rectangle;
using Size = Avalonia.Size;
using AvaloniaPoint = Avalonia.Point;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Edge = Microsoft.Msagl.Core.Layout.Edge;
    using Ellipse = Avalonia.Controls.Shapes.Ellipse;
    using LineSegment = Microsoft.Msagl.Core.Geometry.Curves.LineSegment;


namespace Microsoft.Msagl.AvaloniaGraphControl {
    public class GraphViewer : IViewer {
        Path _targetArrowheadPathForRubberEdge;

        Path _rubberEdgePath;
        Path _rubberLinePath;
        Point _sourcePortLocationForEdgeRouting;
        //AvaloniaPoint _objectUnderMouseDetectionLocation;
        CancelToken _cancelToken = new CancelToken();
        BackgroundWorker _backgroundWorker;
        Point _mouseDownPositionInGraph;
        bool _mouseDownPositionInGraph_initialized;

        Ellipse _sourcePortCircle;
        protected Ellipse TargetPortCircle { get; set; }

        AvaloniaPoint _objectUnderMouseDetectionLocation;
        public event EventHandler LayoutStarted;
        public event EventHandler LayoutComplete;

        /*
                readonly DispatcherTimer layoutThreadCheckingTimer = new DispatcherTimer();
        */

        /// <summary>
        /// if set to true will layout in a task
        /// </summary>
        public bool RunLayoutAsync;

        readonly Canvas _graphCanvas = new Canvas();
        Graph _drawingGraph;

        readonly Dictionary<DrawingObject, Control> drawingObjectsToControls =
            new Dictionary<DrawingObject, Control>();

        readonly LayoutEditor layoutEditor;


        GeometryGraph geometryGraphUnderLayout;
        /*
                Thread layoutThread;
        */
        bool needToCalculateLayout = true;


        object _objectUnderMouseCursor;

        static double _dpiX;
        static int _dpiY;

        readonly Dictionary<DrawingObject, IViewerObject> drawingObjectsToIViewerObjects =
            new Dictionary<DrawingObject, IViewerObject>();

        Control _rectToFillGraphBackground;
        Avalonia.Controls.Shapes.Rectangle _rectToFillCanvas;


        GeometryGraph GeomGraph {
            get { return _drawingGraph.GeometryGraph; }
        }

        /// <summary>
        /// the canvas to draw the graph
        /// </summary>
        public Canvas GraphCanvas {
            get { return _graphCanvas; }
        }

        private AvaloniaPoint lastKnownMousePos;

        public GraphViewer() {
            //LargeGraphNodeCountThreshold = 0;
            layoutEditor = new LayoutEditor(this);

            _graphCanvas.RenderTransformOrigin = RelativePoint.TopLeft;
            _graphCanvas.RenderTransform = new MatrixTransform();

            _graphCanvas.SizeChanged += GraphCanvasSizeChanged;
            _graphCanvas.PointerPressed += GraphCanvasPointerPressed;
            _graphCanvas.PointerMoved += GraphCanvasPointerMoved;

            _graphCanvas.PointerReleased += GraphCanvasPointerReleased;
            _graphCanvas.PointerWheelChanged += GraphCanvasPointerWheel;
            ViewChangeEvent += AdjustBtrectRenderTransform;

            _graphCanvas.KeyDown += GraphCanvasKeyDown;
            _graphCanvas.KeyUp += GraphCanvasKepUp;

            LayoutEditingEnabled = true;
            clickCounter = new ClickCounter(() => lastKnownMousePos);
            clickCounter.Elapsed += ClickCounterElapsed;
        }


        #region Avalonia stuff

        /// <summary>
        /// adds the main panel of the viewer to the children of the parent
        /// </summary>
        /// <param name="panel"></param>
        public void BindToPanel(Panel panel) {
            panel.Children.Add(GraphCanvas);
            panel.PointerMoved += GraphCanvasPointerMoved;
            panel.PointerPressed += GraphCanvasPointerPressed;
            panel.PointerReleased += GraphCanvasPointerReleased;
            panel.PointerWheelChanged += GraphCanvasPointerWheel;
            GraphCanvas.UpdateLayout();
        }


        void ClickCounterElapsed(object sender, EventArgs e) {
            var vedge = clickCounter.ClickedObject as VEdge;
            if (vedge != null) {
                if (clickCounter.UpCount == clickCounter.DownCount && clickCounter.UpCount == 1)
                    HandleClickForEdge(vedge);
            }
            clickCounter.ClickedObject = null;
        }



        void AdjustBtrectRenderTransform(object sender, EventArgs e) {
            if (_rectToFillCanvas == null)
                return;
            _rectToFillCanvas.RenderTransform = new MatrixTransform(_graphCanvas.RenderTransform.Value.Invert());
            var parent = (Panel)GraphCanvas.Parent;
            _rectToFillCanvas.Width = parent.Bounds.Width;
            _rectToFillCanvas.Height = parent.Bounds.Height;

        }

        void HandleClickForEdge(VEdge vEdge) {
            //todo : add a hook
            var lgSettings = Graph.LayoutAlgorithmSettings as LgLayoutSettings;
            if (lgSettings != null) {
                var lgEi = lgSettings.GeometryEdgesToLgEdgeInfos[vEdge.Edge.GeometryEdge];
                lgEi.SlidingZoomLevel = lgEi.SlidingZoomLevel != 0 ? 0 : double.PositiveInfinity;

                ViewChangeEvent(null, null);
            }
        }



        /*
                Tuple<string, VoidDelegate>[] CreateToggleZoomLevelMenuCoupleForNode(VNode vNode) {
                    var list = new List<Tuple<string, VoidDelegate>>();
                    var lgSettings = Graph.LayoutAlgorithmSettings as LgLayoutSettings;
                    if (lgSettings != null) {
                        var lgNodeInfo = lgSettings.GeometryNodesToLgNodeInfos[(Node) vNode.DrawingObject.GeometryObject];
                        list.Add(ToggleZoomLevelMenuCouple(lgNodeInfo));
                        list.Add(MakeAllNodeEdgesAlwaysVisible(lgNodeInfo));
                        list.Add(RestoreAllNodeEdgesZoomLevels(lgNodeInfo));

                        return list.ToArray();
                    }
                    return null;
                }
        */

        /*
                Tuple<string, VoidDelegate> RestoreAllNodeEdgesZoomLevels(LgNodeInfo lgNodeInfo) {
                    const string title = "Restore zoom levels of adjacent edges";

                    var lgSettings = (LgLayoutSettings) Graph.LayoutAlgorithmSettings;

                    return new Tuple<string, VoidDelegate>(title, () => {
                        foreach (var edge in lgNodeInfo.GeometryNode.Edges) {
                            var lgEi = lgSettings.GeometryEdgesToLgEdgeInfos[edge];
                            lgEi.SlidingZoomLevel = double.PositiveInfinity;
                        }
                        ViewChangeEvent(null, null);
                    });
                }
        */

        /*
                Tuple<string, VoidDelegate> MakeAllNodeEdgesAlwaysVisible(LgNodeInfo lgNodeInfo) {
                    const string title = "Set zoom levels for adjacent edges to 1";

                    var lgSettings = (LgLayoutSettings) Graph.LayoutAlgorithmSettings;

                    return new Tuple<string, VoidDelegate>(title, () => {
                        foreach (var edge in lgNodeInfo.GeometryNode.Edges) {
                            var lgEi = lgSettings.GeometryEdgesToLgEdgeInfos[edge];
                            lgEi.SlidingZoomLevel = 1;
                        }
                        ViewChangeEvent(null, null);
                    });
                }
        */

        /*
                Tuple<string, VoidDelegate> CreateToggleZoomLevelMenuCoupleForEdge(VEdge vedge) {
                    var lgSettings = (LgLayoutSettings) Graph.LayoutAlgorithmSettings;
                    var lgEdgeInfo = lgSettings.GeometryEdgesToLgEdgeInfos[(Edge) vedge.DrawingObject.GeometryObject];

                    return ToggleZoomLevelMenuCouple(lgEdgeInfo);
                }
        */

        /*
                Tuple<string, VoidDelegate> ToggleZoomLevelMenuCouple(LgInfoBase lgEdgeInfo) {
                    string title;
                    double newZoomLevel;
                    if (lgEdgeInfo.ZoomLevel > 0) {
                        title = "Make always visible";
                        newZoomLevel = 0;
                    } else {
                        title = "Restore zoom level";
                        newZoomLevel = double.PositiveInfinity;
                    }

                    return new Tuple<string, VoidDelegate>(title, () => {
                        lgEdgeInfo.SlidingZoomLevel = newZoomLevel;
                        ViewChangeEvent(null, null);
                    });
                }
        */

        void GraphCanvasPointerWheel(object sender, PointerWheelEventArgs e) {
            if (e.Delta.Y != 0) {
                const double zoomFractionLocal = 0.9;
                var zoomInc = e.Delta.Y < 0 ? zoomFractionLocal : 1.0 / zoomFractionLocal;
                ZoomAbout(ZoomFactor * zoomInc, e.GetPosition(_graphCanvas));
                e.Handled = true;
            }
        }

        /// <summary>
        /// keeps centerOfZoom pinned to the screen and changes the scale by zoomFactor
        /// </summary>
        /// <param name="zoomFactor"></param>
        /// <param name="centerOfZoom"></param>
        public void ZoomAbout(double zoomFactor, AvaloniaPoint centerOfZoom) {
            if (_graphCanvas.Parent is not Visual parentVisual) {
                return;
            }
            var scale = zoomFactor * FitFactor;
            var centerOfZoomOnScreen =
                _graphCanvas.TransformToVisual(parentVisual)?.Transform(centerOfZoom) ?? new AvaloniaPoint();
            SetTransform(scale, centerOfZoomOnScreen.X - centerOfZoom.X * scale,
                         centerOfZoomOnScreen.Y + centerOfZoom.Y * scale);
        }

        public LayoutEditor LayoutEditor {
            get { return layoutEditor; }
        }

        void GraphCanvasPointerPressed(object sender, PointerPressedEventArgs e) {
            clickCounter.AddMouseDown(_objectUnderMouseCursor);
            if (MouseDown != null)
                MouseDown(this, CreatePointerEventArgs(e));

            if (e.Handled) return;
            _mouseDownPositionInGraph = Common.MsaglPoint(e.GetPosition(_graphCanvas));
            _mouseDownPositionInGraph_initialized = true;
        }


        void GraphCanvasPointerMoved(object sender, PointerEventArgs e) {
            lastKnownMousePos = e.GetPosition((Visual)_graphCanvas.Parent);

            if (MouseMove != null)
                MouseMove(this, CreatePointerEventArgs(e));

            if (e.Handled) return;


            if (e.Properties.IsMiddleButtonPressed && (!LayoutEditingEnabled || _objectUnderMouseCursor == null)) {
                if (!_mouseDownPositionInGraph_initialized) {
                    _mouseDownPositionInGraph = Common.MsaglPoint(e.GetPosition(_graphCanvas));
                    _mouseDownPositionInGraph_initialized = true;
                }

                Pan(e);
            }
            else {
                // Retrieve the coordinate of the mouse position.
                AvaloniaPoint mouseLocation = e.GetPosition(_graphCanvas);
                // Clear the contents of the list used for hit test results.
                ObjectUnderMouseCursor = (_graphCanvas.InputHitTest(mouseLocation) as Control)?.Tag as IViewerObject;
            }
        }

        private void GraphCanvasKepUp(object sender, KeyEventArgs e) {
            ModifierKeys = (ModifierKeys)e.KeyModifiers;
        }

        private void GraphCanvasKeyDown(object sender, KeyEventArgs e) {
            ModifierKeys = (ModifierKeys)e.KeyModifiers;
        }


        Control GetControlFromIViewerObject(IViewerObject viewerObject) {
            Control ret;

            var vNode = viewerObject as VNode;
            if (vNode != null) ret = vNode.ControlOfNodeForLabel ?? vNode.BoundaryPath;
            else {
                var vLabel = viewerObject as VLabel;
                if (vLabel != null) ret = vLabel.Control;
                else {
                    var vEdge = viewerObject as VEdge;
                    if (vEdge != null) ret = vEdge.CurvePath;
                    else {
                        throw new InvalidOperationException(
                            "Unexpected object type in GraphViewer"
                            );
                    }
                }
            }
            if (ret == null)
                throw new InvalidOperationException(
                    "did not find a framework element!"
                    );

            return ret;
        }


        protected double MouseHitTolerance {
            get { return (0.05) * DpiX / CurrentScale; }

        }
        /// <summary>
        /// this function pins the sourcePoint to screenPoint
        /// </summary>
        /// <param name="screenPoint"></param>
        /// <param name="sourcePoint"></param>
        void SetTransformFromTwoPoints(AvaloniaPoint screenPoint, Point sourcePoint) {
            var scale = CurrentScale;
            SetTransform(scale, screenPoint.X - scale * sourcePoint.X, screenPoint.Y + scale * sourcePoint.Y);
        }
        /// <summary>
        /// Moves the point to the center of the viewport
        /// </summary>
        /// <param name="sourcePoint"></param>
        public void PointToCenter(Point sourcePoint) {
            AvaloniaPoint center = new AvaloniaPoint(_graphCanvas.Bounds.Width / 2, _graphCanvas.Bounds.Height / 2);
            SetTransformFromTwoPoints(center, sourcePoint);
        }
        public void NodeToCenterWithScale(Drawing.Node node, double scale) {
            if (node.GeometryNode == null) return;
            var screenPoint = new AvaloniaPoint(_graphCanvas.Bounds.Width / 2, _graphCanvas.Bounds.Height / 2);
            var sourcePoint = node.BoundingBox.Center;
            SetTransform(scale, screenPoint.X - scale * sourcePoint.X, screenPoint.Y + scale * sourcePoint.Y);
        }

        public void NodeToCenter(Drawing.Node node) {
            if (node.GeometryNode == null) return;
            PointToCenter(node.GeometryNode.Center);
        }

        void Pan(PointerEventArgs e) {
            if (UnderLayout)
                return;

            if (!ReferenceEquals(e.Pointer.Captured, _graphCanvas))
                e.Pointer.Capture(_graphCanvas);


            SetTransformFromTwoPoints(e.GetPosition((Control)_graphCanvas.Parent),
                    _mouseDownPositionInGraph);

            if (ViewChangeEvent != null)
                ViewChangeEvent(null, null);
        }

        //        [System.Runtime.InteropServices.DllImportAttribute("user32.dll", EntryPoint = "SetCursorPos")]
        //        [return: System.Runtime.InteropServices.MarshalAsAttribute(System.Runtime.InteropServices.UnmanagedType.Bool)]
        //        public static extern bool SetCursorPos(int X, int Y);


        public double CurrentScale {
            get { return ((MatrixTransform)_graphCanvas.RenderTransform).Matrix.M11; }
        }

        /*
                void Pan(Point vector) {


                    graphCanvas.RenderTransform = new MatrixTransform(mouseDownTransform[0, 0], mouseDownTransform[0, 1],
                                                                      mouseDownTransform[1, 0], mouseDownTransform[1, 1],
                                                                      mouseDownTransform[0, 2] +vector.X,
                                                                      mouseDownTransform[1, 2] +vector.Y);
                }
        */

        internal MsaglMouseEventArgs CreatePointerEventArgs(PointerEventArgs e) {
            return new GvPointerEventArgs(e, this);
        }

        void GraphCanvasPointerReleased(object sender, PointerEventArgs e) {
            OnMouseUp(e);
            clickCounter.AddMouseUp();
            if (ReferenceEquals(e.Pointer.Captured, _graphCanvas)) {
                e.Handled = true;
                e.Pointer.Capture(null);
            }
        }

        void OnMouseUp(PointerEventArgs e) {
            if (MouseUp != null)
                MouseUp(this, CreatePointerEventArgs(e));
        }

        void GraphCanvasSizeChanged(object sender, SizeChangedEventArgs e) {
            if (_drawingGraph == null) return;
            // keep the same zoom level
            double oldfit = GetFitFactor(e.PreviousSize);
            double fitNow = FitFactor;
            double scaleFraction = fitNow / oldfit;
            SetTransform(CurrentScale * scaleFraction, CurrentXOffset * scaleFraction, CurrentYOffset * scaleFraction);
        }

        protected double CurrentXOffset {
            get { return ((MatrixTransform)_graphCanvas.RenderTransform).Matrix.M31; }
        }

        protected double CurrentYOffset {
            get { return ((MatrixTransform)_graphCanvas.RenderTransform).Matrix.M32; }
        }

        /// <summary>
        ///
        /// </summary>
        public double ZoomFactor {
            get { return CurrentScale / FitFactor; }
        }

        #endregion

        #region IViewer stuff

        public event EventHandler<EventArgs> ViewChangeEvent;
        public event EventHandler<MsaglMouseEventArgs> MouseDown;
        public event EventHandler<MsaglMouseEventArgs> MouseMove;
        public event EventHandler<MsaglMouseEventArgs> MouseUp;
        public event EventHandler<ObjectUnderMouseCursorChangedEventArgs> ObjectUnderMouseCursorChanged;

        public IViewerObject ObjectUnderMouseCursor {
            get {
                // this function can bring a stale object
                return GetIViewerObjectFromObjectUnderCursor(_objectUnderMouseCursor);
            }
            private set {
                var old = _objectUnderMouseCursor;
                bool callSelectionChanged = _objectUnderMouseCursor != value && ObjectUnderMouseCursorChanged != null;

                _objectUnderMouseCursor = value;

                if (callSelectionChanged)
                    ObjectUnderMouseCursorChanged(this,
                                                  new ObjectUnderMouseCursorChangedEventArgs(
                                                      GetIViewerObjectFromObjectUnderCursor(old),
                                                      GetIViewerObjectFromObjectUnderCursor(_objectUnderMouseCursor)));
            }
        }

        IViewerObject GetIViewerObjectFromObjectUnderCursor(object obj) {
            if (obj == null)
                return null;
            return obj as IViewerObject;
        }

        public void Invalidate(IViewerObject objectToInvalidate) {
            ((IInvalidatable)objectToInvalidate).Invalidate();
        }

        public void Invalidate() {
            //todo: is it right to do nothing
        }

        public event EventHandler GraphChanged;

        public ModifierKeys ModifierKeys { get; private set; }

        public Point ScreenToSource(MsaglMouseEventArgs e) {
            var p = new Point(e.X, e.Y);
            var m = Transform.Inverse;
            return m * p;
        }


        public IEnumerable<IViewerObject> Entities {
            get {
                foreach (var viewerObject in drawingObjectsToIViewerObjects.Values) {
                    yield return viewerObject;
                    var edge = viewerObject as VEdge;
                    if (edge != null)
                        if (edge.VLabel != null)
                            yield return edge.VLabel;
                }
            }
        }

        internal static double DpiXStatic {
            get {
                if (_dpiX == 0)
                    GetDpi();
                return _dpiX;
            }
        }

        static void GetDpi() {
            // This required platform-specific native calls, so has been removed to make project cross-platform.
            // May need changing in future if there are issues on different display scales.
            _dpiX = 96;
            _dpiY = 96;
        }

        public double DpiX {
            get { return DpiXStatic; }
        }

        public double DpiY {
            get { return DpiYStatic; }
        }

        static double DpiYStatic {
            get {
                if (_dpiX == 0)
                    GetDpi();
                return _dpiY;
            }
        }

        public void OnDragEnd(IEnumerable<IViewerObject> changedObjects) {
            throw new NotImplementedException();
        }

        public double LineThicknessForEditing { get; set; }

        /// <summary>
        /// the layout editing with the mouse is enabled if and only if this field is set to false
        /// </summary>
        public bool LayoutEditingEnabled { get; set; }

        public bool InsertingEdge { get; set; }

        public void PopupMenus(params Tuple<string, VoidDelegate>[] menuItems) {
            var contextMenu = new ContextMenu();
            foreach (var pair in menuItems)
                contextMenu.Items.Add(CreateMenuItem(pair.Item1, pair.Item2));
            contextMenu.Closed += ContextMenuClosed;
            _graphCanvas.ContextMenu = contextMenu;

        }

        void ContextMenuClosed(object sender, RoutedEventArgs e) {
            _graphCanvas.ContextMenu = null;
        }

        public static object CreateMenuItem(string title, VoidDelegate voidVoidDelegate) {
            var menuItem = new MenuItem { Header = title };
            menuItem.Click += (EventHandler<RoutedEventArgs>)(delegate { voidVoidDelegate(); });
            return menuItem;
        }

        public double UnderlyingPolylineCircleRadius {
            get { return 0.1 * DpiX / CurrentScale; }
        }

        public Graph Graph {
            get { return _drawingGraph; }
            set {
                _drawingGraph = value;
                ProcessGraph();
            }
        }
        //
        //        void Dumpxy() {
        //            using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"C:\tmp\dumpxy")) {
        //                file.WriteLine("~nodes");
        //                foreach (var node in Graph.Nodes) {
        //                    var c = node.GeometryNode.Center;
        //                    file.WriteLine("{0} {1} {2}", node.Id, c.X, c.Y);
        //                }
        //                file.WriteLine("~edges");
        //                foreach (var edge in Graph.Edges)
        //                {
        //                    file.WriteLine("{0} {1}", edge.Source, edge.Target);
        //                }
        //            }
        //        }


        const double DesiredPathThicknessInInches = 0.008;

        readonly Dictionary<DrawingObject, Func<DrawingObject, Control>> registeredCreators =
            new Dictionary<DrawingObject, Func<DrawingObject, Control>>();

        readonly ClickCounter clickCounter;
        public string MsaglFileToSave;

        double GetBorderPathThickness() {
            return DesiredPathThicknessInInches * DpiX;
        }

        readonly Object _processGraphLock = new object();
        void ProcessGraph() {
            lock (_processGraphLock) {
                ProcessGraphUnderLock();
            }
        }

        void ProcessGraphUnderLock() {
            try {
                if (LayoutStarted != null)
                    LayoutStarted(null, null);

                CancelToken = new CancelToken();

                if (_drawingGraph == null)
                    _drawingGraph = new Graph(); // assign an empty graph

                HideCanvas();
                ClearGraphViewer();
                CreateControlsForLabelsOnly();
                if (NeedToCalculateLayout) {
                    _drawingGraph.CreateGeometryGraph(); //forcing the layout recalculation
                    if (_graphCanvas.Dispatcher.CheckAccess())
                        PopulateGeometryOfGeometryGraph();
                    else
                        _graphCanvas.Dispatcher.Invoke(PopulateGeometryOfGeometryGraph);
                }

                geometryGraphUnderLayout = _drawingGraph.GeometryGraph;
                if (RunLayoutAsync)
                    SetUpBackgrounWorkerAndRunAsync();
                else
                    RunLayoutInUIThread();
            }
            catch (Exception e) {
                Console.Error.WriteLine(e.ToString());
            }
        }

        void RunLayoutInUIThread() {
            LayoutGraph();
            PostLayoutStep();
            if (LayoutComplete != null)
                LayoutComplete(null, null);
        }

        void SetUpBackgrounWorkerAndRunAsync() {
            _backgroundWorker = new BackgroundWorker();
            _backgroundWorker.DoWork += (a, b) => LayoutGraph();
            _backgroundWorker.RunWorkerCompleted += (sender, args) => {
                if (args.Error != null) {
                    Console.Error.WriteLine(args.Error.ToString());
                    ClearGraphViewer();
                }
                else if (CancelToken.Canceled) {
                    ClearGraphViewer();
                }
                else {
                    if (_graphCanvas.Dispatcher.CheckAccess())
                        PostLayoutStep();
                    else
                        _graphCanvas.Dispatcher.Invoke(PostLayoutStep);
                }
                _backgroundWorker = null; //this will signal that we are not under layout anymore
                if (LayoutComplete != null)
                    LayoutComplete(null, null);
            };
            _backgroundWorker.RunWorkerAsync();
        }

        void HideCanvas() {
            if (_graphCanvas.Dispatcher.CheckAccess())
                _graphCanvas.IsVisible = false; // hide canvas while we lay it out asynchronously.
            else
                _graphCanvas.Dispatcher.Invoke(() => _graphCanvas.IsVisible = false);
        }


        void LayoutGraph() {
            if (NeedToCalculateLayout) {
                try {
                    LayoutHelpers.CalculateLayout(geometryGraphUnderLayout, _drawingGraph.LayoutAlgorithmSettings,
                                                  CancelToken);
                    if (MsaglFileToSave != null) {
                        _drawingGraph.Write(MsaglFileToSave);
                        Environment.Exit(0);
                    }
                }
                catch (OperationCanceledException) {
                    //swallow this exception
                }
            }
        }

        void PostLayoutStep() {
            _graphCanvas.IsVisible = true;
            PushDataFromLayoutGraphToControls();
            _backgroundWorker = null; //this will signal that we are not under layout anymore
            if (GraphChanged != null)
                GraphChanged(this, null);
        }

        /// <summary>
        /// creates a viewer node
        /// </summary>
        /// <param name="drawingNode"></param>
        /// <returns></returns>
        public IViewerNode CreateIViewerNode(Drawing.Node drawingNode) {
            var frameworkElement = CreateTextBlockForDrawingObj(drawingNode);
            var width = frameworkElement.Width + 2 * drawingNode.Attr.LabelMargin;
            var height = frameworkElement.Height + 2 * drawingNode.Attr.LabelMargin;
            var bc = NodeBoundaryCurves.GetNodeBoundaryCurve(drawingNode, width, height);
            drawingNode.GeometryNode = new Node(bc, drawingNode);
            var vNode = CreateVNode(drawingNode);
            layoutEditor.AttachLayoutChangeEvent(vNode);
            return vNode;
        }

        void ClearGraphViewer() {
            ClearGraphCanvasChildren();

            drawingObjectsToIViewerObjects.Clear();
            drawingObjectsToControls.Clear();
        }

        void ClearGraphCanvasChildren() {
            if (_graphCanvas.Dispatcher.CheckAccess())
                _graphCanvas.Children.Clear();
            else _graphCanvas.Dispatcher.Invoke(() => _graphCanvas.Children.Clear());
        }

        /// <summary>
        /// zooms to the default view
        /// </summary>
        public void SetInitialTransform() {
            if (_drawingGraph == null || GeomGraph == null) return;

            var scale = FitFactor;
            var graphCenter = GeomGraph.BoundingBox.Center;
            var vp = new Rectangle(new Point(0, 0),
                                   new Point(_graphCanvas.Bounds.Width, _graphCanvas.Bounds.Height));

            SetTransformOnViewportWithoutRaisingViewChangeEvent(scale, graphCenter, vp);
        }

        public Image DrawImage(string fileName) {
            var rtrans = _graphCanvas.RenderTransform;
            _graphCanvas.RenderTransform = null;
            var renderSize = _graphCanvas.Bounds;

            double scale = FitFactor;
            int w = (int)(GeomGraph.Width * scale);
            int h = (int)(GeomGraph.Height * scale);

            SetTransformOnViewportWithoutRaisingViewChangeEvent(scale, GeomGraph.BoundingBox.Center, new Rectangle(0, 0, w, h));

            Size size = new Size(w, h);
            // Measure and arrange the surface
            // VERY IMPORTANT
            _graphCanvas.Measure(size);
            _graphCanvas.Arrange(new Rect(size));

            foreach (var node in _drawingGraph.Nodes.Concat(_drawingGraph.RootSubgraph.AllSubgraphsDepthFirstExcludingSelf())) {
                IViewerObject o;
                if (drawingObjectsToIViewerObjects.TryGetValue(node, out o)) {
                    ((VNode)o).Invalidate();
                }
            }

            RenderTargetBitmap renderBitmap = new RenderTargetBitmap(new PixelSize(w, h), new Vector(DpiX, DpiY));
            renderBitmap.Render(_graphCanvas);

            if (fileName != null)
                // Create a file stream for saving image
                using (System.IO.FileStream outStream = new System.IO.FileStream(fileName, System.IO.FileMode.Create)) {
                    // save the data to the stream
                    renderBitmap.Save(outStream);
                }

            _graphCanvas.RenderTransform = rtrans;
            _graphCanvas.Measure(renderSize.Size);
            _graphCanvas.Arrange(renderSize);

            return new Image { Source = renderBitmap };
        }

        void SetTransformOnViewportWithoutRaisingViewChangeEvent(double scale, Point graphCenter, Rectangle vp) {
            var dx = vp.Width / 2 - scale * graphCenter.X;
            var dy = vp.Height / 2 + scale * graphCenter.Y;

            SetTransformWithoutRaisingViewChangeEvent(scale, dx, dy);

        }

        public Rectangle ClientViewportMappedToGraph {
            get {
                var t = Transform.Inverse;
                var p0 = new Point(0, 0);
                var p1 = new Point(_graphCanvas.Bounds.Width, _graphCanvas.Bounds.Height);
                return new Rectangle(t * p0, t * p1);
            }
        }


        void SetTransform(double scale, double dx, double dy) {
            if (ScaleIsOutOfRange(scale)) return;
            _graphCanvas.RenderTransform = new MatrixTransform(new Matrix(scale, 0, 0, -scale, dx, dy));
            if (ViewChangeEvent != null)
                ViewChangeEvent(null, null);
        }

        void SetTransformWithoutRaisingViewChangeEvent(double scale, double dx, double dy) {
            if (ScaleIsOutOfRange(scale)) return;
            _graphCanvas.RenderTransform = new MatrixTransform(new Matrix(scale, 0, 0, -scale, dx, dy));
        }

        bool ScaleIsOutOfRange(double scale) {
            return scale < 0.000001 || scale > 100000.0; //todo: remove hardcoded values
        }


        double FitFactor {
            get {
                var geomGraph = GeomGraph;
                if (_drawingGraph == null || geomGraph == null ||

                    geomGraph.Width == 0 || geomGraph.Height == 0)
                    return 1;

                var size = _graphCanvas.Bounds;

                return GetFitFactor(size.Size);
            }
        }

        double GetFitFactor(Size rect) {
            var geomGraph = GeomGraph;
            return geomGraph == null ? 1 : Math.Min(rect.Width / geomGraph.Width, rect.Height / geomGraph.Height);
        }

        void PushDataFromLayoutGraphToControls() {
            CreateRectToFillCanvas();
            CreateAndPositionGraphBackgroundRectangle();
            CreateVNodes();

            // Fix wrong initial end point if Target is Subgraph https://github.com/microsoft/automatic-graph-layout/pull/313#issuecomment-1130468914
            LayoutHelpers.RouteAndLabelEdges(
                _drawingGraph.GeometryGraph,
                _drawingGraph.LayoutAlgorithmSettings,
                _drawingGraph.Edges.Select(e => e.GeometryEdge),
                0,
                null);

            CreateEdges();
        }


        void CreateRectToFillCanvas() {
            var parent = (Panel)GraphCanvas.Parent;
            _rectToFillCanvas = new Avalonia.Controls.Shapes.Rectangle();
            Canvas.SetLeft(_rectToFillCanvas, 0);
            Canvas.SetTop(_rectToFillCanvas, 0);
            _rectToFillCanvas.Width = parent.Bounds.Width;
            _rectToFillCanvas.Height = parent.Bounds.Height;

            _rectToFillCanvas.Fill = Brushes.Transparent;
            _rectToFillCanvas.ZIndex = -2;
            _graphCanvas.Children.Add(_rectToFillCanvas);
        }




        void CreateEdges() {
            foreach (var edge in _drawingGraph.Edges)
                CreateEdge(edge, null);
        }

        VEdge CreateEdge(DrawingEdge edge, LgLayoutSettings lgSettings) {
            lock (this) {
                if (drawingObjectsToIViewerObjects.ContainsKey(edge))
                    return (VEdge)drawingObjectsToIViewerObjects[edge];
                if (lgSettings != null)
                    return CreateEdgeForLgCase(lgSettings, edge);

                Control labelTextBox;
                drawingObjectsToControls.TryGetValue(edge, out labelTextBox);
                var vEdge = new VEdge(edge, labelTextBox);

                var zIndex = ZIndexOfEdge(edge);
                drawingObjectsToIViewerObjects[edge] = vEdge;

                if (edge.Label != null)
                    SetVEdgeLabel(edge, vEdge, zIndex);

                vEdge.CurvePath.ZIndex = zIndex;
                _graphCanvas.Children.Add(vEdge.CurvePath);
                SetVEdgeArrowheads(vEdge, zIndex);

                return vEdge;
            }
        }

        int ZIndexOfEdge(DrawingEdge edge) {
            var source = (VNode)drawingObjectsToIViewerObjects[edge.SourceNode];
            var target = (VNode)drawingObjectsToIViewerObjects[edge.TargetNode];

            var zIndex = Math.Max(source.ZIndex, target.ZIndex) + 1;
            return zIndex;
        }

        VEdge CreateEdgeForLgCase(LgLayoutSettings lgSettings, DrawingEdge edge) {
            return (VEdge)(drawingObjectsToIViewerObjects[edge] = new VEdge(edge, lgSettings) {
                PathStrokeThicknessFunc = () => GetBorderPathThickness() * edge.Attr.LineWidth
            });
        }

        void SetVEdgeLabel(DrawingEdge edge, VEdge vEdge, int zIndex) {
            Control frameworkElementForEdgeLabel;
            if (!drawingObjectsToControls.TryGetValue(edge, out frameworkElementForEdgeLabel)) {
                drawingObjectsToControls[edge] =
                    frameworkElementForEdgeLabel = CreateTextBlockForDrawingObj(edge);
                frameworkElementForEdgeLabel.Tag = new VLabel(edge, frameworkElementForEdgeLabel);
            }

            vEdge.VLabel = (VLabel)frameworkElementForEdgeLabel.Tag;
            if (frameworkElementForEdgeLabel.Parent == null) {
                _graphCanvas.Children.Add(frameworkElementForEdgeLabel);
                frameworkElementForEdgeLabel.ZIndex = zIndex;
            }
        }

        void SetVEdgeArrowheads(VEdge vEdge, int zIndex) {
            if (vEdge.SourceArrowHeadPath != null) {
                vEdge.SourceArrowHeadPath.ZIndex = zIndex;
                _graphCanvas.Children.Add(vEdge.SourceArrowHeadPath);
            }
            if (vEdge.TargetArrowHeadPath != null) {
                vEdge.TargetArrowHeadPath.ZIndex = zIndex;
                _graphCanvas.Children.Add(vEdge.TargetArrowHeadPath);
            }
        }

        void CreateVNodes() {
            foreach (var node in _drawingGraph.Nodes.Concat(_drawingGraph.RootSubgraph.AllSubgraphsDepthFirstExcludingSelf())) {
                CreateVNode(node);
                Invalidate(drawingObjectsToIViewerObjects[node]);
            }
        }

        IViewerNode CreateVNode(Drawing.Node node) {
            lock (this) {
                if (drawingObjectsToIViewerObjects.ContainsKey(node))
                    return (IViewerNode)drawingObjectsToIViewerObjects[node];

                Control feOfLabel;
                if (!drawingObjectsToControls.TryGetValue(node, out feOfLabel))
                    feOfLabel = CreateAndRegisterControlOfDrawingNode(node);

                var vn = new VNode(
                    node,
                    feOfLabel,
                    _drawingGraph.LayoutAlgorithmSettings,
                    e => (VEdge)drawingObjectsToIViewerObjects[e],
                    () => GetBorderPathThickness() * node.Attr.LineWidth, this.CreateToolTipForNodes);

                foreach (var fe in vn.Controls)
                    _graphCanvas.Children.Add(fe);

                drawingObjectsToIViewerObjects[node] = vn;

                #region commented out animation

                /* //playing with the animation
                p.Fill = Brushes.Green;

                SolidColorBrush brush = new SolidColorBrush();
                p.Fill = brush;
                ColorAnimation ca = new ColorAnimation(Colors.Green, Colors.White, new Duration(TimeSpan.FromMilliseconds(3000)));
                //Storyboard sb = new Storyboard();
                //Storyboard.SetTargetProperty(ca, new PropertyPath("Color"));
                //Storyboard.SetTarget(ca, brush);
                //sb.Children.Add(ca);
                //sb.Begin(p);
                brush.BeginAnimation(SolidColorBrush.ColorProperty, ca);
                */

                #endregion

                return vn;
            }
        }

        public Control CreateAndRegisterControlOfDrawingNode(Drawing.Node node) {
            lock (this)
                return drawingObjectsToControls[node] = CreateTextBlockForDrawingObj(node);
        }

        void CreateAndPositionGraphBackgroundRectangle() {
            CreateGraphBackgroundRect();
            SetBackgroundRectanglePositionAndSize();

            var rect = _rectToFillGraphBackground as Avalonia.Controls.Shapes.Rectangle;
            if (rect != null) {
                rect.Fill = Common.BrushFromMsaglColor(_drawingGraph.Attr.BackgroundColor);
                //rect.Fill = Brushes.Green;
            }
            _rectToFillGraphBackground.ZIndex = -1;
            _graphCanvas.Children.Add(_rectToFillGraphBackground);
        }

        void CreateGraphBackgroundRect() {
            var lgGraphBrowsingSettings = _drawingGraph.LayoutAlgorithmSettings as LgLayoutSettings;
            if (lgGraphBrowsingSettings == null) {
                _rectToFillGraphBackground = new Avalonia.Controls.Shapes.Rectangle();
            }
        }


        void SetBackgroundRectanglePositionAndSize() {
            if (GeomGraph == null) return;
            //            Canvas.SetLeft(_rectToFillGraphBackground, geomGraph.Left);
            //            Canvas.SetTop(_rectToFillGraphBackground, geomGraph.Bottom);
            _rectToFillGraphBackground.Width = GeomGraph.Width;
            _rectToFillGraphBackground.Height = GeomGraph.Height;

            var center = GeomGraph.BoundingBox.Center;
            Common.PositionControl(_rectToFillGraphBackground, center, 1);
        }


        void PopulateGeometryOfGeometryGraph() {
            geometryGraphUnderLayout = _drawingGraph.GeometryGraph;
            foreach (
                Node msaglNode in
                    geometryGraphUnderLayout.Nodes) {
                var node = (Drawing.Node)msaglNode.UserData;
                if (_graphCanvas.Dispatcher.CheckAccess())
                    msaglNode.BoundaryCurve = GetNodeBoundaryCurve(node);
                else {
                    var msagNodeInThread = msaglNode;
                    _graphCanvas.Dispatcher.Invoke(() => msagNodeInThread.BoundaryCurve = GetNodeBoundaryCurve(node));
                }
                //AssignLabelWidthHeight(msaglNode, msaglNode.UserData as DrawingObject);
            }

            foreach (
                var cluster in geometryGraphUnderLayout.RootCluster.AllClustersWideFirstExcludingSelf()) {
                var subgraph = (Subgraph) cluster.UserData;

                if (_graphCanvas.Dispatcher.CheckAccess())
                    cluster.CollapsedBoundary = GetClusterCollapsedBoundary(subgraph);
                else {
                    var clusterInThread = cluster;
                    _graphCanvas.Dispatcher.Invoke(
                        () => clusterInThread.BoundaryCurve = GetClusterCollapsedBoundary(subgraph));
                }

                if (cluster.RectangularBoundary == null) {
                    var lp = subgraph.Attr.ClusterLabelMargin;
                    var margin = subgraph.Attr.LabelMargin;

                    var label =
                        MeasureText(
                            subgraph.LabelText,
                            new FontFamily(subgraph.Label.FontName),
                            subgraph.Label.FontSize,
                            drawingObjectsToControls[subgraph]);

                    var offset =
                        lp == LabelPlacement.Top
                            ? subgraph.DiameterOfOpenCollapseButton         // to not overlap CollapseButton if Label on Top
                            : 0;

                    cluster.RectangularBoundary = new RectangularClusterBoundary {
                        MinHeight    = label.Height + 2 * margin,
                        MinWidth     = label.Width  + 2 * margin + offset,
                        TopMargin    = lp == LabelPlacement.Top    ? label.Height : margin,
                        BottomMargin = lp == LabelPlacement.Bottom ? label.Height : margin,
                        LeftMargin   = lp == LabelPlacement.Left   ? label.Width  : margin,
                        RightMargin  = lp == LabelPlacement.Right  ? label.Width  : margin,
                    };
                }

                cluster.RectangularBoundary.TopMargin =
                    subgraph.DiameterOfOpenCollapseButton + 0.5 + subgraph.Attr.LineWidth/2;

                //AssignLabelWidthHeight(msaglNode, msaglNode.UserData as DrawingObject);
            }

            foreach (var msaglEdge in geometryGraphUnderLayout.Edges) {
                var drawingEdge = (DrawingEdge)msaglEdge.UserData;
                AssignLabelWidthHeight(msaglEdge, drawingEdge);
            }
        }

        ICurve GetClusterCollapsedBoundary(Subgraph subgraph) {
            double width, height;

            Control fe;
            if (drawingObjectsToControls.TryGetValue(subgraph, out fe)) {

                width = fe.Width + 2 * subgraph.Attr.LabelMargin + subgraph.DiameterOfOpenCollapseButton;
                height = Math.Max(fe.Height + 2 * subgraph.Attr.LabelMargin, subgraph.DiameterOfOpenCollapseButton);
            }
            else
                return GetApproximateCollapsedBoundary(subgraph);

            if (width < _drawingGraph.Attr.MinNodeWidth)
                width = _drawingGraph.Attr.MinNodeWidth;
            if (height < _drawingGraph.Attr.MinNodeHeight)
                height = _drawingGraph.Attr.MinNodeHeight;
            return NodeBoundaryCurves.GetNodeBoundaryCurve(subgraph, width, height);
        }

        ICurve GetApproximateCollapsedBoundary(Subgraph subgraph) {
            if (textBoxForApproxNodeBoundaries == null)
                SetUpTextBoxForApproxNodeBoundaries();


            double width, height;
            if (String.IsNullOrEmpty(subgraph.LabelText))
                height = width = subgraph.DiameterOfOpenCollapseButton;
            else {
                double a = ((double)subgraph.LabelText.Length) / textBoxForApproxNodeBoundaries.Text.Length *
                           subgraph.Label.FontSize / Label.DefaultFontSize;
                width = textBoxForApproxNodeBoundaries.Width * a + subgraph.DiameterOfOpenCollapseButton;
                height =
                    Math.Max(
                        textBoxForApproxNodeBoundaries.Height * subgraph.Label.FontSize / Label.DefaultFontSize,
                        subgraph.DiameterOfOpenCollapseButton);
            }

            if (width < _drawingGraph.Attr.MinNodeWidth)
                width = _drawingGraph.Attr.MinNodeWidth;
            if (height < _drawingGraph.Attr.MinNodeHeight)
                height = _drawingGraph.Attr.MinNodeHeight;

            return NodeBoundaryCurves.GetNodeBoundaryCurve(subgraph, width, height);
        }


        void AssignLabelWidthHeight(Core.Layout.ILabeledObject labeledGeomObj,
                                    DrawingObject drawingObj) {
            if (drawingObjectsToControls.ContainsKey(drawingObj)) {
                Control fe = drawingObjectsToControls[drawingObj];
                labeledGeomObj.Label.Width = fe.Width;
                labeledGeomObj.Label.Height = fe.Height;
            }
        }


        ICurve GetNodeBoundaryCurve(Drawing.Node node) {
            double width, height;

            Control fe;
            if (drawingObjectsToControls.TryGetValue(node, out fe)) {
                width = fe.Width + 2 * node.Attr.LabelMargin;
                height = fe.Height + 2 * node.Attr.LabelMargin;
            }
            else
                return GetNodeBoundaryCurveByMeasuringText(node);

            if (width < _drawingGraph.Attr.MinNodeWidth)
                width = _drawingGraph.Attr.MinNodeWidth;
            if (height < _drawingGraph.Attr.MinNodeHeight)
                height = _drawingGraph.Attr.MinNodeHeight;
            return NodeBoundaryCurves.GetNodeBoundaryCurve(node, width, height);
        }

        TextBlock textBoxForApproxNodeBoundaries;
        private bool createToolTipForNodes = false;

        public static Size MeasureText(string text, FontFamily family, double size, Visual visual = null) {
            FormattedText formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(family),
                size,
                Brushes.Black);

            return new Size(formattedText.Width, formattedText.Height);
        }

        ICurve GetNodeBoundaryCurveByMeasuringText(Drawing.Node node) {
            double width, height;
            if (String.IsNullOrEmpty(node.LabelText)) {
                width = 10;
                height = 10;
            }
            else {
                var size = MeasureText(node.LabelText, new FontFamily(node.Label.FontName), node.Label.FontSize, GraphCanvas);
                width = size.Width;
                height = size.Height;
            }

            width += 2 * node.Attr.LabelMargin;
            height += 2 * node.Attr.LabelMargin;

            if (width < _drawingGraph.Attr.MinNodeWidth)
                width = _drawingGraph.Attr.MinNodeWidth;
            if (height < _drawingGraph.Attr.MinNodeHeight)
                height = _drawingGraph.Attr.MinNodeHeight;

            return NodeBoundaryCurves.GetNodeBoundaryCurve(node, width, height);
        }


        void SetUpTextBoxForApproxNodeBoundaries() {
            textBoxForApproxNodeBoundaries = new TextBlock {
                Text = "Fox jumping over River",
                FontFamily = new FontFamily(Label.DefaultFontName),
                FontSize = Label.DefaultFontSize,
            };

            textBoxForApproxNodeBoundaries.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            textBoxForApproxNodeBoundaries.Width = textBoxForApproxNodeBoundaries.DesiredSize.Width;
            textBoxForApproxNodeBoundaries.Height = textBoxForApproxNodeBoundaries.DesiredSize.Height;
        }


        void CreateControlsForLabelsOnly() {
            foreach (var edge in _drawingGraph.Edges) {
                var fe = CreateDefaultControlForDrawingObject(edge);
                if (fe != null)
                    if (_graphCanvas.Dispatcher.CheckAccess())
                        fe.Tag = new VLabel(edge, fe);
                    else {
                        var localEdge = edge;
                        _graphCanvas.Dispatcher.Invoke(() => fe.Tag = new VLabel(localEdge, fe));
                    }
            }

            foreach (var node in _drawingGraph.Nodes)
                CreateDefaultControlForDrawingObject(node);
            if (_drawingGraph.RootSubgraph != null)
                foreach (var subgraph in _drawingGraph.RootSubgraph.AllSubgraphsWidthFirstExcludingSelf())
                    CreateDefaultControlForDrawingObject(subgraph);
        }

        //        void CreateControlForEdgeLabel(DrawingEdge edge) {
        //            var textBlock = CreateTextBlockForDrawingObj(edge);
        //            if (textBlock == null) return;
        //            drawingGraphObjectsToTextBoxes[edge] = textBlock;
        //            textBlock.Tag = new VLabel(edge, textBlock);
        //        }

        public void RegisterLabelCreator(DrawingObject drawingObject, Func<DrawingObject, Control> func) {
            registeredCreators[drawingObject] = func;
        }

        public void UnregisterLabelCreator(DrawingObject drawingObject) {
            registeredCreators.Remove(drawingObject);
        }

        public Func<DrawingObject, Control> GetLabelCreator(DrawingObject drawingObject) {
            return registeredCreators[drawingObject];
        }

        Control CreateTextBlockForDrawingObj(DrawingObject drawingObj) {
            Func<DrawingObject, Control> registeredCreator;
            if (registeredCreators.TryGetValue(drawingObj, out registeredCreator))
                return registeredCreator(drawingObj);
            var labeledObj = drawingObj as ILabeledObject;
            if (labeledObj == null)
                return null;

            var drawingLabel = labeledObj.Label;
            if (drawingLabel == null)
                return null;

            TextBlock textBlock = null;
            if (_graphCanvas.Dispatcher.CheckAccess())
                textBlock = CreateTextBlock(drawingLabel);
            else
                _graphCanvas.Dispatcher.Invoke(() => textBlock = CreateTextBlock(drawingLabel));

            return textBlock;
        }

        static TextBlock CreateTextBlock(Label drawingLabel) {
            var textBlock = new TextBlock {
                Tag = drawingLabel,
                Text = drawingLabel.Text,
                FontFamily = new FontFamily(drawingLabel.FontName),
                FontSize = drawingLabel.FontSize,
                Foreground = Common.BrushFromMsaglColor(drawingLabel.FontColor)
            };

            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            textBlock.Width = textBlock.DesiredSize.Width;
            textBlock.Height = textBlock.DesiredSize.Height;
            if (drawingLabel.GeometryLabel == null) {
                drawingLabel.GeometryLabel = new Core.Layout.Label();
            }
            drawingLabel.GeometryLabel.Width = textBlock.Width;
            drawingLabel.GeometryLabel.Height = textBlock.Height;
            return textBlock;
        }


        Control CreateDefaultControlForDrawingObject(DrawingObject drawingObject) {
            lock (this) {
                var textBlock = CreateTextBlockForDrawingObj(drawingObject);
                if (textBlock != null)
                    drawingObjectsToControls[drawingObject] = textBlock;
                return textBlock;
            }
        }




        public void DrawRubberLine(MsaglMouseEventArgs args) {
            DrawRubberLine(ScreenToSource(args));
        }

        public void StopDrawingRubberLine() {
            _graphCanvas.Children.Remove(_rubberLinePath);
            _rubberLinePath = null;
            _graphCanvas.Children.Remove(_targetArrowheadPathForRubberEdge);
            _targetArrowheadPathForRubberEdge = null;
        }

        public void AddEdge(IViewerEdge edge, bool registerForUndo) {
            //if (registerForUndo) drawingLayoutEditor.RegisterEdgeAdditionForUndo(edge);

            var drawingEdge = edge.Edge;
            Edge geomEdge = drawingEdge.GeometryEdge;

            _drawingGraph.AddPrecalculatedEdge(drawingEdge);
            _drawingGraph.GeometryGraph.Edges.Add(geomEdge);

        }

        public IViewerEdge CreateEdgeWithGivenGeometry(DrawingEdge drawingEdge) {
            return CreateEdge(drawingEdge, _drawingGraph.LayoutAlgorithmSettings as LgLayoutSettings);
        }

        public void AddNode(IViewerNode node, bool registerForUndo) {
            if (_drawingGraph == null)
                throw new InvalidOperationException(); // adding a node when the graph does not exist
            var vNode = (VNode)node;
            _drawingGraph.AddNode(vNode.Node);
            _drawingGraph.GeometryGraph.Nodes.Add(vNode.Node.GeometryNode);
            layoutEditor.AttachLayoutChangeEvent(vNode);
            _graphCanvas.Children.Add(vNode.ControlOfNodeForLabel);
            layoutEditor.CleanObstacles();
        }

        public IViewerObject AddNode(Drawing.Node drawingNode) {
            Graph.AddNode(drawingNode);
            var vNode = CreateVNode(drawingNode);
            LayoutEditor.AttachLayoutChangeEvent(vNode);
            LayoutEditor.CleanObstacles();
            return vNode;
        }

        public void RemoveEdge(IViewerEdge edge, bool registerForUndo) {
            lock (this) {
                var vedge = (VEdge)edge;
                var dedge = vedge.Edge;
                _drawingGraph.RemoveEdge(dedge);
                _drawingGraph.GeometryGraph.Edges.Remove(dedge.GeometryEdge);
                drawingObjectsToControls.Remove(dedge);
                drawingObjectsToIViewerObjects.Remove(dedge);

                vedge.RemoveItselfFromCanvas(_graphCanvas);
            }
        }

        public void RemoveNode(IViewerNode node, bool registerForUndo) {
            lock (this) {
                RemoveEdges(node.Node.OutEdges);
                RemoveEdges(node.Node.InEdges);
                RemoveEdges(node.Node.SelfEdges);
                drawingObjectsToControls.Remove(node.Node);
                drawingObjectsToIViewerObjects.Remove(node.Node);
                var vnode = (VNode)node;
                vnode.DetouchFromCanvas(_graphCanvas);

                _drawingGraph.RemoveNode(node.Node);
                _drawingGraph.GeometryGraph.Nodes.Remove(node.Node.GeometryNode);
                layoutEditor.DetachNode(node);
                layoutEditor.CleanObstacles();
            }
        }

        void RemoveEdges(IEnumerable<DrawingEdge> drawingEdges) {
            foreach (var de in drawingEdges.ToArray()) {
                var vedge = (VEdge)drawingObjectsToIViewerObjects[de];
                RemoveEdge(vedge, false);
            }
        }


        public IViewerEdge RouteEdge(DrawingEdge drawingEdge) {
            var geomEdge = GeometryGraphCreator.CreateGeometryEdgeFromDrawingEdge(drawingEdge);
            var geomGraph = _drawingGraph.GeometryGraph;
            LayoutHelpers.RouteAndLabelEdges(geomGraph, _drawingGraph.LayoutAlgorithmSettings, new[] { geomEdge }, 0, null);
            return CreateEdge(drawingEdge, _drawingGraph.LayoutAlgorithmSettings as LgLayoutSettings);
        }

        public IViewerGraph ViewerGraph { get; set; }

        public double ArrowheadLength {
            get { return 0.2 * DpiX / CurrentScale; }
        }

        public void SetSourcePortForEdgeRouting(Point portLocation) {
            _sourcePortLocationForEdgeRouting = portLocation;
            if (_sourcePortCircle == null) {
                _sourcePortCircle = CreatePortPath();
                _graphCanvas.Children.Add(_sourcePortCircle);
            }
            _sourcePortCircle.Width = _sourcePortCircle.Height = UnderlyingPolylineCircleRadius;
            _sourcePortCircle.StrokeThickness = _sourcePortCircle.Width / 10;
            Common.PositionControl(_sourcePortCircle, portLocation, 1);
        }

        Ellipse CreatePortPath() {
            return new Ellipse {
                Stroke = Brushes.Brown,
                Fill = Brushes.Brown,
            };
        }



        public void SetTargetPortForEdgeRouting(Point portLocation) {
            if (TargetPortCircle == null) {
                TargetPortCircle = CreatePortPath();
                _graphCanvas.Children.Add(TargetPortCircle);
            }
            TargetPortCircle.Width = TargetPortCircle.Height = UnderlyingPolylineCircleRadius;
            TargetPortCircle.StrokeThickness = TargetPortCircle.Width / 10;
            Common.PositionControl(TargetPortCircle, portLocation, 1);
        }

        public void RemoveSourcePortEdgeRouting() {
            _graphCanvas.Children.Remove(_sourcePortCircle);
            _sourcePortCircle = null;
        }

        public void RemoveTargetPortEdgeRouting() {
            _graphCanvas.Children.Remove(TargetPortCircle);
            TargetPortCircle = null;
        }


        public void DrawRubberEdge(EdgeGeometry edgeGeometry) {
            if (_rubberEdgePath == null) {
                _rubberEdgePath = new Path {
                    Stroke = Brushes.Black,
                    StrokeThickness = GetBorderPathThickness() * 3
                };
                _graphCanvas.Children.Add(_rubberEdgePath);
                _targetArrowheadPathForRubberEdge = new Path {
                    Stroke = Brushes.Black,
                    StrokeThickness = GetBorderPathThickness() * 3
                };
                _graphCanvas.Children.Add(_targetArrowheadPathForRubberEdge);
            }
            _rubberEdgePath.Data = VEdge.GetICurveAvaloniaGeometry(edgeGeometry.Curve);
            _targetArrowheadPathForRubberEdge.Data = VEdge.DefiningTargetArrowHead(edgeGeometry,
                                                                                  edgeGeometry.LineWidth);
        }


        bool UnderLayout {
            get { return _backgroundWorker != null; }
        }

        public void StopDrawingRubberEdge() {
            _graphCanvas.Children.Remove(_rubberEdgePath);
            _graphCanvas.Children.Remove(_targetArrowheadPathForRubberEdge);
            _rubberEdgePath = null;
            _targetArrowheadPathForRubberEdge = null;
        }


        public PlaneTransformation Transform {
            get {
                var mt = _graphCanvas.RenderTransform as MatrixTransform;
                if (mt == null)
                    return PlaneTransformation.UnitTransformation;
                var m = mt.Matrix;
                return new PlaneTransformation(m.M11, m.M12, m.M31, m.M21, m.M22, m.M32);
            }
            set {
                SetRenderTransformWithoutRaisingEvents(value);

                if (ViewChangeEvent != null)
                    ViewChangeEvent(null, null);
            }
        }

        void SetRenderTransformWithoutRaisingEvents(PlaneTransformation value) {
            _graphCanvas.RenderTransform = new MatrixTransform(new Matrix(value[0, 0], value[0, 1], value[1, 0], value[1, 1],
                                                              value[0, 2],
                                                              value[1, 2]));
        }


        public bool NeedToCalculateLayout {
            get { return needToCalculateLayout; }
            set { needToCalculateLayout = value; }
        }

        /// <summary>
        /// the cancel token used to cancel a long running layout
        /// </summary>
        public CancelToken CancelToken {
            get { return _cancelToken; }
            set { _cancelToken = value; }
        }

        /// <summary>
        /// no layout is done, but the overlap is removed for graphs with geometry
        /// </summary>
        public bool NeedToRemoveOverlapOnly { get; set; }


        public void DrawRubberLine(Point rubberEnd) {
            if (_rubberLinePath == null) {
                _rubberLinePath = new Path {
                    Stroke = Brushes.Black,
                    StrokeThickness = GetBorderPathThickness() * 3
                };
                _graphCanvas.Children.Add(_rubberLinePath);
                //                targetArrowheadPathForRubberLine = new Path {
                //                    Stroke = Brushes.Black,
                //                    StrokeThickness = GetBorderPathThickness()*3
                //                };
                //                graphCanvas.Children.Add(targetArrowheadPathForRubberLine);
            }
            _rubberLinePath.Data =
                VEdge.GetICurveAvaloniaGeometry(new LineSegment(_sourcePortLocationForEdgeRouting, rubberEnd));
        }

        public void StartDrawingRubberLine(Point startingPoint) {
        }

        #endregion



        public IViewerNode CreateIViewerNode(Drawing.Node drawingNode, Point center, object visualElement) {
            if (_drawingGraph == null)
                return null;
            var frameworkElement = visualElement as Control ?? CreateTextBlockForDrawingObj(drawingNode);
            var width = frameworkElement.Width + 2 * drawingNode.Attr.LabelMargin;
            var height = frameworkElement.Height + 2 * drawingNode.Attr.LabelMargin;
            var bc = NodeBoundaryCurves.GetNodeBoundaryCurve(drawingNode, width, height);
            drawingNode.GeometryNode = new Node(bc, drawingNode) { Center = center };
            var vNode = CreateVNode(drawingNode);
            _drawingGraph.AddNode(drawingNode);
            _drawingGraph.GeometryGraph.Nodes.Add(drawingNode.GeometryNode);
            layoutEditor.AttachLayoutChangeEvent(vNode);
            MakeRoomForNewNode(drawingNode);

            return vNode;
        }

        void MakeRoomForNewNode(Drawing.Node drawingNode) {
            IncrementalDragger incrementalDragger = new IncrementalDragger(new[] { drawingNode.GeometryNode },
                                                                           Graph.GeometryGraph,
                                                                           Graph.LayoutAlgorithmSettings);
            incrementalDragger.Drag(new Point());

            foreach (var n in incrementalDragger.ChangedGraph.Nodes) {
                var dn = (Drawing.Node)n.UserData;
                var vn = drawingObjectsToIViewerObjects[dn] as VNode;
                if (vn != null)
                    vn.Invalidate();
            }

            foreach (var n in incrementalDragger.ChangedGraph.Edges) {
                var dn = (Drawing.Edge)n.UserData;
                var ve = drawingObjectsToIViewerObjects[dn] as VEdge;
                if (ve != null)
                    ve.Invalidate();
            }
        }
        public bool IncrementalDraggingModeAlways { get; set; }
        public bool CreateToolTipForNodes { get => createToolTipForNodes; private set => createToolTipForNodes = value; }
    }
}
