using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using Photobooth.Settings;
using Serilog;

namespace Photobooth.Print
{
    public sealed class PrintService
    {
        private readonly SettingsManager _settings;

        public PrintService(SettingsManager settings) => _settings = settings;

        // First installed printer whose name contains "DNP" or "DS620"; null if none.
        public string? FindDnpPrinter()
        {
            foreach (string name in PrinterSettings.InstalledPrinters)
            {
                var u = name.ToUpperInvariant();
                if (u.Contains("DNP") || u.Contains("DS620") || u.Contains("DS-620") || u.Contains("RX1"))
                    return name;
            }
            return null;
        }

        // Manual override → DNP auto-detect → null (system default).
        public string? EffectivePrinterName()
        {
            if (!string.IsNullOrWhiteSpace(_settings.Current.PrinterName))
                return _settings.Current.PrinterName;
            return FindDnpPrinter();
        }

        public Task PrintStripAsync(Bitmap strip)
        {
            return Task.Run(() =>
            {
                string? printerName = EffectivePrinterName();
                Log.Information("Printing strip on {Printer}", printerName ?? "(system default)");

                var doc = new PrintDocument();
                if (printerName != null)
                    doc.PrinterSettings.PrinterName = printerName;

                // Portrait 4×6 paper — matches the 1240×1844 px canvas.
                var paperSize = Find4x6PortraitSize(doc.PrinterSettings);
                if (paperSize != null)
                {
                    doc.DefaultPageSettings.PaperSize = paperSize;
                    Log.Debug("Paper size: {Name}", paperSize.PaperName);
                }
                else
                {
                    Log.Warning("No 4×6 paper size found — using driver default");
                }

                doc.DefaultPageSettings.Landscape = false;

                var bmp = strip; // capture for lambda
                doc.PrintPage += (_, e) =>
                {
                    e.Graphics!.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    e.Graphics.PixelOffsetMode    = PixelOffsetMode.HighQuality;
                    e.Graphics.DrawImage(bmp, e.PageBounds);
                    e.HasMorePages = false;
                };

                doc.Print();
                Log.Information("Print job submitted");
            });
        }

        private static PaperSize? Find4x6PortraitSize(PrinterSettings ps)
        {
            // DNP driver names the portrait 4×6 as "PR(4x6)" — try exact prefix first.
            foreach (PaperSize s in ps.PaperSizes)
            {
                var n = s.PaperName.ToUpperInvariant();
                if (n.StartsWith("PR(4") || n.StartsWith("PR (4"))
                    return s;
            }
            // Fallback: any name containing 4×6.
            foreach (PaperSize s in ps.PaperSizes)
            {
                var n = s.PaperName.ToUpperInvariant();
                if (n.Contains("4X6") || n.Contains("4 X 6"))
                    return s;
            }
            // Fallback: match physical dimensions (hundredths of an inch: 400×600).
            foreach (PaperSize s in ps.PaperSizes)
            {
                if (s.Width == 400 && s.Height == 600)
                    return s;
            }
            return null;
        }
    }
}
