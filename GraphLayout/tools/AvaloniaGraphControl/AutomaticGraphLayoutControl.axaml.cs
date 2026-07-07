using System;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Msagl.Drawing;

namespace Microsoft.Msagl.AvaloniaGraphControl {
    public partial class AutomaticGraphLayoutControl : ScrollViewer, IDisposable {
        GraphViewer _graphViewer;
        public AutomaticGraphLayoutControl() {
            InitializeComponent();
            Loaded += (s, e) => SetGraph();
        }

        ~AutomaticGraphLayoutControl() {
            Dispose();
        }

        public void Dispose() {
            _graphViewer.Dispose();

            GC.SuppressFinalize(this);
        }

        public Graph Graph {
            get => GetValue(GraphProperty);
            set => SetValue(GraphProperty, value);
        }
        public static readonly StyledProperty<Graph> GraphProperty =
            AvaloniaProperty.Register<AutomaticGraphLayoutControl, Graph>("Graph");

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);

            if (change.Property == GraphProperty) {
                SetGraph();
            }
        }

        private void SetGraph() {
            if (Graph == null) {
                dockPanel.Children.Clear();
                return;
            }
            if (_graphViewer == null) {
                _graphViewer = new GraphViewer();
                _graphViewer.BindToPanel(dockPanel);
            }
            _graphViewer.Graph = Graph;
        }
    }
}
