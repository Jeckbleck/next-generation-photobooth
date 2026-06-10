using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views;

public partial class PrinterPanel : UserControl
{
    private readonly SettingsManager _settings;

    public PrinterPanel(SettingsManager settings)
    {
        _settings = settings;
        InitializeComponent();
    }

    public void Activate()
    {
        AutoPrintToggle.IsChecked = _settings.AutoPrint;
        PopulatePrinterDropdown();
    }

    private void PopulatePrinterDropdown()
    {
        PrinterDropdown.IsEnabled        = false;
        PrinterStatusText.Text           = "Loading printers…";
        PrinterDropdown.SelectionChanged -= PrinterDropdown_SelectionChanged;
        PrinterDropdown.Items.Clear();
        PrinterDropdown.Items.Add("(none)");

        string? saved = _settings.PrinterName;

        _ = Task.Run(() =>
        {
            var printers = new List<string>();
            foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                printers.Add(name);
            return printers;
        }).ContinueWith(t =>
        {
            int selectIndex = 0;
            foreach (var name in t.Result)
            {
                PrinterDropdown.Items.Add(name);
                if (name == saved)
                    selectIndex = PrinterDropdown.Items.Count - 1;
            }

            PrinterDropdown.SelectedIndex = selectIndex;
            PrinterStatusText.Text = selectIndex == 0
                ? "No printer selected."
                : $"Active: {PrinterDropdown.SelectedItem}";

            PrinterDropdown.IsEnabled = true;
            PrinterDropdown.SelectionChanged += PrinterDropdown_SelectionChanged;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void AutoPrintToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.SetAutoPrint(AutoPrintToggle.IsChecked == true);
    }

    private void PrinterDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PrinterDropdown.SelectedIndex <= 0)
        {
            _settings.SetPrinterName(null);
            PrinterStatusText.Text = "No printer selected.";
            Log.Information("Printer selection cleared");
        }
        else
        {
            string name = (string)PrinterDropdown.SelectedItem;
            _settings.SetPrinterName(name);
            PrinterStatusText.Text = $"Active: {name}";
            Log.Information("Printer selected: {Name}", name);
        }
    }
}
