using CvaAnalyzer.Models;
using CvaAnalyzer.ViewModels;
using System.Windows;

namespace CvaAnalyzer.Views;

public partial class BackgroundMetadataWindow
{
    public BackgroundMetadataViewModel MetadataViewModel { get; } = new();

    public BackgroundMetadataWindow()
    {
        InitializeComponent();
        DataContext = MetadataViewModel;
    }
    private void ScanRateBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Разрешаем только цифры и одну десятичную точку
        e.Handled = !IsTextAllowed(e.Text);
    }

    private void ScanRateBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        // Запрещаем вставку нечислового текста
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            string text = (string)e.DataObject.GetData(typeof(string));
            if (!IsTextAllowed(text))
            {
                e.CancelCommand();
                return;
            }
        }
        e.Handled = true;
    }


    private bool IsTextAllowed(string text)
    {
        // Допускаем цифры и максимум одну точку
        if (string.IsNullOrEmpty(text)) return false;

        string currentText = ScanRateBox.Text;
        string newText = currentText.Insert(ScanRateBox.SelectionStart, text);

        return System.Text.RegularExpressions.Regex.IsMatch(newText, @"^\d*\.?\d*$");
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ScanRateBox.Text))
        {
            MessageBox.Show("Поле «Скорость развертки» не может быть пустым.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(ScanRateBox.Text, out double scanRate) || scanRate <= 0)
        {
            MessageBox.Show("Скорость развертки должна быть положительным числом.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Явно установим значение в ViewModel
        MetadataViewModel.ScanRate = scanRate;
        MetadataViewModel.Electrolyte = ElectrolyteBox.Text?.Trim() ?? string.Empty;
        MetadataViewModel.WorkingElectrode = WorkingElectrodeBox.Text?.Trim() ?? string.Empty;
        MetadataViewModel.ReferenceElectrode = ReferenceElectrodeBox.Text?.Trim() ?? string.Empty;
        MetadataViewModel.Atmosphere = AtmosphereBox.Text?.Trim() ?? string.Empty;
        MetadataViewModel.CellType = CellTypeBox.Text?.Trim() ?? string.Empty;
        MetadataViewModel.DepositionMethod = DepositionMethodBox.Text?.Trim() ?? string.Empty;
        MetadataViewModel.Illumination = IlluminationBox.Text?.Trim() ?? string.Empty;

        DialogResult = true;
        Close();
    }

    public void SetInitialData(BackgroundData? data)
    {
        if (data == null) return;
        MetadataViewModel.SetFromBackground(data);
        // Явно подставляем значения в поля (на случай, если привязка ещё не обновилась)
        SampleNameBox.Text = MetadataViewModel.SampleName ?? string.Empty;
        ScanRateBox.Text = MetadataViewModel.ScanRate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ElectrolyteBox.Text = MetadataViewModel.Electrolyte ?? string.Empty;
        WorkingElectrodeBox.Text = MetadataViewModel.WorkingElectrode ?? string.Empty;
        ReferenceElectrodeBox.Text = MetadataViewModel.ReferenceElectrode ?? string.Empty;
        AtmosphereBox.Text = MetadataViewModel.Atmosphere ?? string.Empty;
        CellTypeBox.Text = MetadataViewModel.CellType ?? string.Empty;
        DepositionMethodBox.Text = MetadataViewModel.DepositionMethod ?? string.Empty;
        IlluminationBox.Text = MetadataViewModel.Illumination ?? string.Empty;
    }

    public string GetSampleName()
    {
        return MetadataViewModel.SampleName?.Trim() ?? string.Empty;
    }

    public BackgroundMetadata GetMetadataFromInputs()
    {
        return new BackgroundMetadata
        {
            ScanRate = MetadataViewModel.ScanRate,
            Electrolyte = ElectrolyteBox.Text?.Trim() ?? string.Empty,
            WorkingElectrode = WorkingElectrodeBox.Text?.Trim() ?? string.Empty,
            ReferenceElectrode = ReferenceElectrodeBox.Text?.Trim() ?? string.Empty,
            Atmosphere = AtmosphereBox.Text?.Trim() ?? string.Empty,
            CellType = CellTypeBox.Text?.Trim() ?? string.Empty,
            DepositionMethod = DepositionMethodBox.Text?.Trim() ?? string.Empty,
            Illumination = IlluminationBox.Text?.Trim() ?? string.Empty
        };
    }
}