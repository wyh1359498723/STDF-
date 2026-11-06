using System.Windows;

namespace StdfAnalyzer;

public enum TmbBinChoiceResult
{
    Cancelled,
    HardBin,
    SoftBin,
}

public partial class TmbBinChoiceDialog : Window
{
    public TmbBinChoiceResult Choice { get; private set; } = TmbBinChoiceResult.Cancelled;

    public TmbBinChoiceDialog()
    {
        InitializeComponent();
    }

    private void HardBin_Click(object sender, RoutedEventArgs e)
    {
        Choice = TmbBinChoiceResult.HardBin;
        DialogResult = true;
        Close();
    }

    private void SoftBin_Click(object sender, RoutedEventArgs e)
    {
        Choice = TmbBinChoiceResult.SoftBin;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = TmbBinChoiceResult.Cancelled;
        DialogResult = false;
        Close();
    }
}
