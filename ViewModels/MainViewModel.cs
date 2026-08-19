using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvaAnalyzer.Models;
using CvaAnalyzer.Services.Database;
using CvaAnalyzer.Services.Export;
using CvaAnalyzer.Services;
using CvaAnalyzer.Services.Parsers;
using CvaAnalyzer.Views;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using MathNet.Numerics.LinearAlgebra;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace CvaAnalyzer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        _currentPlotModel = CurrentVsPotential;
        var allPlotModels = new[] { CurrentVsPotential, PotentialVsCurrent, PotentialVsTime, CurrentVsTime, ChargeVsTime, CurrentDensityVsScanRate, CurrentDensityVsSqrtScanRate, ScanRateTabPlotModel };
        foreach (var pm in allPlotModels)
        {
            pm.IsLegendVisible = false;
            pm.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.RightTop,
                LegendPlacement = LegendPlacement.Inside,
                LegendBackground = OxyColor.FromArgb(220, 255, 255, 255),
                LegendBorder = OxyColors.Gray
            });
        }
    }
    private CyclicVoltammetryData? _experiment;
    private BackgroundData? _background;
    private BackgroundData? _backgroundFromFile;
    private Dictionary<PlotModel, LineSeries> _zeroLines = new();
    private Baseline? _sharedBaseline;

    public PlotModel CurrentVsPotential { get; } = new() { IsLegendVisible = true };
    public PlotModel PotentialVsCurrent { get; } = new() { IsLegendVisible = true };
    public PlotModel PotentialVsTime { get; } = new() { IsLegendVisible = true };
    public PlotModel CurrentVsTime { get; } = new() { IsLegendVisible = true };
    public PlotModel ChargeVsTime { get; } = new() { IsLegendVisible = true };
    public PlotModel CurrentDensityVsScanRate { get; } = new() { IsLegendVisible = true };
    public PlotModel CurrentDensityVsSqrtScanRate { get; } = new() { IsLegendVisible = true };
    public PlotModel ScanRateTabPlotModel { get; } = new() { IsLegendVisible = true };

    private PlotModel _currentPlotModel;
    public PlotModel CurrentPlotModel
    {
        get => _currentPlotModel;
        set => SetProperty(ref _currentPlotModel, value);
    }

    private int _plotSelectionIndex;
    public int PlotSelectionIndex
    {
        get => _plotSelectionIndex;
        set
        {
            if (SetProperty(ref _plotSelectionIndex, value))
                UpdateCurrentPlotModel();
        }
    }

    public ObservableCollection<ReactionEntryViewModel> Reactions { get; } = new();
    private readonly ObservableCollection<ReactionEntryViewModel> _allReactions = new();
    public ObservableCollection<ScanRatePoint> ScanRateTable { get; } = new();
    public ObservableCollection<BackgroundData> BackgroundLibrary { get; } = new();
    public ObservableCollection<PlotLegendItem> CurrentLegendItems { get; } = new();
    public bool HasLegendItems => CurrentLegendItems.Count > 0;

    private bool _subtractBackground;
    public bool SubtractBackground
    {
        get => _subtractBackground;
        set
        {
            if (SetProperty(ref _subtractBackground, value))
            {
                RebuildPlots();
            }
        }
    }

    private bool _showBackground;
    public bool ShowBackground
    {
        get => _showBackground;
        set
        {
            if (SetProperty(ref _showBackground, value))
            {
                RebuildPlots();
            }
        }
    }

    public bool HasBackground => _background != null;
    public bool HasBackgroundFromFile => _backgroundFromFile != null;

    private string _anodicPeakCountText = "1";
    public string AnodicPeakCountText
    {
        get => _anodicPeakCountText;
        set => SetProperty(ref _anodicPeakCountText, value ?? "1");
    }

    private string _cathodicPeakCountText = "1";
    public string CathodicPeakCountText
    {
        get => _cathodicPeakCountText;
        set => SetProperty(ref _cathodicPeakCountText, value ?? "1");
    }

    private (Baseline baseline, List<GaussianPeak> peaks)? _anodicFit;
    private List<VoltammetryPoint>? _anodicPointsForFit;
    private (Baseline baseline, List<GaussianPeak> peaks)? _cathodicFit;
    private List<VoltammetryPoint>? _cathodicPointsForFit;

    private List<GaussianPeak> _userPlacedPeaks = new();

    private bool _isPeakPlacementMode;
    public bool IsPeakPlacementMode
    {
        get => _isPeakPlacementMode;
        set => SetProperty(ref _isPeakPlacementMode, value);
    }

    public bool HasUserPlacedPeaks => _userPlacedPeaks.Count > 0;

    public bool HasApproximation => _anodicFit.HasValue || _cathodicFit.HasValue;

    private int _peakShapeIndex;
    public int PeakShapeIndex
    {
        get => _peakShapeIndex;
        set => SetProperty(ref _peakShapeIndex, value);
    }

    private int _fitMethodIndex;
    public int FitMethodIndex
    {
        get => _fitMethodIndex;
        set => SetProperty(ref _fitMethodIndex, value);
    }

    private int _lineTypeIndex;
    public int LineTypeIndex
    {
        get => _lineTypeIndex;
        set
        {
            if (SetProperty(ref _lineTypeIndex, value))
            {
                RebuildPlots();
            }
        }
    }
    
    private string _zeroLineButtonText = "Показать базовую линию";
    public string ZeroLineButtonText
    {
        get => _zeroLineButtonText;
        set => SetProperty(ref _zeroLineButtonText, value);
    }

    private string _baselineDegreeText = "2";
    public string BaselineDegreeText
    {
        get => _baselineDegreeText;
        set => SetProperty(ref _baselineDegreeText, value ?? "2");
    }

    private bool _showPeakComponents;
    public bool ShowPeakComponents
    {
        get => _showPeakComponents;
        set
        {
            if (SetProperty(ref _showPeakComponents, value))
                RebuildPlots();
        }
    }

    private LineStyle GetLineStyle()
    {
        return _lineTypeIndex switch
        {
            0 => LineStyle.Solid,
            1 => LineStyle.Dash,
            2 => LineStyle.Dot,
            _ => LineStyle.Solid
        };
    }

    private void RefreshLegendItems(PlotModel model)
    {
        CurrentLegendItems.Clear();

        foreach (var series in model.Series.Where(s => !string.IsNullOrWhiteSpace(s.Title)))
        {
            Brush brush = Brushes.Black;
            if (series is LineSeries lineSeries)
                brush = new SolidColorBrush(Color.FromArgb(lineSeries.Color.A, lineSeries.Color.R, lineSeries.Color.G, lineSeries.Color.B));
            else if (series is ScatterSeries scatterSeries)
                brush = new SolidColorBrush(Color.FromArgb(scatterSeries.MarkerFill.A, scatterSeries.MarkerFill.R, scatterSeries.MarkerFill.G, scatterSeries.MarkerFill.B));

            CurrentLegendItems.Add(new PlotLegendItem
            {
                Title = series.Title,
                Color = brush
            });
        }

        OnPropertyChanged(nameof(HasLegendItems));
    }

    private string _gammaResult = "";
    public string GammaResult
    {
        get => _gammaResult;
        set => SetProperty(ref _gammaResult, value);
    }

    private int _gammaElectronCount = 1;
    public int GammaElectronCount
    {
        get => _gammaElectronCount;
        set => SetProperty(ref _gammaElectronCount, Math.Max(1, value));
    }

    private double _gammaAreaCm2 = 1.0;
    public double GammaAreaCm2
    {
        get => _gammaAreaCm2;
        set => SetProperty(ref _gammaAreaCm2, Math.Max(0, value));
    }

    private bool _useManualGammaRange;
    public bool UseManualGammaRange
    {
        get => _useManualGammaRange;
        set => SetProperty(ref _useManualGammaRange, value);
    }

    private double _gammaRangeMin;
    public double GammaRangeMin
    {
        get => _gammaRangeMin;
        set => SetProperty(ref _gammaRangeMin, value);
    }

    private double _gammaRangeMax;
    public double GammaRangeMax
    {
        get => _gammaRangeMax;
        set => SetProperty(ref _gammaRangeMax, value);
    }

    public IRelayCommand CalculateGammaCommand => new RelayCommand(CalculateGamma);
    public IRelayCommand ExportPeaksCommand => new RelayCommand(ExportPeaks);
    public IRelayCommand FitGaussianCommand => new RelayCommand(FitGaussian);
    public IRelayCommand FitBaselineCommand => new RelayCommand(FitBaseline);
    public IRelayCommand ClearUserPlacedPeaksCommand => new RelayCommand(ClearUserPlacedPeaks);
    public IRelayCommand ClearApproximationCommand => new RelayCommand(ClearApproximation);
    public IRelayCommand ResetPlotViewCommand => new RelayCommand(ResetPlotView);
    public IRelayCommand ExportDataCommand => new RelayCommand(ExportData);
    public IRelayCommand LoadReactionsCommand => new RelayCommand(LoadReactions);
    public IRelayCommand AddZeroLineCommand => new RelayCommand(AddZeroLine);
    public IRelayCommand LoadReactionsFromDbCommand => new RelayCommand(LoadReactionsFromDb);
    public IRelayCommand ImportReactionsToDbCommand => new RelayCommand(ImportReactionsToDb);
    public IRelayCommand AddReactionCommand => new RelayCommand(AddReaction);
    public IRelayCommand LoadScanRateSeriesCommand => new RelayCommand(LoadScanRateSeries);
    public IRelayCommand AnalyzeScanRateCommand => new RelayCommand(AnalyzeScanRateSeries);
    public IRelayCommand ExportScanRateTableCommand => new RelayCommand(ExportScanRateTable);
    public IRelayCommand LoadBackgroundLibraryCommand => new RelayCommand(LoadBackgroundLibrary);
    public IRelayCommand ApplyBackgroundFilterCommand => new RelayCommand(ApplyBackgroundFilters);
    public IRelayCommand ClearBackgroundFiltersCommand => new RelayCommand(ClearBackgroundFilters);
    public IRelayCommand UseSelectedBackgroundCommand => new RelayCommand(UseSelectedBackground);
    public IRelayCommand UseBackgroundFromFileCommand => new RelayCommand(UseBackgroundFromFile);
    public IRelayCommand EditSelectedBackgroundCommand => new RelayCommand(EditSelectedBackground);
    public IRelayCommand DeleteSelectedBackgroundCommand => new RelayCommand(DeleteSelectedBackground);
    public IRelayCommand SearchByPotentialCommand => new RelayCommand(ApplyReactionFilter);

    private string _reactionPhText = string.Empty;
    public string ReactionPhText
    {
        get => _reactionPhText;
        set
        {
            if (SetProperty(ref _reactionPhText, value))
            {
                UpdateReactionPotentials();
                Reactions.Clear();
                foreach (var r in _allReactions)
                    Reactions.Add(r);
            }
        }
    }

    private double? _reactionPh;

    private string _potentialSearchText = string.Empty;
    public string PotentialSearchText
    {
        get => _potentialSearchText;
        set => SetProperty(ref _potentialSearchText, value);
    }

    private string _potentialToleranceText = "0.05";
    public string PotentialToleranceText
    {
        get => _potentialToleranceText;
        set => SetProperty(ref _potentialToleranceText, value);
    }

    private string _newReactionText = string.Empty;
    public string NewReactionText
    {
        get => _newReactionText;
        set => SetProperty(ref _newReactionText, value);
    }

    private string _newReactionE0 = string.Empty;
    public string NewReactionE0
    {
        get => _newReactionE0;
        set => SetProperty(ref _newReactionE0, value);
    }

    private string _newReactionN = string.Empty;
    public string NewReactionN
    {
        get => _newReactionN;
        set => SetProperty(ref _newReactionN, value);
    }

    private string _newReactionKH = string.Empty;
    public string NewReactionKH
    {
        get => _newReactionKH;
        set => SetProperty(ref _newReactionKH, value);
    }

    private string _newReactionKOH = string.Empty;
    public string NewReactionKOH
    {
        get => _newReactionKOH;
        set => SetProperty(ref _newReactionKOH, value);
    }

    private List<CyclicVoltammetryData> _scanRateCycles = new List<CyclicVoltammetryData>();
    private string _scanRateSampleName = string.Empty;

    private string _experimentFileName = string.Empty;
    public string ExperimentFileName
    {
        get => _experimentFileName;
        set => SetProperty(ref _experimentFileName, value);
    }

    private string _scanRatesInput = "500, 200, 100, 80, 50, 20, 10, 5";
    public string ScanRatesInput
    {
        get => _scanRatesInput;
        set => SetProperty(ref _scanRatesInput, value);
    }

    private string _scanRateTargetPotentialText = string.Empty;
    public string ScanRateTargetPotentialText
    {
        get => _scanRateTargetPotentialText;
        set => SetProperty(ref _scanRateTargetPotentialText, value);
    }

    private int _scanRateBranchIndex;
    public int ScanRateBranchIndex
    {
        get => _scanRateBranchIndex;
        set => SetProperty(ref _scanRateBranchIndex, value);
    }

    private double _scanRateElectrodeAreaCm2 = 1.0;
    public double ScanRateElectrodeAreaCm2
    {
        get => _scanRateElectrodeAreaCm2;
        set => SetProperty(ref _scanRateElectrodeAreaCm2, Math.Max(0, value));
    }

    private string _scanRateConclusion = string.Empty;
    public string ScanRateConclusion
    {
        get => _scanRateConclusion;
        set => SetProperty(ref _scanRateConclusion, value);
    }

    private string _scanRateFitInfo = string.Empty;
    public string ScanRateFitInfo
    {
        get => _scanRateFitInfo;
        set => SetProperty(ref _scanRateFitInfo, value);
    }

    private int _scanRateElectronCount = 1;
    public int ScanRateElectronCount
    {
        get => _scanRateElectronCount;
        set => SetProperty(ref _scanRateElectronCount, Math.Max(1, value));
    }

    private double _scanRateTemperatureK = 298.15;
    public double ScanRateTemperatureK
    {
        get => _scanRateTemperatureK;
        set => SetProperty(ref _scanRateTemperatureK, Math.Max(0, value));
    }

    private string _scanRateGammaSurfaceResult = string.Empty;
    public string ScanRateGammaSurfaceResult
    {
        get => _scanRateGammaSurfaceResult;
        set => SetProperty(ref _scanRateGammaSurfaceResult, value);
    }

    private int _scanRateTabCoordinateIndex;
    public int ScanRateTabCoordinateIndex
    {
        get => _scanRateTabCoordinateIndex;
        set
        {
            if (SetProperty(ref _scanRateTabCoordinateIndex, value))
                UpdateScanRateTabPlot();
        }
    }

    private List<DataPoint>? _scanRateTabPointsV;
    private List<DataPoint>? _scanRateTabPointsSqrt;
    private (double slope, double intercept, double r2)? _scanRateTabFitV;
    private (double slope, double intercept, double r2)? _scanRateTabFitSqrt;

    private List<BackgroundData> _allBackgrounds = new List<BackgroundData>();
    private BackgroundData? _selectedBackground;
    public BackgroundData? SelectedBackground
    {
        get => _selectedBackground;
        set => SetProperty(ref _selectedBackground, value);
    }

    public enum FilterParameter
    {
        None,
        SampleName,
        ScanRate,
        Electrolyte,
        WorkingElectrode,
        ReferenceElectrode,
        Atmosphere,
        CellType,
        DepositionMethod,
        Illumination
    }

    public ObservableCollection<string> FilterParameterNames { get; } = new()
    {
        "Не выбрано",
        "Название образца",
        "Скорость развертки",
        "Электролит",
        "Рабочий электрод",
        "Ссылочный электрод",
        "Атмосфера",
        "Тип ячейки",
        "Метод осаждения",
        "Освещение"
    };

    private int _selectedFilterParameterIndex = 0;
    public int SelectedFilterParameterIndex
    {
        get => _selectedFilterParameterIndex;
        set
        {
            if (SetProperty(ref _selectedFilterParameterIndex, value))
            {
                ApplyBackgroundFilters();
            }
        }
    }

    private string _filterValue = string.Empty;
    public string FilterValue
    {
        get => _filterValue;
        set
        {
            if (SetProperty(ref _filterValue, value))
            {
                ApplyBackgroundFilters();
            }
        }
    }

    private ObservableCollection<VoltammetryPoint> SubtractBackgroundData(
        ObservableCollection<VoltammetryPoint> experiment,
        ObservableCollection<VoltammetryPoint> background)
    {
        var result = new ObservableCollection<VoltammetryPoint>();
        foreach (var p in experiment)
        {
            double bgCurrent = InterpolateBackgroundCurrent(p.Potential, background);
            result.Add(new VoltammetryPoint
            {
                Time = p.Time,
                Potential = p.Potential,
                Current = p.Current - bgCurrent
            });
        }
        return result;
    }

    private List<VoltammetryPoint> GetMainPointsOrderedByTime()
    {
        if (_experiment == null)
            return new List<VoltammetryPoint>();

        var raw = _subtractBackground && _background != null
            ? SubtractBackgroundData(_experiment.Points, _background.Points)
            : _experiment.Points;

        return raw.OrderBy(p => p.Time).ThenBy(p => p.Potential).ToList();
    }

    public void LoadExperiment(string filePath)
    {
        var parser = new TxtCvaParser();
        _experiment = parser.Parse(filePath);
        ExperimentFileName = Path.GetFileName(filePath);

        _anodicFit = null;
        _anodicPointsForFit = null;
        _cathodicFit = null;
        _cathodicPointsForFit = null;
        _sharedBaseline = null;
        _userPlacedPeaks.Clear();
        OnPropertyChanged(nameof(HasUserPlacedPeaks));
        OnPropertyChanged(nameof(HasApproximation));

        UpdateCurrentPlotModel();
    }

    public void LoadBackgroundFromFile(string filePath)
    {
        var parser = new TxtCvaParser();
        var parsed = parser.Parse(filePath);

        var saveResult = MessageBox.Show(
            "Сохранить фон в библиотеку?",
            "Фон",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (saveResult == MessageBoxResult.Cancel)
            return;

        BackgroundMetadata? metadata = null;
        string sampleName = parsed.SampleName;
        if (saveResult == MessageBoxResult.Yes)
        {
            var window = new BackgroundMetadataWindow();
            window.SetInitialData(new BackgroundData { SampleName = parsed.SampleName, Metadata = new BackgroundMetadata() });
            if (window.ShowDialog() != true) return;

            var metadataFromInputs = window.GetMetadataFromInputs();
            if (metadataFromInputs.ScanRate <= 0)
            {
                MessageBox.Show("Скорость развертки должна быть больше 0.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            metadata = metadataFromInputs;
            sampleName = window.GetSampleName();
            if (string.IsNullOrWhiteSpace(sampleName)) sampleName = parsed.SampleName;
        }

        _background = new BackgroundData
        {
            SampleName = sampleName,
            Metadata = metadata ?? new BackgroundMetadata()
        };

        foreach (var p in parsed.Points)
            _background.Points.Add(p);

        _backgroundFromFile = new BackgroundData
        {
            SampleName = _background.SampleName,
            Metadata = _background.Metadata
        };
        foreach (var p in _background.Points)
            _backgroundFromFile.Points.Add(p);

        if (saveResult == MessageBoxResult.Yes)
        {
            var db = new BackgroundLibraryService();
            db.SaveBackground(_background);
        }

        OnPropertyChanged(nameof(HasBackground));
        OnPropertyChanged(nameof(HasBackgroundFromFile));
        RebuildPlots();
    }

    private void UseBackgroundFromFile()
    {
        if (_backgroundFromFile == null)
        {
            MessageBox.Show("Загрузите фон из файла.");
            return;
        }
        _background = _backgroundFromFile;
        OnPropertyChanged(nameof(HasBackground));
        RebuildPlots();
    }

    private void ClearUserPlacedPeaks()
    {
        _userPlacedPeaks.Clear();
        SyncUserPlacedPeakCounts();
        OnPropertyChanged(nameof(HasUserPlacedPeaks));
        RebuildPlots();
    }

    private void ClearApproximation()
    {
        _anodicFit = null;
        _anodicPointsForFit = null;
        _cathodicFit = null;
        _cathodicPointsForFit = null;
        _sharedBaseline = null;
        OnPropertyChanged(nameof(HasApproximation));
        RebuildPlots();
    }

    private void ResetPlotView()
    {
        if (CurrentPlotModel == null) return;
        foreach (var axis in CurrentPlotModel.Axes)
            axis.Reset();
        CurrentPlotModel.InvalidatePlot(true);
    }

    public void AddPeakAtDataPoint(double potential, double current)
    {
        if (_experiment == null) return;

        var points = GetMainPointsOrderedByTime();
        var sorted = points.OrderBy(p => p.Potential).ToList();
        if (sorted.Count < 2) return;

        var baseline = _sharedBaseline ?? CreateEndpointBaseline(sorted);
        var baselineCurrent = EvaluateBaseline(baseline, potential);
        var amplitude = current - baselineCurrent;

        if (Math.Abs(amplitude) < 1e-9) return;

        var shape = GetSelectedPeakShape();
        _userPlacedPeaks.Add(new GaussianPeak
        {
            Amplitude = amplitude,
            Center = potential,
            Sigma = 0.05,
            Shape = shape,
            IsUserDefined = true,
            FixedCurrent = current
        });
        SyncUserPlacedPeakCounts();
        OnPropertyChanged(nameof(HasUserPlacedPeaks));
        RebuildPlots();
    }

    private void SyncUserPlacedPeakCounts()
    {
        AnodicPeakCountText = _userPlacedPeaks.Count(p => p.Amplitude > 0).ToString(CultureInfo.InvariantCulture);
        CathodicPeakCountText = _userPlacedPeaks.Count(p => p.Amplitude < 0).ToString(CultureInfo.InvariantCulture);
    }

    private void RebuildPlots()
    {
        if (_experiment == null) return;

        bool hadZeroLines = _zeroLines.Count > 0;
        var plotModels = new[] { CurrentVsPotential, PotentialVsCurrent, PotentialVsTime, CurrentVsTime, ChargeVsTime, CurrentDensityVsScanRate, CurrentDensityVsSqrtScanRate };

        foreach (var plotModel in plotModels)
        {
            if (_zeroLines.TryGetValue(plotModel, out var zeroLine) && plotModel.Series.Contains(zeroLine))
            {
                plotModel.Series.Remove(zeroLine);
            }
        }

        var mainPoints = GetMainPointsOrderedByTime();

        var iuPoints = mainPoints.Select(p => new DataPoint(p.Potential, p.Current));
        UpdateCurrentVsPotential(CurrentVsPotential, iuPoints, OxyColors.Blue);
        if (_background != null && _showBackground)
        {
            var bgPoints = _background.Points.Select(p => new DataPoint(p.Potential, p.Current));
            AddBackgroundSeries(CurrentVsPotential, bgPoints, OxyColors.Red);
        }

        bool anyFitDrawn = false;
        if (_sharedBaseline != null && !hadZeroLines)
        {
            DrawSharedBaseline(CurrentVsPotential, _sharedBaseline, mainPoints, "Базовая / нулевая линия");
            anyFitDrawn = true;
        }
        if (_anodicFit.HasValue && _anodicPointsForFit != null && _anodicPointsForFit.Count > 0)
        {
            DrawBranchFit(CurrentVsPotential, _anodicFit.Value.peaks, _anodicPointsForFit, "анодная");
            anyFitDrawn = true;
        }
        if (_cathodicFit.HasValue && _cathodicPointsForFit != null && _cathodicPointsForFit.Count > 0)
        {
            DrawBranchFit(CurrentVsPotential, _cathodicFit.Value.peaks, _cathodicPointsForFit, "катодная");
            anyFitDrawn = true;
        }
        if (anyFitDrawn)
            CurrentVsPotential.InvalidatePlot(true);
        else if (_userPlacedPeaks.Count > 0 && mainPoints.Any())
        {
            var sorted = mainPoints.OrderBy(p => p.Potential).ToList();
            var baseline = _sharedBaseline ?? CreateEndpointBaseline(sorted);

            var scatterPoints = _userPlacedPeaks.Select(p =>
            {
                var baselineAtCenter = EvaluateBaseline(baseline, p.Center);
                var currentAtPeak = baselineAtCenter + p.Amplitude;
                return new ScatterPoint(p.Center, currentAtPeak);
            }).ToList();
            var scatterSeries = new ScatterSeries
            {
                ItemsSource = scatterPoints,
                MarkerType = MarkerType.Diamond,
                MarkerSize = 8,
                MarkerFill = OxyColors.Orange,
                Title = "Расставленные пики"
            };
            CurrentVsPotential.Series.Add(scatterSeries);
            CurrentVsPotential.InvalidatePlot(true);
        }

        var uiPoints = mainPoints.Select(p => new DataPoint(p.Current, p.Potential));
        UpdatePotentialVsCurrent(PotentialVsCurrent, uiPoints, OxyColors.DarkCyan);
        if (_background != null && _showBackground)
        {
            var bgPoints = _background.Points.Select(p => new DataPoint(p.Current, p.Potential));
            AddBackgroundSeries(PotentialVsCurrent, bgPoints, OxyColors.Red);
        }

        var utPoints = mainPoints.Select(p => new DataPoint(p.Time, p.Potential));
        UpdateTimeBasedPlot(PotentialVsTime, utPoints, "Потенциал (В)", OxyColors.Green);
        if (_background != null && _showBackground)
        {
            var bgPoints = _background.Points.Select(p => new DataPoint(p.Time, p.Potential));
            AddBackgroundSeries(PotentialVsTime, bgPoints, OxyColors.LightGray);
        }

        var itPoints = mainPoints.Select(p => new DataPoint(p.Time, p.Current));
        UpdateTimeBasedPlot(CurrentVsTime, itPoints, "Ток (А)", OxyColors.Red);
        if (_background != null && _showBackground)
        {
            var bgPoints = _background.Points.Select(p => new DataPoint(p.Time, p.Current));
            AddBackgroundSeries(CurrentVsTime, bgPoints, OxyColors.LightGray);
        }

        var qtPoints = mainPoints.Select(p => new DataPoint(p.Time, p.Charge));
        UpdateTimeBasedPlot(ChargeVsTime, qtPoints, "Заряд (Кл)", OxyColors.Purple);
        if (_background != null && _showBackground)
        {
            var bgPoints = _background.Points.Select(p => new DataPoint(p.Time, p.Charge));
            AddBackgroundSeries(ChargeVsTime, bgPoints, OxyColors.LightGray);
        }

        if (CurrentPlotModel != null)
        {
            CurrentPlotModel.InvalidatePlot(true);
        }

        if (hadZeroLines)
        {
            AddZeroLinesToAllPlots();
        }

        if (CurrentPlotModel != null)
            RefreshLegendItems(CurrentPlotModel);
    }

    private void DrawSharedBaseline(PlotModel model, Baseline baseline, List<VoltammetryPoint> allPoints, string title)
    {
        if (allPoints.Count == 0) return;
        var uMin = allPoints.Min(p => p.Potential);
        var uMax = allPoints.Max(p => p.Potential);
        var baselinePoints = Enumerable.Range(0, 300)
            .Select(i =>
            {
                double u = uMin + (uMax - uMin) * i / 299.0;
                return new DataPoint(u, EvaluateBaseline(baseline, u));
            })
            .ToList();
        model.Series.Add(new LineSeries
        {
            ItemsSource = baselinePoints,
            Color = OxyColors.DarkGray,
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash,
            Title = title
        });
    }

    private void DrawBranchFit(PlotModel model, List<GaussianPeak> peaks, List<VoltammetryPoint> branchPoints, string branchLabel)
    {
        if (_sharedBaseline == null || branchPoints.Count == 0)
            return;

        var baseline = _sharedBaseline;
        var uMin = branchPoints.Min(p => p.Potential);
        var uMax = branchPoints.Max(p => p.Potential);
        for (int idx = 0; idx < peaks.Count; idx++)
        {
            var peak = peaks[idx];
            var gaussPoints = Enumerable.Range(0, 100)
                .Select(i =>
                {
                    double u = peak.Center - 3 * peak.Sigma + (6 * peak.Sigma) * i / 99.0;
                    return new DataPoint(u, EvaluatePeak(peak, u));
                })
                .ToList();
            if (ShowPeakComponents)
            {
                model.Series.Add(new LineSeries
                {
                    ItemsSource = gaussPoints,
                    Color = OxyColors.Orange,
                    StrokeThickness = 1,
                    Title = peaks.Count > 1 ? $"Пик {branchLabel} {idx + 1}" : $"Пик {branchLabel}"
                });
            }
        }
        var totalPoints = Enumerable.Range(0, 300)
            .Select(i =>
            {
                double u = uMin + (uMax - uMin) * i / 299.0;
                double total = EvaluateBaseline(baseline, u) + peaks.Sum(p => EvaluatePeak(p, u));
                return new DataPoint(u, total);
            })
            .ToList();
        model.Series.Add(new LineSeries
        {
            ItemsSource = totalPoints,
            Color = OxyColors.Magenta,
            StrokeThickness = 2,
            LineStyle = LineStyle.Dash,
            Title = $"Сумма ({branchLabel})"
        });
    }

    private void UpdateCurrentVsPotential(PlotModel model, IEnumerable<DataPoint> points, OxyColor color)
    {
        model.Series.Clear();
        model.Axes.Clear();

        var list = points.ToList();
        if (!list.Any()) return;

        var xAxis = new LinearAxis { Position = AxisPosition.Bottom, Title = "Потенциал (В)" };
        var yAxis = new LinearAxis { Position = AxisPosition.Left, Title = "Ток (А)" };

        model.Axes.Add(xAxis);
        model.Axes.Add(yAxis);

        var line = new LineSeries
        {
            MarkerType = MarkerType.None,
            Color = color,
            StrokeThickness = 2,
            LineStyle = GetLineStyle(),
            ItemsSource = list,
            Title = "Эксперимент"
        };
        model.Series.Add(line);
        model.InvalidatePlot(true);
    }

    private void UpdatePotentialVsCurrent(PlotModel model, IEnumerable<DataPoint> points, OxyColor color)
    {
        model.Series.Clear();
        model.Axes.Clear();

        var list = points.ToList();
        if (!list.Any()) return;

        var xAxis = new LinearAxis { Position = AxisPosition.Bottom, Title = "Ток (А)" };
        var yAxis = new LinearAxis { Position = AxisPosition.Left, Title = "Потенциал (В)" };

        model.Axes.Add(xAxis);
        model.Axes.Add(yAxis);

        var line = new LineSeries
        {
            MarkerType = MarkerType.None,
            Color = color,
            StrokeThickness = 2,
            LineStyle = GetLineStyle(),
            ItemsSource = list,
            Title = "Эксперимент"
        };
        model.Series.Add(line);
        model.InvalidatePlot(true);
    }

    private void UpdateTimeBasedPlot(PlotModel model, IEnumerable<DataPoint> points, string yTitle, OxyColor color)
    {
        model.Series.Clear();
        model.Axes.Clear();

        var list = points.ToList();
        if (!list.Any()) return;

        var xAxis = new LinearAxis { Position = AxisPosition.Bottom, Title = "Время (с)" };
        var yAxis = new LinearAxis { Position = AxisPosition.Left, Title = yTitle };

        model.Axes.Add(xAxis);
        model.Axes.Add(yAxis);

        var line = new LineSeries
        {
            MarkerType = MarkerType.None,
            Color = color,
            StrokeThickness = 2,
            LineStyle = GetLineStyle(),
            ItemsSource = list,
            Title = "Эксперимент"
        };
        model.Series.Add(line);
        model.InvalidatePlot(true);
    }

    private void AddBackgroundSeries(PlotModel model, IEnumerable<DataPoint> points, OxyColor color)
    {
        var list = points.ToList();
        if (!list.Any()) return;

        var bgSeries = new LineSeries
        {
            ItemsSource = list,
            Color = color,
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash,
            MarkerType = MarkerType.None,
            Title = "Фон"
        };
        model.Series.Insert(0, bgSeries);
        model.InvalidatePlot(true);
    }

    private static double InterpolateBackgroundCurrent(double potential, ObservableCollection<VoltammetryPoint> background)
    {
        if (background.Count == 0) return 0;

        var sorted = background.OrderBy(p => p.Potential).ToArray();

        if (potential <= sorted[0].Potential)
            return sorted[0].Current;
        if (potential >= sorted[^1].Potential)
            return sorted[^1].Current;

        for (int i = 1; i < sorted.Length; i++)
        {
            if (potential <= sorted[i].Potential)
            {
                var x0 = sorted[i - 1].Potential;
                var y0 = sorted[i - 1].Current;
                var x1 = sorted[i].Potential;
                var y1 = sorted[i].Current;
                return y0 + (y1 - y0) * (potential - x0) / (x1 - x0);
            }
        }
        return 0;
    }

    private static double InterpolateCurrentAtPotential(double potential, List<VoltammetryPoint> sortedPoints)
    {
        if (sortedPoints.Count == 0) return 0;
        if (potential <= sortedPoints[0].Potential)
            return sortedPoints[0].Current;
        if (potential >= sortedPoints[^1].Potential)
            return sortedPoints[^1].Current;

        for (int i = 1; i < sortedPoints.Count; i++)
        {
            if (potential <= sortedPoints[i].Potential)
            {
                var x0 = sortedPoints[i - 1].Potential;
                var y0 = sortedPoints[i - 1].Current;
                var x1 = sortedPoints[i].Potential;
                var y1 = sortedPoints[i].Current;
                return y0 + (y1 - y0) * (potential - x0) / (x1 - x0);
            }
        }

        return sortedPoints[^1].Current;
    }

    private static double EvaluateBaseline(Baseline baseline, double x)
    {
        if (baseline.Coefficients != null && baseline.Coefficients.Length > 0)
        {
            double value = 0;
            double power = 1;
            for (int i = 0; i < baseline.Coefficients.Length; i++)
            {
                value += baseline.Coefficients[i] * power;
                power *= x;
            }
            return value;
        }

        return baseline.Intercept + baseline.Slope * x;
    }

    private static Baseline CreateBaselineFromCoefficients(double[] coefficients)
    {
        return new Baseline
        {
            Coefficients = coefficients,
            Intercept = coefficients.Length > 0 ? coefficients[0] : 0,
            Slope = coefficients.Length > 1 ? coefficients[1] : 0
        };
    }

    private int GetBaselinePolynomialDegree()
    {
        if (int.TryParse(BaselineDegreeText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var degree))
            return Math.Clamp(degree, 1, 6);
        return 2;
    }

    private static double[] FitPolynomialCoefficients(List<DataPoint> points, int degree)
    {
        if (points.Count == 0)
            return new[] { 0.0, 0.0 };

        var matrix = Matrix<double>.Build.Dense(points.Count, degree + 1);
        var y = Vector<double>.Build.Dense(points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            double power = 1;
            for (int j = 0; j <= degree; j++)
            {
                matrix[i, j] = power;
                power *= points[i].X;
            }
            y[i] = points[i].Y;
        }

        return matrix.QR().Solve(y).ToArray();
    }

    private static Baseline CreateEndpointBaseline(List<VoltammetryPoint> sortedPoints)
    {
        var first = sortedPoints.First();
        var last = sortedPoints.Last();
        var slope = (last.Current - first.Current) / (last.Potential - first.Potential);
        return CreateBaselineFromCoefficients(new[] { first.Current - slope * first.Potential, slope });
    }

    private static Baseline ComputeSharedBaseline(List<VoltammetryPoint> anodicBranch, List<VoltammetryPoint> cathodicBranch, int degree)
    {
        if (anodicBranch.Count < 2 && cathodicBranch.Count < 2)
            return new Baseline();
        if (anodicBranch.Count < 2)
            return CreateEndpointBaseline(cathodicBranch.OrderBy(p => p.Potential).ToList());
        if (cathodicBranch.Count < 2)
            return CreateEndpointBaseline(anodicBranch.OrderBy(p => p.Potential).ToList());

        var anodicSorted = anodicBranch.OrderBy(p => p.Potential).ToList();
        var cathodicSorted = cathodicBranch.OrderBy(p => p.Potential).ToList();
        var overlapMin = Math.Max(anodicSorted.First().Potential, cathodicSorted.First().Potential);
        var overlapMax = Math.Min(anodicSorted.Last().Potential, cathodicSorted.Last().Potential);

        if (overlapMax <= overlapMin)
            return CreateEndpointBaseline(anodicSorted.Concat(cathodicSorted).OrderBy(p => p.Potential).ToList());

        var midpointSamples = new List<DataPoint>();
        const int sampleCount = 120;
        for (int i = 0; i < sampleCount; i++)
        {
            var potential = overlapMin + (overlapMax - overlapMin) * i / (sampleCount - 1.0);
            var anodicCurrent = InterpolateCurrentAtPotential(potential, anodicSorted);
            var cathodicCurrent = InterpolateCurrentAtPotential(potential, cathodicSorted);
            midpointSamples.Add(new DataPoint(potential, (anodicCurrent + cathodicCurrent) / 2.0));
        }

        var xMin = Math.Min(anodicSorted.First().Potential, cathodicSorted.First().Potential);
        var xMax = Math.Max(anodicSorted.Last().Potential, cathodicSorted.Last().Potential);
        var yMin = (InterpolateCurrentAtPotential(xMin, anodicSorted) + InterpolateCurrentAtPotential(xMin, cathodicSorted)) / 2.0;
        var yMax = (InterpolateCurrentAtPotential(xMax, anodicSorted) + InterpolateCurrentAtPotential(xMax, cathodicSorted)) / 2.0;

        if (degree <= 1 || Math.Abs(xMax - xMin) < 1e-12)
            return CreateEndpointBaseline(new List<VoltammetryPoint>
            {
                new() { Potential = xMin, Current = yMin },
                new() { Potential = xMax, Current = yMax }
            });

        double lineSlope = (yMax - yMin) / (xMax - xMin);
        double lineIntercept = yMin - lineSlope * xMin;
        int innerDegree = Math.Max(0, degree - 2);

        var matrix = Matrix<double>.Build.Dense(midpointSamples.Count, innerDegree + 1);
        var y = Vector<double>.Build.Dense(midpointSamples.Count);
        for (int i = 0; i < midpointSamples.Count; i++)
        {
            var x = midpointSamples[i].X;
            var gate = (x - xMin) * (x - xMax);
            var power = 1.0;
            for (int j = 0; j <= innerDegree; j++)
            {
                matrix[i, j] = gate * power;
                power *= x;
            }
            y[i] = midpointSamples[i].Y - (lineIntercept + lineSlope * x);
        }

        var correction = matrix.QR().Solve(y).ToArray();
        var coefficients = new double[degree + 1];
        coefficients[0] = lineIntercept;
        coefficients[1] = lineSlope;

        for (int j = 0; j <= innerDegree; j++)
        {
            var a = correction[j];
            coefficients[j] += a * xMin * xMax;
            coefficients[j + 1] -= a * (xMin + xMax);
            coefficients[j + 2] += a;
        }

        return CreateBaselineFromCoefficients(coefficients);
    }

    private void CalculateGamma()
    {
        if (_experiment == null)
        {
            GammaResult = "Нет данных: загрузите файл эксперимента.";
            return;
        }

        var points = GetMainPointsOrderedByTime();

        if (points.Count < 3)
        {
            GammaResult = "Недостаточно данных";
            return;
        }

        List<VoltammetryPoint> relevantPoints;
        if (_useManualGammaRange)
        {
            double min = Math.Min(_gammaRangeMin, _gammaRangeMax);
            double max = Math.Max(_gammaRangeMin, _gammaRangeMax);
            relevantPoints = points
                .Where(p => p.Potential >= min && p.Potential <= max)
                .OrderBy(p => p.Time)
                .ToList();
            if (relevantPoints.Count < 2)
            {
                GammaResult = "Недостаточно точек в диапазоне";
                return;
            }
        }
        else
        {
            var maxPoint = points.OrderByDescending(p => Math.Abs(p.Current)).First();
            double maxCurrent = Math.Abs(maxPoint.Current);
            double threshold = maxCurrent * 0.1;

            var sortedPoints = points.OrderBy(p => p.Potential).ToList();
            int peakIndex = sortedPoints.FindIndex(p => p.Potential == maxPoint.Potential);
            if (peakIndex == -1) peakIndex = sortedPoints.Count / 2;

            int left = peakIndex;
            while (left > 0 && Math.Abs(sortedPoints[left].Current) > threshold)
                left--;

            int right = peakIndex;
            while (right < sortedPoints.Count - 1 && Math.Abs(sortedPoints[right].Current) > threshold)
                right++;

            relevantPoints = sortedPoints.Skip(left).Take(right - left + 1)
                .OrderBy(p => p.Time)
                .ToList();

            if (relevantPoints.Count < 2)
            {
                GammaResult = "Пик слишком узкий";
                return;
            }
        }

        double Q = 0;
        for (int i = 0; i < relevantPoints.Count - 1; i++)
        {
            double dt = relevantPoints[i + 1].Time - relevantPoints[i].Time;
            double avgI = (relevantPoints[i].Current + relevantPoints[i + 1].Current) / 2.0;
            Q += avgI * dt;
        }

        const double Faraday = 96485.33212;
        if (_gammaElectronCount <= 0 || _gammaAreaCm2 <= 0)
        {
            GammaResult = "Введите n и площадь электрода";
            return;
        }

        double areaM2 = _gammaAreaCm2 / 10000.0;
        double gamma = Math.Abs(Q) / (_gammaElectronCount * Faraday * areaM2) * 1e6;
        GammaResult = $"Γ = {gamma:F2} мкмоль/м²";
    }

    private static (List<VoltammetryPoint> anodic, List<VoltammetryPoint> cathodic) SplitIntoBranches(List<VoltammetryPoint> pointsOrderedByTime)
    {
        if (pointsOrderedByTime.Count < 10)
            return (new List<VoltammetryPoint>(), new List<VoltammetryPoint>());

        var anodic = new List<VoltammetryPoint>();
        var cathodic = new List<VoltammetryPoint>();
        int segmentStart = 0;
        int currentSign = 0;

        for (int i = 1; i < pointsOrderedByTime.Count; i++)
        {
            double deltaPotential = pointsOrderedByTime[i].Potential - pointsOrderedByTime[i - 1].Potential;
            int sign = deltaPotential > 0 ? 1 : deltaPotential < 0 ? -1 : 0;
            if (sign == 0)
                continue;

            if (currentSign == 0)
            {
                currentSign = sign;
                segmentStart = i - 1;
                continue;
            }

            if (sign != currentSign)
            {
                var segment = pointsOrderedByTime.Skip(segmentStart).Take(i - segmentStart).ToList();
                if (currentSign > 0)
                    AppendBranchSegment(anodic, segment);
                else
                    AppendBranchSegment(cathodic, segment);

                segmentStart = i - 1;
                currentSign = sign;
            }
        }

        if (currentSign != 0)
        {
            var lastSegment = pointsOrderedByTime.Skip(segmentStart).ToList();
            if (currentSign > 0)
                AppendBranchSegment(anodic, lastSegment);
            else
                AppendBranchSegment(cathodic, lastSegment);
        }

        const int minBranchPoints = 5;
        if (anodic.Count < minBranchPoints) anodic = new List<VoltammetryPoint>();
        if (cathodic.Count < minBranchPoints) cathodic = new List<VoltammetryPoint>();
        return (anodic, cathodic);
    }

    private static void AppendBranchSegment(List<VoltammetryPoint> branch, List<VoltammetryPoint> segment)
    {
        if (segment.Count == 0)
            return;

        if (branch.Count > 0 && branch[^1].Time == segment[0].Time)
            segment = segment.Skip(1).ToList();

        branch.AddRange(segment);
    }

    private static List<VoltammetryPoint> ConvertToResidualPoints(List<VoltammetryPoint> branchPoints, Baseline baseline)
    {
        return branchPoints
            .Select(p => new VoltammetryPoint
            {
                Time = p.Time,
                Potential = p.Potential,
                Current = p.Current - EvaluateBaseline(baseline, p.Potential)
            })
            .OrderBy(p => p.Potential)
            .ToList();
    }

    private void FitBaseline()
    {
        if (_experiment == null)
        {
            MessageBox.Show("Загрузите данные эксперимента.");
            return;
        }

        var pointsByTime = GetMainPointsOrderedByTime();
        var (anodicBranch, cathodicBranch) = SplitIntoBranches(pointsByTime);

        if (anodicBranch.Count < 5 || cathodicBranch.Count < 5)
        {
            MessageBox.Show("Аппроксимация базовой линии: на анодной и катодной ветви нужно не менее 5 точек.");
            return;
        }

        _sharedBaseline = ComputeSharedBaseline(anodicBranch, cathodicBranch, GetBaselinePolynomialDegree());
        _anodicFit = null;
        _anodicPointsForFit = null;
        _cathodicFit = null;
        _cathodicPointsForFit = null;
        OnPropertyChanged(nameof(HasApproximation));
        RebuildPlots();
    }

    private void FitGaussian()
    {
        if (_experiment == null)
        {
            MessageBox.Show("Загрузите данные эксперимента.");
            return;
        }

        var pointsByTime = GetMainPointsOrderedByTime();

        try
        {
            var (anodicBranch, cathodicBranch) = SplitIntoBranches(pointsByTime);
            var shape = GetSelectedPeakShape();
            bool useUserDefinedPeaks = _userPlacedPeaks.Count > 0;
            int requestedAnodic = 1;
            int requestedCathodic = 1;
            if (int.TryParse(AnodicPeakCountText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var an) && an > 0)
                requestedAnodic = an;
            if (int.TryParse(CathodicPeakCountText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cat) && cat > 0)
                requestedCathodic = cat;

            _anodicFit = null;
            _anodicPointsForFit = null;
            _cathodicFit = null;
            _cathodicPointsForFit = null;
            _sharedBaseline ??= ComputeSharedBaseline(anodicBranch, cathodicBranch, GetBaselinePolynomialDegree());

            int anodicPeaksFound = 0;
            int cathodicPeaksFound = 0;

            if (anodicBranch.Count >= 5)
            {
                var sortedAnodic = anodicBranch.OrderBy(p => p.Potential).ToList();
                var baselineAnodic = new Baseline { Slope = _sharedBaseline.Slope, Intercept = _sharedBaseline.Intercept, Coefficients = _sharedBaseline.Coefficients.ToArray() };
                var residualAnodic = ConvertToResidualPoints(sortedAnodic, baselineAnodic);
                var zeroBaseline = new Baseline { Coefficients = new[] { 0.0 }, Intercept = 0, Slope = 0 };
                var anodicPeaks = useUserDefinedPeaks
                    ? GetUserDefinedPeaksForBranch(_userPlacedPeaks, baselineAnodic, shape, anodic: true, sortedAnodic)
                    : FindPeaksOnBranch(residualAnodic, zeroBaseline, shape, anodic: true);
                var selectedAnodic = useUserDefinedPeaks
                    ? anodicPeaks.ToList()
                    : SelectStrongestPeaks(anodicPeaks, requestedAnodic);
                anodicPeaksFound = selectedAnodic.Count;
                var peaksAnodic = selectedAnodic;
                var weightsAnodicFirst = GetEndpointWeights(residualAnodic, 0.06, 3.0);
                var fitAnodic = useUserDefinedPeaks
                    ? FitUserDefinedPeaksFixedBaseline(residualAnodic, zeroBaseline, peaksAnodic)
                    : _fitMethodIndex switch
                    {
                        0 => FitGaussianLinearFixedBaseline(residualAnodic, zeroBaseline, peaksAnodic),
                        1 => FitGaussianLevenbergMarquardtFixedBaseline(residualAnodic, zeroBaseline, peaksAnodic, weightsAnodicFirst),
                        2 => FitGaussianNelderMeadFixedBaseline(residualAnodic, zeroBaseline, peaksAnodic, weightsAnodicFirst),
                        _ => (zeroBaseline, peaksAnodic)
                    };
                _anodicFit = (baselineAnodic, fitAnodic.Item2);
                _anodicPointsForFit = sortedAnodic;
            }

            if (cathodicBranch.Count >= 5)
            {
                var sortedCathodic = cathodicBranch.OrderBy(p => p.Potential).ToList();
                var baselineCathodic = new Baseline { Slope = _sharedBaseline.Slope, Intercept = _sharedBaseline.Intercept, Coefficients = _sharedBaseline.Coefficients.ToArray() };
                var residualCathodic = ConvertToResidualPoints(sortedCathodic, baselineCathodic);
                var zeroBaseline = new Baseline { Coefficients = new[] { 0.0 }, Intercept = 0, Slope = 0 };
                var cathodicPeaks = useUserDefinedPeaks
                    ? GetUserDefinedPeaksForBranch(_userPlacedPeaks, baselineCathodic, shape, anodic: false, sortedCathodic)
                    : FindPeaksOnBranch(residualCathodic, zeroBaseline, shape, anodic: false);
                var selectedCathodic = useUserDefinedPeaks
                    ? cathodicPeaks.ToList()
                    : SelectStrongestPeaks(cathodicPeaks, requestedCathodic);
                cathodicPeaksFound = selectedCathodic.Count;
                var peaksCathodic = selectedCathodic;
                var weightsCathodicFirst = GetEndpointWeights(residualCathodic, 0.06, 3.0);
                var fitCathodic = useUserDefinedPeaks
                    ? FitUserDefinedPeaksFixedBaseline(residualCathodic, zeroBaseline, peaksCathodic)
                    : _fitMethodIndex switch
                    {
                        0 => FitGaussianLinearFixedBaseline(residualCathodic, zeroBaseline, peaksCathodic),
                        1 => FitGaussianLevenbergMarquardtFixedBaseline(residualCathodic, zeroBaseline, peaksCathodic, weightsCathodicFirst),
                        2 => FitGaussianNelderMeadFixedBaseline(residualCathodic, zeroBaseline, peaksCathodic, weightsCathodicFirst),
                        _ => (zeroBaseline, peaksCathodic)
                    };
                _cathodicFit = (baselineCathodic, fitCathodic.Item2);
                _cathodicPointsForFit = sortedCathodic;
            }

            if (!_anodicFit.HasValue && !_cathodicFit.HasValue)
            {
                MessageBox.Show("Не удалось выделить анодную и катодную ветви: на каждой нужно не менее 5 точек. Проверьте файл данных.");
                return;
            }

            double maxAbsCurrent = pointsByTime.Count > 0 ? pointsByTime.Max(p => Math.Abs(p.Current)) : 1e-6;
            double closureThreshold = Math.Max(1e-8, 0.02 * maxAbsCurrent);
            bool closureRefined = false;
            string? closureWarning = null;

            if (_anodicFit.HasValue && _anodicPointsForFit != null && _anodicPointsForFit.Count > 0 && _anodicFit.Value.peaks.Count > 0)
            {
                double errAnodic = GetClosureError(_anodicPointsForFit, _anodicFit.Value.baseline, _anodicFit.Value.peaks);
                if (errAnodic > closureThreshold)
                {
                    var residualAnodic = ConvertToResidualPoints(_anodicPointsForFit, _anodicFit.Value.baseline);
                    var zeroBaseline = new Baseline { Coefficients = new[] { 0.0 }, Intercept = 0, Slope = 0 };
                    var weightsAnodic = GetEndpointWeights(residualAnodic, 0.08, 12.0);
                    var refinedAnodic = _fitMethodIndex == 1
                        ? FitGaussianLevenbergMarquardtFixedBaseline(residualAnodic, zeroBaseline, _anodicFit.Value.peaks.ToList(), weightsAnodic)
                        : FitGaussianNelderMeadFixedBaseline(residualAnodic, zeroBaseline, _anodicFit.Value.peaks.ToList(), weightsAnodic);
                    _anodicFit = (_anodicFit.Value.baseline, refinedAnodic.peaks);
                    closureRefined = true;
                }
                if (GetClosureError(_anodicPointsForFit, _anodicFit.Value.baseline, _anodicFit.Value.peaks) > closureThreshold)
                    closureWarning = "Замыкание анодной ветви на концах > 2% от максимального тока; уточните базовую линию или задайте пики вручную.";
            }

            if (_cathodicFit.HasValue && _cathodicPointsForFit != null && _cathodicPointsForFit.Count > 0 && _cathodicFit.Value.peaks.Count > 0)
            {
                double errCathodic = GetClosureError(_cathodicPointsForFit, _cathodicFit.Value.baseline, _cathodicFit.Value.peaks);
                if (errCathodic > closureThreshold)
                {
                    var residualCathodic = ConvertToResidualPoints(_cathodicPointsForFit, _cathodicFit.Value.baseline);
                    var zeroBaseline = new Baseline { Coefficients = new[] { 0.0 }, Intercept = 0, Slope = 0 };
                    var weightsCathodic = GetEndpointWeights(residualCathodic, 0.08, 12.0);
                    var refinedCathodic = _fitMethodIndex == 1
                        ? FitGaussianLevenbergMarquardtFixedBaseline(residualCathodic, zeroBaseline, _cathodicFit.Value.peaks.ToList(), weightsCathodic)
                        : FitGaussianNelderMeadFixedBaseline(residualCathodic, zeroBaseline, _cathodicFit.Value.peaks.ToList(), weightsCathodic);
                    _cathodicFit = (_cathodicFit.Value.baseline, refinedCathodic.peaks);
                    closureRefined = true;
                }
                if (GetClosureError(_cathodicPointsForFit, _cathodicFit.Value.baseline, _cathodicFit.Value.peaks) > closureThreshold)
                    closureWarning = (closureWarning != null ? closureWarning + " " : "") + "Замыкание катодной ветви на концах > 2% от максимального тока.";
            }

            OnPropertyChanged(nameof(HasApproximation));
            RebuildPlots();

            string msg = $"Анодная ветвь: пиков подобрано {anodicPeaksFound}. Катодная ветвь: пиков подобрано {cathodicPeaksFound}.";
            if (closureRefined) msg += " Замыкание на концах ветвей подправлено.";
            if (closureWarning != null) msg += " " + closureWarning;
            MessageBox.Show(msg);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}");
        }
    }

    private static List<GaussianPeak> FindPeaksOnBranch(List<VoltammetryPoint> sortedByPotential, Baseline baseline, PeakShape shape, bool anodic)
    {
        var peaks = new List<GaussianPeak>();
        double maxAbsCurrent = sortedByPotential.Max(p => Math.Abs(p.Current));
        double minAmplitude = Math.Max(1e-6, maxAbsCurrent * 0.005);
        const int radius = 2;
        for (int i = radius; i < sortedByPotential.Count - radius; i++)
        {
            double curr = sortedByPotential[i].Current;
            double maxInWindow = sortedByPotential.Skip(i - radius).Take(2 * radius + 1).Max(p => p.Current);
            double minInWindow = sortedByPotential.Skip(i - radius).Take(2 * radius + 1).Min(p => p.Current);
            double baselineCurrent = EvaluateBaseline(baseline, sortedByPotential[i].Potential);
            double amplitude = sortedByPotential[i].Current - baselineCurrent;

            if (anodic)
            {
                bool isMax = curr >= maxInWindow - 1e-12;
                if (amplitude > minAmplitude && isMax && !peaks.Any(p => Math.Abs(p.Center - sortedByPotential[i].Potential) < 0.02))
                    peaks.Add(new GaussianPeak
                    {
                        Amplitude = amplitude,
                        Center = sortedByPotential[i].Potential,
                        Sigma = EstimatePeakSigma(sortedByPotential, i, amplitude, anodic: true),
                        Shape = shape
                    });
            }
            else
            {
                bool isMin = curr <= minInWindow + 1e-12;
                if (amplitude < -minAmplitude && isMin && !peaks.Any(p => Math.Abs(p.Center - sortedByPotential[i].Potential) < 0.02))
                    peaks.Add(new GaussianPeak
                    {
                        Amplitude = amplitude,
                        Center = sortedByPotential[i].Potential,
                        Sigma = EstimatePeakSigma(sortedByPotential, i, amplitude, anodic: false),
                        Shape = shape
                    });
            }
        }
        return peaks.OrderBy(p => p.Center).ToList();
    }

    private static double EstimatePeakSigma(List<VoltammetryPoint> sortedByPotential, int centerIndex, double amplitude, bool anodic)
    {
        double halfLevel = amplitude * 0.5;
        int left = centerIndex;
        int right = centerIndex;

        while (left > 0)
        {
            double current = sortedByPotential[left].Current;
            if (anodic ? current <= halfLevel : current >= halfLevel)
                break;
            left--;
        }

        while (right < sortedByPotential.Count - 1)
        {
            double current = sortedByPotential[right].Current;
            if (anodic ? current <= halfLevel : current >= halfLevel)
                break;
            right++;
        }

        double width = Math.Abs(sortedByPotential[right].Potential - sortedByPotential[left].Potential);
        if (width < 1e-6)
            return 0.04;

        return Math.Clamp(width / 2.355, 0.01, 0.20);
    }

    private static List<GaussianPeak> SelectStrongestPeaks(List<GaussianPeak> peaks, int requestedCount)
    {
        return peaks
            .OrderByDescending(p => Math.Abs(p.Amplitude))
            .Take(requestedCount)
            .OrderBy(p => p.Center)
            .ToList();
    }

    private static List<GaussianPeak> GetUserDefinedPeaksForBranch(List<GaussianPeak> userPlacedPeaks, Baseline baseline, PeakShape shape, bool anodic, List<VoltammetryPoint> branchPoints)
    {
        if (branchPoints.Count == 0)
            return new List<GaussianPeak>();

        var minPotential = branchPoints.Min(p => p.Potential) - 1e-6;
        var maxPotential = branchPoints.Max(p => p.Potential) + 1e-6;

        return userPlacedPeaks
            .Where(p => p.IsUserDefined && p.Center >= minPotential && p.Center <= maxPotential)
            .Select(p =>
            {
                var amplitude = p.FixedCurrent - EvaluateBaseline(baseline, p.Center);
                return new GaussianPeak
                {
                    Amplitude = amplitude,
                    Center = p.Center,
                    Sigma = p.Sigma,
                    Shape = shape,
                    Eta = p.Eta,
                    IsUserDefined = true,
                    FixedCurrent = p.FixedCurrent
                };
            })
            .Where(p => anodic ? p.Amplitude > 0 : p.Amplitude < 0)
            .OrderBy(p => p.Center)
            .ToList();
    }

    private static (Baseline baseline, List<GaussianPeak> peaks) FitUserDefinedPeaksFixedBaseline(
        List<VoltammetryPoint> points,
        Baseline baseline,
        List<GaussianPeak> peaks)
    {
        try
        {
            int nParams = peaks.Count;
            if (nParams == 0)
                return (baseline, peaks);

            var x0 = peaks.Select(p => Math.Max(1e-4, p.Sigma)).ToArray();
            var simplex = new List<double[]> { x0 };
            const double step = 0.15;
            for (int i = 0; i < nParams; i++)
            {
                var x = (double[])x0.Clone();
                x[i] += step * Math.Abs(x[i] != 0 ? x[i] : 0.05);
                simplex.Add(x);
            }

            const int maxIterations = 80;
            const double alpha = 1.0;
            const double gamma = 2.0;
            const double rho = 0.5;
            const double sigma = 0.5;
            var peakShape = peaks.First().Shape;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                simplex = simplex.OrderBy(x => ObjectiveFunctionFixedAnchors(points, baseline, peaks, x, peakShape)).ToList();
                var best = simplex[0];
                var worst = simplex[^1];
                var secondWorst = simplex[^2];

                var centroid = new double[nParams];
                for (int i = 0; i < simplex.Count - 1; i++)
                    for (int j = 0; j < nParams; j++)
                        centroid[j] += simplex[i][j];
                for (int j = 0; j < nParams; j++)
                    centroid[j] /= (simplex.Count - 1);

                var reflected = new double[nParams];
                for (int j = 0; j < nParams; j++)
                    reflected[j] = centroid[j] + alpha * (centroid[j] - worst[j]);

                double fReflected = ObjectiveFunctionFixedAnchors(points, baseline, peaks, reflected, peakShape);
                double fBest = ObjectiveFunctionFixedAnchors(points, baseline, peaks, best, peakShape);
                double fSecondWorst = ObjectiveFunctionFixedAnchors(points, baseline, peaks, secondWorst, peakShape);

                if (fReflected < fBest)
                {
                    var expanded = new double[nParams];
                    for (int j = 0; j < nParams; j++)
                        expanded[j] = centroid[j] + gamma * (reflected[j] - centroid[j]);
                    simplex[^1] = ObjectiveFunctionFixedAnchors(points, baseline, peaks, expanded, peakShape) < fReflected ? expanded : reflected;
                }
                else if (fReflected < fSecondWorst)
                {
                    simplex[^1] = reflected;
                }
                else
                {
                    var contracted = new double[nParams];
                    for (int j = 0; j < nParams; j++)
                        contracted[j] = centroid[j] + rho * (worst[j] - centroid[j]);

                    if (ObjectiveFunctionFixedAnchors(points, baseline, peaks, contracted, peakShape) < ObjectiveFunctionFixedAnchors(points, baseline, peaks, worst, peakShape))
                    {
                        simplex[^1] = contracted;
                    }
                    else
                    {
                        for (int i = 1; i < simplex.Count; i++)
                            for (int j = 0; j < nParams; j++)
                                simplex[i][j] = best[j] + sigma * (simplex[i][j] - best[j]);
                    }
                }
            }

            var result = simplex[0];
            for (int i = 0; i < peaks.Count; i++)
                peaks[i].Sigma = Math.Max(1e-4, result[i]);
        }
        catch
        {
        }

        return (baseline, peaks);
    }

    private static void ClampPeakParameters(double[] parameters, List<GaussianPeak> initialPeaks, List<VoltammetryPoint> points)
    {
        if (points.Count == 0)
            return;

        double minX = points.Min(p => p.Potential);
        double maxX = points.Max(p => p.Potential);
        double rangeX = Math.Max(1e-6, maxX - minX);
        double minSigma = Math.Max(0.005, rangeX * 0.01);
        double maxSigma = Math.Min(0.25, rangeX * 0.18);

        for (int i = 0; i < initialPeaks.Count; i++)
        {
            double initialAmplitude = initialPeaks[i].Amplitude;
            double initialCenter = initialPeaks[i].Center;
            double centerWindow = Math.Max(0.03, rangeX * 0.08);

            parameters[i * 3] = initialAmplitude >= 0
                ? Math.Max(0, parameters[i * 3])
                : Math.Min(0, parameters[i * 3]);
            parameters[i * 3 + 1] = Math.Max(minX, Math.Min(maxX, Math.Max(initialCenter - centerWindow, Math.Min(initialCenter + centerWindow, parameters[i * 3 + 1]))));
            parameters[i * 3 + 2] = Math.Max(minSigma, Math.Min(maxSigma, parameters[i * 3 + 2]));
        }
    }

    private static (Baseline baseline, List<GaussianPeak> peaks) FitGaussianLinearFixedBaseline(
        List<VoltammetryPoint> points,
        Baseline baseline,
        List<GaussianPeak> peaks)
    {
        try
        {
            int nPoints = points.Count;
            int columns = peaks.Count;
            if (columns == 0)
                return (baseline, peaks);

            var matrix = Matrix<double>.Build.Dense(nPoints, columns);
            var y = Vector<double>.Build.Dense(nPoints);
            for (int i = 0; i < nPoints; i++)
            {
                double x = points[i].Potential;
                y[i] = points[i].Current - EvaluateBaseline(baseline, x);
                for (int p = 0; p < peaks.Count; p++)
                    matrix[i, p] = EvaluateBasis(peaks[p], x);
            }

            var coeff = matrix.QR().Solve(y);
            for (int p = 0; p < peaks.Count; p++)
                peaks[p].Amplitude = coeff[p];
        }
        catch
        {
        }

        return (baseline, peaks);
    }

    private (Baseline baseline, List<GaussianPeak> peaks) FitGaussianLevenbergMarquardtFixedBaseline(
        List<VoltammetryPoint> points,
        Baseline baseline,
        List<GaussianPeak> peaks,
        double[]? pointWeights = null)
    {
        try
        {
            int nParams = peaks.Count * 3;
            if (nParams == 0)
                return (baseline, peaks);

            var parameters = new double[nParams];
            var initialPeaks = peaks
                .Select(p => new GaussianPeak { Amplitude = p.Amplitude, Center = p.Center, Sigma = p.Sigma, Shape = p.Shape, Eta = p.Eta })
                .ToList();
            for (int i = 0; i < peaks.Count; i++)
            {
                parameters[i * 3] = peaks[i].Amplitude;
                parameters[i * 3 + 1] = peaks[i].Center;
                parameters[i * 3 + 2] = peaks[i].Sigma;
            }

            double lambda = 0.01;
            const int maxIterations = 50;
            const double tolerance = 1e-6;
            var peakShape = GetSelectedPeakShape();

            for (int iter = 0; iter < maxIterations; iter++)
            {
                var residuals = ComputeResidualsFixedBaseline(points, baseline, parameters, peaks.Count, peakShape, pointWeights);
                var jacobian = ComputeJacobianFixedBaseline(points, baseline, parameters, peaks.Count, peakShape, pointWeights);
                var jt = jacobian.Transpose();
                var jtj = jt * jacobian;
                for (int i = 0; i < jtj.RowCount; i++)
                    jtj[i, i] += lambda;

                var delta = jtj.LU().Solve(jt * residuals);
                var candidate = new double[nParams];
                for (int i = 0; i < nParams; i++)
                    candidate[i] = parameters[i] - delta[i];
                ClampPeakParameters(candidate, initialPeaks, points);

                double errorOld = residuals.DotProduct(residuals);
                var residualsNew = ComputeResidualsFixedBaseline(points, baseline, candidate, peaks.Count, peakShape, pointWeights);
                double errorNew = residualsNew.DotProduct(residualsNew);

                if (errorNew < errorOld)
                {
                    parameters = candidate;
                    lambda *= 0.1;
                    if (delta.Norm(2) < tolerance)
                        break;
                }
                else
                {
                    lambda *= 10.0;
                }
            }

            for (int i = 0; i < peaks.Count; i++)
            {
                peaks[i].Amplitude = parameters[i * 3];
                peaks[i].Center = parameters[i * 3 + 1];
                peaks[i].Sigma = Math.Max(1e-6, parameters[i * 3 + 2]);
            }
        }
        catch
        {
        }

        return (baseline, peaks);
    }

    private (Baseline baseline, List<GaussianPeak> peaks) FitGaussianNelderMeadFixedBaseline(
        List<VoltammetryPoint> points,
        Baseline baseline,
        List<GaussianPeak> peaks,
        double[]? pointWeights = null)
    {
        try
        {
            int nParams = peaks.Count * 3;
            if (nParams == 0)
                return (baseline, peaks);

            var x0 = new double[nParams];
            var initialPeaks = peaks
                .Select(p => new GaussianPeak { Amplitude = p.Amplitude, Center = p.Center, Sigma = p.Sigma, Shape = p.Shape, Eta = p.Eta })
                .ToList();
            for (int i = 0; i < peaks.Count; i++)
            {
                x0[i * 3] = peaks[i].Amplitude;
                x0[i * 3 + 1] = peaks[i].Center;
                x0[i * 3 + 2] = peaks[i].Sigma;
            }
            ClampPeakParameters(x0, initialPeaks, points);

            var simplex = new List<double[]> { x0 };
            const double step = 0.1;
            for (int i = 0; i < nParams; i++)
            {
                var x = (double[])x0.Clone();
                x[i] += step * Math.Abs(x[i] != 0 ? x[i] : 1.0);
                ClampPeakParameters(x, initialPeaks, points);
                simplex.Add(x);
            }

            const int maxIterations = 100;
            const double alpha = 1.0;
            const double gamma = 2.0;
            const double rho = 0.5;
            const double sigma = 0.5;
            var peakShape = GetSelectedPeakShape();

            for (int iter = 0; iter < maxIterations; iter++)
            {
                simplex = simplex.OrderBy(x => ObjectiveFunctionFixedBaseline(points, baseline, x, peaks.Count, peakShape, pointWeights)).ToList();
                var best = simplex[0];
                var worst = simplex[^1];
                var secondWorst = simplex[^2];

                var centroid = new double[nParams];
                for (int i = 0; i < simplex.Count - 1; i++)
                    for (int j = 0; j < nParams; j++)
                        centroid[j] += simplex[i][j];
                for (int j = 0; j < nParams; j++)
                    centroid[j] /= (simplex.Count - 1);

                var reflected = new double[nParams];
                for (int j = 0; j < nParams; j++)
                    reflected[j] = centroid[j] + alpha * (centroid[j] - worst[j]);
                ClampPeakParameters(reflected, initialPeaks, points);

                double fReflected = ObjectiveFunctionFixedBaseline(points, baseline, reflected, peaks.Count, peakShape, pointWeights);
                double fBest = ObjectiveFunctionFixedBaseline(points, baseline, best, peaks.Count, peakShape, pointWeights);
                double fSecondWorst = ObjectiveFunctionFixedBaseline(points, baseline, secondWorst, peaks.Count, peakShape, pointWeights);

                if (fReflected < fBest)
                {
                    var expanded = new double[nParams];
                    for (int j = 0; j < nParams; j++)
                        expanded[j] = centroid[j] + gamma * (reflected[j] - centroid[j]);
                    ClampPeakParameters(expanded, initialPeaks, points);
                    simplex[^1] = ObjectiveFunctionFixedBaseline(points, baseline, expanded, peaks.Count, peakShape, pointWeights) < fReflected ? expanded : reflected;
                }
                else if (fReflected < fSecondWorst)
                {
                    simplex[^1] = reflected;
                }
                else
                {
                    var contracted = new double[nParams];
                    for (int j = 0; j < nParams; j++)
                        contracted[j] = centroid[j] + rho * (worst[j] - centroid[j]);
                    ClampPeakParameters(contracted, initialPeaks, points);

                    if (ObjectiveFunctionFixedBaseline(points, baseline, contracted, peaks.Count, peakShape, pointWeights) < ObjectiveFunctionFixedBaseline(points, baseline, worst, peaks.Count, peakShape, pointWeights))
                    {
                        simplex[^1] = contracted;
                    }
                    else
                    {
                        for (int i = 1; i < simplex.Count; i++)
                        {
                            for (int j = 0; j < nParams; j++)
                                simplex[i][j] = best[j] + sigma * (simplex[i][j] - best[j]);
                            ClampPeakParameters(simplex[i], initialPeaks, points);
                        }
                    }
                }

                double range = 0;
                for (int j = 0; j < nParams; j++)
                {
                    double min = simplex.Min(x => x[j]);
                    double max = simplex.Max(x => x[j]);
                    range = Math.Max(range, max - min);
                }
                if (range < 1e-6)
                    break;
            }

            var result = simplex[0];
            ClampPeakParameters(result, initialPeaks, points);
            for (int i = 0; i < peaks.Count; i++)
            {
                peaks[i].Amplitude = result[i * 3];
                peaks[i].Center = result[i * 3 + 1];
                peaks[i].Sigma = Math.Max(1e-6, result[i * 3 + 2]);
            }
        }
        catch
        {
        }

        return (baseline, peaks);
    }

    private static (Baseline baseline, List<GaussianPeak> peaks) FitGaussianLinear(
        List<VoltammetryPoint> points,
        Baseline baseline,
        List<GaussianPeak> peaks)
    {
        try
        {
            int nPoints = points.Count;
            int columns = 2 + peaks.Count;

            var matrix = Matrix<double>.Build.Dense(nPoints, columns);
            for (int i = 0; i < nPoints; i++)
            {
                double x = points[i].Potential;
                matrix[i, 0] = 1.0;
                matrix[i, 1] = x;
                for (int p = 0; p < peaks.Count; p++)
                {
                    matrix[i, 2 + p] = EvaluateBasis(peaks[p], x);
                }
            }

            var y = Vector<double>.Build.Dense(points.Select(p => p.Current).ToArray());
            var coeff = matrix.QR().Solve(y);

            baseline.Intercept = coeff[0];
            baseline.Slope = coeff[1];
            for (int p = 0; p < peaks.Count; p++)
                peaks[p].Amplitude = coeff[2 + p];
        }
        catch
        {
        }

        return (baseline, peaks);
    }

    private (Baseline baseline, List<GaussianPeak> peaks) FitGaussianLevenbergMarquardt(
        List<VoltammetryPoint> points,
        Baseline baseline,
        List<GaussianPeak> peaks)
    {
        try
        {
            int nParams = 2 + peaks.Count * 3;
            var params0 = new double[nParams];
            params0[0] = baseline.Intercept;
            params0[1] = baseline.Slope;
            for (int i = 0; i < peaks.Count; i++)
            {
                params0[2 + i * 3] = peaks[i].Amplitude;
                params0[3 + i * 3] = peaks[i].Center;
                params0[4 + i * 3] = peaks[i].Sigma;
            }

            double lambda = 0.01;
            int maxIterations = 50;
            double tolerance = 1e-6;

            for (int iter = 0; iter < maxIterations; iter++)
            {
            var peakShape = GetSelectedPeakShape();
            var residuals = ComputeResiduals(points, params0, peaks.Count, peakShape);
            var jacobian = ComputeJacobian(points, params0, peaks.Count, peakShape);
                
                var jt = jacobian.Transpose();
                var jtj = jt * jacobian;

                for (int i = 0; i < jtj.RowCount; i++)
                    jtj[i, i] += lambda;

                var jtr = jt * residuals;
                var delta = jtj.LU().Solve(jtr);
                
                var paramsNew = new double[nParams];
                for (int i = 0; i < nParams; i++)
                    paramsNew[i] = params0[i] - delta[i];

                double errorOld = residuals.DotProduct(residuals);
                var residualsNew = ComputeResiduals(points, paramsNew, peaks.Count, peakShape);
                double errorNew = residualsNew.DotProduct(residualsNew);

                if (errorNew < errorOld)
                {
                    params0 = paramsNew;
                    lambda *= 0.1;
                    if (delta.Norm(2) < tolerance)
                        break;
                }
                else
                {
                    lambda *= 10.0;
                }
            }

            baseline.Intercept = params0[0];
            baseline.Slope = params0[1];
            for (int i = 0; i < peaks.Count; i++)
            {
                peaks[i].Amplitude = params0[2 + i * 3];
                peaks[i].Center = params0[3 + i * 3];
                peaks[i].Sigma = Math.Max(1e-6, params0[4 + i * 3]);
            }
        }
        catch
        {
        }

        return (baseline, peaks);
    }

    private static Vector<double> ComputeResiduals(List<VoltammetryPoint> points, double[] parameters, int peakCount, PeakShape peakShape)
    {
        var residuals = new double[points.Count];
        double intercept = parameters[0];
        double slope = parameters[1];

        for (int i = 0; i < points.Count; i++)
        {
            double x = points[i].Potential;
            double y = points[i].Current;
            double model = intercept + slope * x;

            for (int p = 0; p < peakCount; p++)
            {
                double amp = parameters[2 + p * 3];
                double center = parameters[3 + p * 3];
                double sigma = Math.Max(1e-6, parameters[4 + p * 3]);
                var peak = new GaussianPeak { Amplitude = amp, Center = center, Sigma = sigma, Shape = peakShape };
                model += EvaluatePeak(peak, x);
            }

            residuals[i] = y - model;
        }

        return Vector<double>.Build.Dense(residuals);
    }

    private static Matrix<double> ComputeJacobian(List<VoltammetryPoint> points, double[] parameters, int peakCount, PeakShape peakShape)
    {
        int nPoints = points.Count;
        int nParams = 2 + peakCount * 3;
        var jacobian = Matrix<double>.Build.Dense(nPoints, nParams);
        double eps = 1e-6;

            var residuals0 = ComputeResiduals(points, parameters, peakCount, peakShape);

        for (int j = 0; j < nParams; j++)
        {
            var paramsPerturbed = (double[])parameters.Clone();
            paramsPerturbed[j] += eps;
            var residualsPerturbed = ComputeResiduals(points, paramsPerturbed, peakCount, peakShape);
            
            for (int i = 0; i < nPoints; i++)
            {
                jacobian[i, j] = (residualsPerturbed[i] - residuals0[i]) / eps;
            }
        }

        return jacobian;
    }

    private static Vector<double> ComputeResidualsFixedBaseline(List<VoltammetryPoint> points, Baseline baseline, double[] parameters, int peakCount, PeakShape peakShape, double[]? pointWeights = null)
    {
        var residuals = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            double x = points[i].Potential;
            double y = points[i].Current;
            double model = EvaluateBaseline(baseline, x);

            for (int p = 0; p < peakCount; p++)
            {
                double amp = parameters[p * 3];
                double center = parameters[p * 3 + 1];
                double sigma = Math.Max(1e-6, parameters[p * 3 + 2]);
                var peak = new GaussianPeak { Amplitude = amp, Center = center, Sigma = sigma, Shape = peakShape };
                model += EvaluatePeak(peak, x);
            }

            double r = y - model;
            double w = pointWeights != null && i < pointWeights.Length ? Math.Sqrt(pointWeights[i]) : 1.0;
            residuals[i] = r * w;
        }

        return Vector<double>.Build.Dense(residuals);
    }

    private static Matrix<double> ComputeJacobianFixedBaseline(List<VoltammetryPoint> points, Baseline baseline, double[] parameters, int peakCount, PeakShape peakShape, double[]? pointWeights = null)
    {
        int nPoints = points.Count;
        int nParams = peakCount * 3;
        var jacobian = Matrix<double>.Build.Dense(nPoints, nParams);
        const double eps = 1e-6;
        var residuals0 = ComputeResidualsFixedBaseline(points, baseline, parameters, peakCount, peakShape, pointWeights);

        for (int j = 0; j < nParams; j++)
        {
            var paramsPerturbed = (double[])parameters.Clone();
            paramsPerturbed[j] += eps;
            var residualsPerturbed = ComputeResidualsFixedBaseline(points, baseline, paramsPerturbed, peakCount, peakShape, pointWeights);
            for (int i = 0; i < nPoints; i++)
                jacobian[i, j] = (residualsPerturbed[i] - residuals0[i]) / eps;
        }

        return jacobian;
    }

    private (Baseline baseline, List<GaussianPeak> peaks) FitGaussianNelderMead(
        List<VoltammetryPoint> points,
        Baseline baseline,
        List<GaussianPeak> peaks)
    {
        try
        {
            int nParams = 2 + peaks.Count * 3;

            var x0 = new double[nParams];
            x0[0] = baseline.Intercept;
            x0[1] = baseline.Slope;
            for (int i = 0; i < peaks.Count; i++)
            {
                x0[2 + i * 3] = peaks[i].Amplitude;
                x0[3 + i * 3] = peaks[i].Center;
                x0[4 + i * 3] = peaks[i].Sigma;
            }

            var simplex = new List<double[]>();
            simplex.Add(x0);
            double step = 0.1;
            for (int i = 0; i < nParams; i++)
            {
                var x = (double[])x0.Clone();
                x[i] += step * Math.Abs(x[i] != 0 ? x[i] : 1.0);
                simplex.Add(x);
            }

            int maxIterations = 100;
            double alpha = 1.0, gamma = 2.0, rho = 0.5, sigma = 0.5;

            var peakShape = GetSelectedPeakShape();
            for (int iter = 0; iter < maxIterations; iter++)
            {
                simplex = simplex.OrderBy(x => ObjectiveFunction(points, x, peaks.Count, peakShape)).ToList();
                
                var best = simplex[0];
                var worst = simplex[^1];
                var secondWorst = simplex[^2];

                var centroid = new double[nParams];
                for (int i = 0; i < simplex.Count - 1; i++)
                {
                    for (int j = 0; j < nParams; j++)
                        centroid[j] += simplex[i][j];
                }
                for (int j = 0; j < nParams; j++)
                    centroid[j] /= (simplex.Count - 1);

                var reflected = new double[nParams];
                for (int j = 0; j < nParams; j++)
                    reflected[j] = centroid[j] + alpha * (centroid[j] - worst[j]);

                double fReflected = ObjectiveFunction(points, reflected, peaks.Count, peakShape);
                double fBest = ObjectiveFunction(points, best, peaks.Count, peakShape);
                double fSecondWorst = ObjectiveFunction(points, secondWorst, peaks.Count, peakShape);

                if (fReflected < fBest)
                {
                    var expanded = new double[nParams];
                    for (int j = 0; j < nParams; j++)
                        expanded[j] = centroid[j] + gamma * (reflected[j] - centroid[j]);
                    
                    if (ObjectiveFunction(points, expanded, peaks.Count, peakShape) < fReflected)
                        simplex[^1] = expanded;
                    else
                        simplex[^1] = reflected;
                }
                else if (fReflected < fSecondWorst)
                {
                    simplex[^1] = reflected;
                }
                else
                {
                    var contracted = new double[nParams];
                    for (int j = 0; j < nParams; j++)
                        contracted[j] = centroid[j] + rho * (worst[j] - centroid[j]);
                    
                    if (ObjectiveFunction(points, contracted, peaks.Count, peakShape) < ObjectiveFunction(points, worst, peaks.Count, peakShape))
                        simplex[^1] = contracted;
                    else
                    {
                        for (int i = 1; i < simplex.Count; i++)
                        {
                            for (int j = 0; j < nParams; j++)
                                simplex[i][j] = best[j] + sigma * (simplex[i][j] - best[j]);
                        }
                    }
                }

                double range = 0;
                for (int j = 0; j < nParams; j++)
                {
                    double min = simplex.Min(x => x[j]);
                    double max = simplex.Max(x => x[j]);
                    range = Math.Max(range, max - min);
                }
                if (range < 1e-6)
                    break;
            }

            var result = simplex[0];
            baseline.Intercept = result[0];
            baseline.Slope = result[1];
            for (int i = 0; i < peaks.Count; i++)
            {
                peaks[i].Amplitude = result[2 + i * 3];
                peaks[i].Center = result[3 + i * 3];
                peaks[i].Sigma = Math.Max(1e-6, result[4 + i * 3]);
            }
        }
        catch
        {
        }

        return (baseline, peaks);
    }

    private static double ObjectiveFunction(List<VoltammetryPoint> points, double[] parameters, int peakCount, PeakShape peakShape)
    {
        double intercept = parameters[0];
        double slope = parameters[1];
        double sumSquaredError = 0;

        for (int i = 0; i < points.Count; i++)
        {
            double x = points[i].Potential;
            double y = points[i].Current;
            double model = intercept + slope * x;

            for (int p = 0; p < peakCount; p++)
            {
                double amp = parameters[2 + p * 3];
                double center = parameters[3 + p * 3];
                double sigma = Math.Max(1e-6, parameters[4 + p * 3]);
                var peak = new GaussianPeak { Amplitude = amp, Center = center, Sigma = sigma, Shape = peakShape };
                model += EvaluatePeak(peak, x);
            }

            double error = y - model;
            sumSquaredError += error * error;
        }

        return sumSquaredError;
    }

    private static double ObjectiveFunctionFixedBaseline(List<VoltammetryPoint> points, Baseline baseline, double[] parameters, int peakCount, PeakShape peakShape, double[]? pointWeights = null)
    {
        double sumSquaredError = 0;

        for (int i = 0; i < points.Count; i++)
        {
            double x = points[i].Potential;
            double y = points[i].Current;
            double model = EvaluateBaseline(baseline, x);

            for (int p = 0; p < peakCount; p++)
            {
                double amp = parameters[p * 3];
                double center = parameters[p * 3 + 1];
                double sigma = Math.Max(1e-6, parameters[p * 3 + 2]);
                var peak = new GaussianPeak { Amplitude = amp, Center = center, Sigma = sigma, Shape = peakShape };
                model += EvaluatePeak(peak, x);
            }

            double error = y - model;
            double w = pointWeights != null && i < pointWeights.Length ? pointWeights[i] : 1.0;
            sumSquaredError += w * error * error;
        }

        return sumSquaredError;
    }

    private static double[] GetEndpointWeights(List<VoltammetryPoint> points, double endpointFraction = 0.08, double endpointWeight = 10.0)
    {
        if (points.Count == 0) return new double[0];
        int n = Math.Max(2, (int)(points.Count * endpointFraction));
        var w = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
            w[i] = (i < n || i >= points.Count - n) ? endpointWeight : 1.0;
        return w;
    }

    private static double GetClosureError(List<VoltammetryPoint> branchPoints, Baseline baseline, List<GaussianPeak> peaks)
    {
        if (branchPoints.Count == 0 || baseline == null) return 0;
        int checkCount = Math.Min(3, Math.Max(1, branchPoints.Count / 10));
        double maxErr = 0;
        for (int i = 0; i < checkCount; i++)
        {
            double u = branchPoints[i].Potential;
            double actual = branchPoints[i].Current;
            double model = EvaluateBaseline(baseline, u) + peaks.Sum(p => EvaluatePeak(p, u));
            maxErr = Math.Max(maxErr, Math.Abs(actual - model));
        }
        for (int i = branchPoints.Count - checkCount; i < branchPoints.Count; i++)
        {
            if (i < 0) continue;
            double u = branchPoints[i].Potential;
            double actual = branchPoints[i].Current;
            double model = EvaluateBaseline(baseline, u) + peaks.Sum(p => EvaluatePeak(p, u));
            maxErr = Math.Max(maxErr, Math.Abs(actual - model));
        }
        return maxErr;
    }

    private static double ObjectiveFunctionFixedAnchors(List<VoltammetryPoint> points, Baseline baseline, List<GaussianPeak> peaks, double[] sigmas, PeakShape peakShape)
    {
        double sumSquaredError = 0;

        for (int i = 0; i < points.Count; i++)
        {
            double x = points[i].Potential;
            double y = points[i].Current;
            double model = EvaluateBaseline(baseline, x);

            for (int p = 0; p < peaks.Count; p++)
            {
                var peak = new GaussianPeak
                {
                    Amplitude = peaks[p].Amplitude,
                    Center = peaks[p].Center,
                    Sigma = Math.Max(1e-4, sigmas[p]),
                    Shape = peakShape,
                    Eta = peaks[p].Eta
                };
                model += EvaluatePeak(peak, x);
            }

            double error = y - model;
            sumSquaredError += error * error;
        }

        return sumSquaredError;
    }

    private PeakShape GetSelectedPeakShape()
    {
        return _peakShapeIndex switch
        {
            1 => PeakShape.Lorentzian,
            2 => PeakShape.PseudoVoigt,
            _ => PeakShape.Gaussian
        };
    }

    private static double EvaluateBasis(GaussianPeak peak, double x)
    {
        double sigma = Math.Max(1e-6, peak.Sigma);
        switch (peak.Shape)
        {
            case PeakShape.Lorentzian:
                {
                    double t = (x - peak.Center) / sigma;
                    return 1.0 / (1.0 + t * t);
                }
            case PeakShape.PseudoVoigt:
                {
                    double t = (x - peak.Center) / sigma;
                    double lor = 1.0 / (1.0 + t * t);
                    double gau = Math.Exp(-0.5 * t * t);
                    return peak.Eta * lor + (1.0 - peak.Eta) * gau;
                }
            default:
                {
                    double t = (x - peak.Center) / sigma;
                    return Math.Exp(-0.5 * t * t);
                }
        }
    }

    private static double EvaluatePeak(GaussianPeak peak, double x)
    {
        return peak.Amplitude * EvaluateBasis(peak, x);
    }

    private void ExportPeaks()
    {
        if (_experiment == null || (!_anodicFit.HasValue && !_cathodicFit.HasValue))
        {
            MessageBox.Show("Выполните аппроксимацию пиков.");
            return;
        }

        var anodicPeaks = _anodicFit.HasValue ? _anodicFit.Value.peaks.OrderBy(p => p.Center) : Enumerable.Empty<GaussianPeak>();
        var cathodicPeaks = _cathodicFit.HasValue ? _cathodicFit.Value.peaks.OrderBy(p => p.Center) : Enumerable.Empty<GaussianPeak>();

        var sb = new StringBuilder();
        sb.Append("Название образца");

        int anodicIndex = 1;
        foreach (var p in anodicPeaks)
            sb.Append($" | Пик анодный {anodicIndex++}");

        int cathodicIndex = 1;
        foreach (var p in cathodicPeaks)
            sb.Append($" | Пик катодный {cathodicIndex++}");

        sb.AppendLine();
        sb.Append(_experiment.SampleName);

        foreach (var p in anodicPeaks)
            sb.Append($" | {p.Center:F3}");
        foreach (var p in cathodicPeaks)
            sb.Append($" | {p.Center:F3}");

        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Текстовый файл|*.txt",
            FileName = "пики_ЦВА.txt"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            File.WriteAllText(saveFileDialog.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"Данные сохранены в:\n{saveFileDialog.FileName}");
        }
    }

    private void ExportData()
    {
        if (_experiment == null)
        {
            MessageBox.Show("Загрузите данные эксперимента.");
            return;
        }

        var points = GetMainPointsOrderedByTime();

        var exportData = new CyclicVoltammetryData
        {
            SampleName = _experiment.SampleName
        };

        foreach (var p in points)
            exportData.Points.Add(p);

        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Текстовый файл|*.txt",
            FileName = $"{_experiment.SampleName}_данные.txt"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            DataExporter.ExportToTxt(exportData, saveFileDialog.FileName);
            MessageBox.Show($"Данные сохранены в:\n{saveFileDialog.FileName}");
        }
    }

    private void LoadReactions()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Табличные файлы (*.txt;*.tsv;*.csv)|*.txt;*.tsv;*.csv|Все файлы (*.*)|*.*",
            Title = "Выберите файл библиотеки реакций"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var service = new ReactionLibraryService();
            var reactions = service.LoadFromFile(dialog.FileName);
            LoadReactionsIntoView(reactions);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка при загрузке библиотеки реакций:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UpdateReactionPotentials()
    {
        if (string.IsNullOrWhiteSpace(_reactionPhText))
        {
            _reactionPh = null;
        }
        else
        {
            var normalized = _reactionPhText.Replace(',', '.');
            if (double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pH))
                _reactionPh = pH;
        }

        foreach (var reaction in _allReactions)
            reaction.UpdatePotential(_reactionPh);
    }

    private void LoadReactionsFromDb()
    {
        try
        {
            var db = new ReactionDatabaseService();
            var reactions = db.LoadAll();
            LoadReactionsIntoView(reactions);
            if (reactions.Count == 0)
                MessageBox.Show(
                    "В базе пока нет реакций.\n\nИспользуйте «Импорт в БД», чтобы загрузить реакции из файла в базу, или «Показать из файла» для разового просмотра без сохранения в БД.",
                    "Реакции",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка при загрузке из БД:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ImportReactionsToDb()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Табличные файлы (*.txt;*.tsv;*.csv)|*.txt;*.tsv;*.csv|Все файлы (*.*)|*.*",
            Title = "Импорт библиотеки реакций"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var service = new ReactionLibraryService();
            var reactions = service.LoadFromFile(dialog.FileName);
            var db = new ReactionDatabaseService();
            db.SaveReactions(reactions);
            LoadReactionsIntoView(reactions);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка при импорте в БД:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AddReaction()
    {
        if (string.IsNullOrWhiteSpace(_newReactionText))
        {
            MessageBox.Show("Введите уравнение реакции");
            return;
        }

        if (!TryParseDouble(_newReactionE0, out var e0) ||
            !int.TryParse(_newReactionN, out var n) ||
            !TryParseDouble(_newReactionKH, out var kH) ||
            !TryParseDouble(_newReactionKOH, out var kOH))
        {
            MessageBox.Show("Проверьте числовые поля (E0, n, k(H+), k(OH-))");
            return;
        }

        var entry = new ReactionEntry
        {
            Reaction = _newReactionText.Trim(),
            E0 = e0,
            N = n,
            KHPlus = kH,
            KOHMinus = kOH
        };

        try
        {
            var db = new ReactionDatabaseService();
            db.SaveReaction(entry);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при сохранении в БД:\n{ex.Message}");
            return;
        }

        LoadReactionsIntoView(new[] { entry }, append: true);
        NewReactionText = string.Empty;
        NewReactionE0 = string.Empty;
        NewReactionN = string.Empty;
        NewReactionKH = string.Empty;
        NewReactionKOH = string.Empty;
    }

    private void LoadReactionsIntoView(IEnumerable<ReactionEntry> reactions, bool append = false)
    {
        if (!append)
        {
            _allReactions.Clear();
            _potentialSearchText = string.Empty;
            OnPropertyChanged(nameof(PotentialSearchText));
        }

        foreach (var r in reactions)
        {
            var vm = new ReactionEntryViewModel(r.Reaction, r.E0, r.N, r.KHPlus, r.KOHMinus);
            vm.UpdatePotential(_reactionPh);
            _allReactions.Add(vm);
        }

        ApplyReactionFilter();
    }

    private void ApplyReactionFilter()
    {
        UpdateReactionPotentials();

        Reactions.Clear();
        if (_allReactions.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(_potentialSearchText) || !TryParseDouble(_potentialSearchText, out var target))
        {
            foreach (var r in _allReactions)
                Reactions.Add(r);
            return;
        }

        double tolerance = 0.05;
        if (TryParseDouble(_potentialToleranceText, out var parsedTol) && parsedTol > 0)
            tolerance = parsedTol;

        foreach (var reaction in _allReactions)
        {
            double value = reaction.AdjustedPotential ?? reaction.E0;
            if (Math.Abs(value - target) <= tolerance)
                Reactions.Add(reaction);
        }
    }

    private static bool TryParseDouble(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var normalized = text.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void LoadScanRateSeries()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt",
            Title = "Выберите файл с серией циклов"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var parser = new TxtCvaSeriesParser();
            var (sampleName, cycles) = parser.Parse(dialog.FileName);
            _scanRateSampleName = sampleName;
            _scanRateCycles = cycles;

            MessageBox.Show($"Загружено циклов: {_scanRateCycles.Count}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка при загрузке серии:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AnalyzeScanRateSeries()
    {
        if (_scanRateCycles.Count == 0)
        {
            MessageBox.Show("Загрузите серию циклов.");
            return;
        }

        var rates = ParseScanRates(_scanRatesInput);
        if (rates.Count == 0)
        {
            MessageBox.Show("Введите список скоростей развертки");
            return;
        }

        if (!TryParseDouble(_scanRateTargetPotentialText, out var targetPotential))
        {
            MessageBox.Show("Введите потенциал (допускается дробное значение)");
            return;
        }

        if (_scanRateElectrodeAreaCm2 <= 0)
        {
            MessageBox.Show("Площадь электрода должна быть больше 0");
            return;
        }

        if (rates.Count != _scanRateCycles.Count)
        {
            MessageBox.Show("Количество скоростей не совпадает с числом циклов");
            return;
        }

        ScanRateTable.Clear();
        var currentDensities = new List<double>();
        var scanRates = new List<double>();

        bool anodic = _scanRateBranchIndex == 0;
        for (int i = 0; i < _scanRateCycles.Count; i++)
        {
            var cycle = _scanRateCycles[i];
            double current = GetCurrentAtPotential(cycle, targetPotential, anodic);
            double density = current / _scanRateElectrodeAreaCm2;

            scanRates.Add(rates[i]);
            currentDensities.Add(density);
            ScanRateTable.Add(new ScanRatePoint
            {
                ScanRate = rates[i],
                CurrentDensity = density
            });
        }

        UpdateScanRatePlots(scanRates, currentDensities);
    }

    private void ExportScanRateTable()
    {
        if (ScanRateTable.Count == 0)
        {
            MessageBox.Show("Выполните анализ скорости развертки.");
            return;
        }

        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Текстовый файл|*.txt",
            FileName = $"{_scanRateSampleName}_скорости.txt"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Название образца\tСкорость развертки, мВ/с\tПлотность тока, А/см^2");
            foreach (var row in ScanRateTable)
            {
                sb.AppendLine($"{_scanRateSampleName}\t{row.ScanRate:F3}\t{row.CurrentDensity:E}");
            }

            File.WriteAllText(saveFileDialog.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"Данные сохранены в:\n{saveFileDialog.FileName}");
        }
    }

    private static List<double> ParseScanRates(string input)
    {
        var list = new List<double>();
        var tokens = input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var normalized = token.Replace(',', '.');
            if (double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                list.Add(value);
        }
        return list;
    }

    private static double GetCurrentAtPotential(CyclicVoltammetryData cycle, double targetPotential, bool anodic)
    {
        var intersections = new List<double>();
        for (int i = 0; i < cycle.Points.Count - 1; i++)
        {
            var p1 = cycle.Points[i];
            var p2 = cycle.Points[i + 1];

            if ((p1.Potential - targetPotential) * (p2.Potential - targetPotential) <= 0 &&
                Math.Abs(p2.Potential - p1.Potential) > double.Epsilon)
            {
                double t = (targetPotential - p1.Potential) / (p2.Potential - p1.Potential);
                double current = p1.Current + (p2.Current - p1.Current) * t;
                intersections.Add(current);
            }
        }

        if (intersections.Count > 0)
            return anodic ? intersections.Max() : intersections.Min();

        var nearest = cycle.Points
            .OrderBy(p => Math.Abs(p.Potential - targetPotential))
            .First();
        return nearest.Current;
    }

    private void UpdateScanRatePlots(List<double> scanRates, List<double> currentDensities)
    {
        CurrentDensityVsScanRate.Series.Clear();
        CurrentDensityVsScanRate.Axes.Clear();
        CurrentDensityVsSqrtScanRate.Series.Clear();
        CurrentDensityVsSqrtScanRate.Axes.Clear();
        ScanRateTabPlotModel.Series.Clear();
        ScanRateTabPlotModel.Axes.Clear();
        ScanRateGammaSurfaceResult = string.Empty;

        if (scanRates.Count < 2)
        {
            ScanRateConclusion = "Недостаточно данных";
            ScanRateFitInfo = string.Empty;
            _scanRateTabPointsV = null;
            _scanRateTabPointsSqrt = null;
            _scanRateTabFitV = null;
            _scanRateTabFitSqrt = null;
            ScanRateTabPlotModel.InvalidatePlot(true);
            return;
        }

        var pointsV = scanRates.Select((v, i) => new DataPoint(v, currentDensities[i])).ToList();
        var pointsSqrt = scanRates.Select((v, i) => new DataPoint(Math.Sqrt(v), currentDensities[i])).ToList();

        var fitV = FitLine(pointsV);
        var fitSqrt = FitLine(pointsSqrt);

        _scanRateTabPointsV = pointsV;
        _scanRateTabPointsSqrt = pointsSqrt;
        _scanRateTabFitV = fitV;
        _scanRateTabFitSqrt = fitSqrt;

        const double r2Threshold = 0.95;
        AddScatterWithOptionalFit(CurrentDensityVsScanRate, pointsV, "Скорость развертки, мВ/с", fitV, fitV.r2 >= r2Threshold);
        AddScatterWithOptionalFit(CurrentDensityVsSqrtScanRate, pointsSqrt, "√(скорости), √(мВ/с)", fitSqrt, fitSqrt.r2 >= r2Threshold);

        UpdateScanRateTabPlot();

        ScanRateFitInfo = $"R²(I-v) = {fitV.r2:F3}; R²(I-√v) = {fitSqrt.r2:F3}";

        if (fitV.r2 >= 0.98)
        {
            ScanRateConclusion = "Поверхностно контролируемая реакция";
            var pointsVVolt = scanRates.Select((v, i) => new DataPoint(v * 0.001, currentDensities[i])).ToList();
            var fitVVolt = FitLine(pointsVVolt);
            var gamma = CalculateSurfaceGammaFromSlope(fitVVolt.slope);
            if (gamma.HasValue)
                ScanRateGammaSurfaceResult = $"Γ (Барди-Фолкнер) = {gamma.Value:F2} мкмоль/м²";
        }
        else if (fitSqrt.r2 >= 0.98)
            ScanRateConclusion = "Диффузионно контролируемая реакция";
        else
            ScanRateConclusion = "Смешанный характер процесса";
    }

    private void UpdateScanRateTabPlot()
    {
        ScanRateTabPlotModel.Series.Clear();
        ScanRateTabPlotModel.Axes.Clear();
        if (_scanRateTabPointsV == null || _scanRateTabPointsSqrt == null)
        {
            ScanRateTabPlotModel.InvalidatePlot(true);
            return;
        }

        const double r2Threshold = 0.95;
        bool useSqrt = _scanRateTabCoordinateIndex == 1;
        var points = useSqrt ? _scanRateTabPointsSqrt : _scanRateTabPointsV;
        var fit = useSqrt ? _scanRateTabFitSqrt : _scanRateTabFitV;
        string xTitle = useSqrt ? "√(скорости развертки), √(мВ/с)" : "Скорость развертки, мВ/с";
        bool drawLine = fit.HasValue && fit.Value.r2 >= r2Threshold;

        var xAxis = new LinearAxis { Position = AxisPosition.Bottom, Title = xTitle };
        var yAxis = new LinearAxis { Position = AxisPosition.Left, Title = "Плотность тока, А/см²" };
        ScanRateTabPlotModel.Axes.Add(xAxis);
        ScanRateTabPlotModel.Axes.Add(yAxis);

        var scatter = new ScatterSeries
        {
            MarkerType = MarkerType.Circle,
            MarkerFill = OxyColors.DarkBlue,
            ItemsSource = points,
            Title = "Точки"
        };
        ScanRateTabPlotModel.Series.Add(scatter);

        if (drawLine && fit.HasValue)
        {
            var f = fit.Value;
            var xMin = points.Min(p => p.X);
            var xMax = points.Max(p => p.X);
            var fitPoints = new[]
            {
                new DataPoint(xMin, f.slope * xMin + f.intercept),
                new DataPoint(xMax, f.slope * xMax + f.intercept)
            };
            var line = new LineSeries
            {
                ItemsSource = fitPoints,
                Color = OxyColors.Red,
                StrokeThickness = 2,
                Title = "Аппроксимация"
            };
            ScanRateTabPlotModel.Series.Add(line);
        }

        ScanRateTabPlotModel.InvalidatePlot(true);
    }

    private static void AddScatterWithOptionalFit(PlotModel model, List<DataPoint> points, string xTitle,
        (double slope, double intercept, double r2) fit, bool drawLine)
    {
        var xAxis = new LinearAxis { Position = AxisPosition.Bottom, Title = xTitle };
        var yAxis = new LinearAxis { Position = AxisPosition.Left, Title = "Плотность тока, А/см²" };
        model.Axes.Add(xAxis);
        model.Axes.Add(yAxis);

        var scatter = new ScatterSeries
        {
            MarkerType = MarkerType.Circle,
            MarkerFill = OxyColors.DarkBlue,
            ItemsSource = points
        };
        model.Series.Add(scatter);

        if (drawLine)
        {
            var xMin = points.Min(p => p.X);
            var xMax = points.Max(p => p.X);
            var fitPoints = new[]
            {
                new DataPoint(xMin, fit.slope * xMin + fit.intercept),
                new DataPoint(xMax, fit.slope * xMax + fit.intercept)
            };
            var line = new LineSeries
            {
                ItemsSource = fitPoints,
                Color = OxyColors.Red,
                StrokeThickness = 2
            };
            model.Series.Add(line);
        }
        model.InvalidatePlot(true);
    }

    private static (double slope, double intercept, double r2) FitLine(List<DataPoint> points)
    {
        double xMean = points.Average(p => p.X);
        double yMean = points.Average(p => p.Y);

        double ssXY = points.Sum(p => (p.X - xMean) * (p.Y - yMean));
        double ssXX = points.Sum(p => (p.X - xMean) * (p.X - xMean));
        double slope = ssXX == 0 ? 0 : ssXY / ssXX;
        double intercept = yMean - slope * xMean;

        double ssTot = points.Sum(p => Math.Pow(p.Y - yMean, 2));
        double ssRes = points.Sum(p => Math.Pow(p.Y - (slope * p.X + intercept), 2));
        double r2 = ssTot == 0 ? 0 : 1 - (ssRes / ssTot);

        return (slope, intercept, r2);
    }

    private double? CalculateSurfaceGammaFromSlope(double slopeCurrentDensityPerVolt)
    {
        if (_scanRateElectronCount <= 0 || _scanRateTemperatureK <= 0)
            return null;

        const double faraday = 96485.33212;
        const double gasConstant = 8.314462618;
        double n = _scanRateElectronCount;

        double gammaMolPerCm2 = (4 * gasConstant * _scanRateTemperatureK * slopeCurrentDensityPerVolt)
            / (n * n * faraday * faraday);

        double gammaMicroMolPerM2 = gammaMolPerCm2 * 1e10;
        return gammaMicroMolPerM2;
    }

    private void LoadBackgroundLibrary()
    {
        try
        {
            var service = new BackgroundLibraryService();
            _allBackgrounds = service.LoadAllBackgrounds();
            ApplyBackgroundFilters();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка при загрузке библиотеки фонов:\n{ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ApplyBackgroundFilters()
    {
        BackgroundLibrary.Clear();
        if (_allBackgrounds.Count == 0)
            return;

        var filterParam = (FilterParameter)_selectedFilterParameterIndex;

        if (filterParam == FilterParameter.None || string.IsNullOrWhiteSpace(_filterValue))
        {
            foreach (var bg in _allBackgrounds)
                BackgroundLibrary.Add(bg);
            return;
        }

        foreach (var bg in _allBackgrounds)
        {
            bool matches = filterParam switch
            {
                FilterParameter.SampleName => MatchesFilter(bg.SampleName, _filterValue),
                FilterParameter.ScanRate => TryParseDouble(_filterValue, out var sr) && Math.Abs(bg.Metadata.ScanRate - sr) < 1e-9,
                FilterParameter.Electrolyte => MatchesFilter(bg.Metadata.Electrolyte, _filterValue),
                FilterParameter.WorkingElectrode => MatchesFilter(bg.Metadata.WorkingElectrode, _filterValue),
                FilterParameter.ReferenceElectrode => MatchesFilter(bg.Metadata.ReferenceElectrode, _filterValue),
                FilterParameter.Atmosphere => MatchesFilter(bg.Metadata.Atmosphere, _filterValue),
                FilterParameter.CellType => MatchesFilter(bg.Metadata.CellType, _filterValue),
                FilterParameter.DepositionMethod => MatchesFilter(bg.Metadata.DepositionMethod, _filterValue),
                FilterParameter.Illumination => MatchesFilter(bg.Metadata.Illumination, _filterValue),
                _ => true
            };

            if (matches)
                BackgroundLibrary.Add(bg);
        }
    }

    private void ClearBackgroundFilters()
    {
        SelectedFilterParameterIndex = 0;
        FilterValue = string.Empty;
    }

    private void UseSelectedBackground()
    {
        if (_selectedBackground == null)
        {
            MessageBox.Show("Выберите фон из списка");
            return;
        }

        _background = _selectedBackground;
        OnPropertyChanged(nameof(HasBackground));
        RebuildPlots();
    }

    private void EditSelectedBackground()
    {
        if (_selectedBackground == null)
        {
            MessageBox.Show("Выберите фон из списка для редактирования");
            return;
        }

        var window = new BackgroundMetadataWindow();
        window.SetInitialData(_selectedBackground);
        if (window.ShowDialog() != true)
            return;

        var meta = window.GetMetadataFromInputs();
        if (meta.ScanRate <= 0)
        {
            MessageBox.Show("Скорость развертки должна быть больше 0.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var updated = new BackgroundData
        {
            SampleName = window.GetSampleName(),
            Metadata = new BackgroundMetadata
            {
                Id = _selectedBackground.Metadata.Id,
                ScanRate = meta.ScanRate,
                Electrolyte = meta.Electrolyte,
                WorkingElectrode = meta.WorkingElectrode,
                ReferenceElectrode = meta.ReferenceElectrode,
                Atmosphere = meta.Atmosphere,
                CellType = meta.CellType,
                DepositionMethod = meta.DepositionMethod,
                Illumination = meta.Illumination
            }
        };
        foreach (var p in _selectedBackground.Points)
            updated.Points.Add(p);

        var service = new BackgroundLibraryService();
        service.UpdateBackground(updated);
        LoadBackgroundLibrary();
        MessageBox.Show("Запись обновлена.");
    }

    private void DeleteSelectedBackground()
    {
        if (_selectedBackground == null)
        {
            MessageBox.Show("Выберите фон из списка для удаления");
            return;
        }

        var result = MessageBox.Show(
            $"Удалить запись «{_selectedBackground.SampleName}» (скорость {_selectedBackground.Metadata.ScanRate} мВ/с) из библиотеки?",
            "Удаление фона",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        var service = new BackgroundLibraryService();
        service.DeleteBackground(_selectedBackground.Metadata.Id);
        if (_background?.Metadata.Id == _selectedBackground.Metadata.Id)
        {
            _background = null;
            OnPropertyChanged(nameof(HasBackground));
            RebuildPlots();
        }
        LoadBackgroundLibrary();
    }

    private static bool MatchesFilter(string value, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        return value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCurrentPlotModel()
    {
        CurrentPlotModel = _plotSelectionIndex switch
        {
            1 => PotentialVsCurrent,
            2 => PotentialVsTime,
            3 => CurrentVsTime,
            4 => ChargeVsTime,
            5 => CurrentDensityVsScanRate,
            6 => CurrentDensityVsSqrtScanRate,
            _ => CurrentVsPotential
        };
        
        RebuildPlots();
        RefreshLegendItems(CurrentPlotModel);
    }

    private void AddZeroLine()
    {
        if (_zeroLines.Count > 0)
        {
            RemoveZeroLinesFromAllPlots();
            ZeroLineButtonText = "Показать базовую линию";
            return;
        }

        AddZeroLinesToAllPlots();
        ZeroLineButtonText = "Скрыть базовую линию";
    }
    
    private void AddZeroLinesToAllPlots()
    {
        if (_experiment == null)
            return;

        if (_zeroLines.TryGetValue(CurrentVsPotential, out var existing) && CurrentVsPotential.Series.Contains(existing))
            return;

        var mainPoints = GetMainPointsOrderedByTime();
        if (mainPoints.Count < 2)
            return;

        var baseline = _sharedBaseline;
        if (baseline == null)
        {
            var (anodicBranch, cathodicBranch) = SplitIntoBranches(mainPoints);
            baseline = ComputeSharedBaseline(anodicBranch, cathodicBranch, GetBaselinePolynomialDegree());
        }

        var sorted = mainPoints.OrderBy(p => p.Potential).ToList();
        var xMin = sorted.First().Potential;
        var xMax = sorted.Last().Potential;
        var zeroLine = new LineSeries
        {
            Color = OxyColors.Gray,
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash,
            Title = "Базовая / нулевая линия",
            ItemsSource = Enumerable.Range(0, 300)
                .Select(i =>
                {
                    double x = xMin + (xMax - xMin) * i / 299.0;
                    return new DataPoint(x, EvaluateBaseline(baseline, x));
                })
                .ToList()
        };

        CurrentVsPotential.Series.Add(zeroLine);
        _zeroLines[CurrentVsPotential] = zeroLine;
        CurrentVsPotential.InvalidatePlot(true);
    }
    
    private void RemoveZeroLinesFromAllPlots()
    {
        if (_zeroLines.TryGetValue(CurrentVsPotential, out var zeroLine) && CurrentVsPotential.Series.Contains(zeroLine))
        {
            CurrentVsPotential.Series.Remove(zeroLine);
            CurrentVsPotential.InvalidatePlot(true);
        }

        _zeroLines.Clear();
    }
}