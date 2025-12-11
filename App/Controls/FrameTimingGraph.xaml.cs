using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Remotier.Controls
{
    public partial class FrameTimingGraph : UserControl
    {
        private readonly List<double> _history = new List<double>();
        private const int MaxSamples = 100;
        private double _maxValue = 33.0; // Default to ~33ms (30fps) as baseline scale

        public FrameTimingGraph()
        {
            InitializeComponent();
        }

        public void AddSample(double valueMs)
        {
            _history.Add(valueMs);
            if (_history.Count > MaxSamples) _history.RemoveAt(0);

            // Update Text
            StatsText.Text = $"{valueMs:F1}ms";
            StatsText.Foreground = valueMs > 16.6 ? Brushes.Yellow : Brushes.Lime;

            // Redraw
            DrawGraph();
        }

        private void DrawGraph()
        {
            GraphCanvas.Children.Clear();

            double width = GraphCanvas.ActualWidth;
            double height = GraphCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            double barWidth = width / MaxSamples;
            double scale = height / Math.Max(_maxValue, 100.0); // Scale to 100ms max

            for (int i = 0; i < _history.Count; i++)
            {
                double val = _history[i];
                double barHeight = val * scale;
                if (barHeight > height) barHeight = height;

                var rect = new Rectangle
                {
                    Width = Math.Max(1, barWidth - 1),
                    Height = barHeight,
                    Fill = val > 33 ? Brushes.Red : (val > 16 ? Brushes.Yellow : Brushes.Lime)
                };

                Canvas.SetLeft(rect, i * barWidth);
                Canvas.SetBottom(rect, 0);

                GraphCanvas.Children.Add(rect);
            }
        }
    }
}
