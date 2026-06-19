using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Microsoft.Msagl.Drawing;

namespace Microsoft.Msagl.AvaloniaGraphControl {
    internal class VLabel:IViewerObject,IInvalidatable {
        internal readonly Control Control;
        bool markedForDragging;

        public VLabel(Edge edge, Control frameworkElement) {
            Control = frameworkElement;
            DrawingObject = edge.Label;
        }

        public DrawingObject DrawingObject { get; private set; }

        public bool MarkedForDragging {
            get { return markedForDragging; }
            set {
                markedForDragging = value;
                if (value) {
                    AttachmentLine = new Line {
                        Stroke = Avalonia.Media.Brushes.Black,
                       StrokeDashArray = new AvaloniaList<double>(OffsetElems())
                    }; //the line will have 0,0, 0,0 start and end so it would not be rendered

                    ((Canvas)Control.Parent).Children.Add(AttachmentLine);
                }
                else {
                    ((Canvas) Control.Parent).Children.Remove(AttachmentLine);
                    AttachmentLine = null;
                }
            }
        }



        IEnumerable<double> OffsetElems() {
            yield return 1;
            yield return 2;
        }

        Line AttachmentLine { get; set; }

#pragma warning disable 67
        public event EventHandler MarkForDraggingEvent;

        public event EventHandler UnmarkForDraggingEvent;
#pragma warning restore 67

        public void Invalidate() {
            var label = (Drawing.Label)DrawingObject;
            Common.PositionControl(Control, label.Center, 1);
            var geomLabel = label.GeometryLabel;
            if (AttachmentLine != null)
            {
                AttachmentLine.StartPoint = new Point(
                    geomLabel.AttachmentSegmentStart.X, geomLabel.AttachmentSegmentStart.Y);
                AttachmentLine.EndPoint = new Point(
                    geomLabel.AttachmentSegmentEnd.X, geomLabel.AttachmentSegmentEnd.Y);
            }
        }
    }
}
