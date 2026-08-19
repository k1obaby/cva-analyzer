using Microsoft.Win32;
using CvaAnalyzer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text;
using OxyPlot;
using OxyPlot.Axes;

namespace CvaAnalyzer;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();
    private bool _isDraggingLegend;
    private Point _legendDragStart;
    private double _legendStartLeft;
    private double _legendStartTop;

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            DataContext = ViewModel;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка при инициализации окна:\n{ex.Message}\n\nСтек вызовов:\n{ex.StackTrace}",
                "Ошибка инициализации",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void LoadExperimentButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt",
            Title = "Выберите файл с экспериментальными данными"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            ViewModel.LoadExperiment(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка при загрузке эксперимента:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LoadBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt",
            Title = "Выберите файл с фоном"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            ViewModel.LoadBackgroundFromFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка при загрузке фона:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MainPlotView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.IsPeakPlacementMode || ViewModel.PlotSelectionIndex != 0)
            return;

        var plotView = (OxyPlot.Wpf.PlotView)sender;
        var pos = e.GetPosition(plotView);
        var plotModel = ViewModel.CurrentPlotModel;
        if (plotModel == null || plotModel.Axes.Count < 2) return;

        var xAxis = plotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Bottom);
        var yAxis = plotModel.Axes.FirstOrDefault(a => a.Position == AxisPosition.Left);
        if (xAxis == null || yAxis == null) return;

        var screenPoint = new ScreenPoint(pos.X, pos.Y);
        var dataPoint = Axis.InverseTransform(screenPoint, xAxis, yAxis);

        ViewModel.AddPeakAtDataPoint(dataPoint.X, dataPoint.Y);
        e.Handled = true;
    }

    private void LegendOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingLegend = true;
        _legendDragStart = e.GetPosition(PlotAreaGrid);
        _legendStartLeft = Canvas.GetLeft(LegendOverlay);
        _legendStartTop = Canvas.GetTop(LegendOverlay);

        if (double.IsNaN(_legendStartLeft))
            _legendStartLeft = 10;
        if (double.IsNaN(_legendStartTop))
            _legendStartTop = 10;

        LegendOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void LegendOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingLegend)
            return;

        var current = e.GetPosition(PlotAreaGrid);
        var newLeft = _legendStartLeft + (current.X - _legendDragStart.X);
        var newTop = _legendStartTop + (current.Y - _legendDragStart.Y);

        var maxLeft = Math.Max(0, PlotAreaGrid.ActualWidth - LegendOverlay.ActualWidth - 4);
        var maxTop = Math.Max(0, PlotAreaGrid.ActualHeight - LegendOverlay.ActualHeight - 4);

        Canvas.SetLeft(LegendOverlay, Math.Max(0, Math.Min(newLeft, maxLeft)));
        Canvas.SetTop(LegendOverlay, Math.Max(0, Math.Min(newTop, maxTop)));
    }

    private void LegendOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingLegend)
            return;

        _isDraggingLegend = false;
        LegendOverlay.ReleaseMouseCapture();
        e.Handled = true;
    }
}