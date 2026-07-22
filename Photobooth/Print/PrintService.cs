using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Print
{
    public sealed class PrintService
    {
        private readonly SettingsManager _settings;
        private readonly IPrintAdapter _adapter;

        public PrintService(SettingsManager settings, IPrintAdapter adapter)
        {
            _settings = settings;
            _adapter  = adapter;
        }

        // First installed printer whose name contains "DNP" or "DS620"; null if none.
        public string? FindDnpPrinter()
        {
            foreach (string name in _adapter.GetInstalledPrinterNames())
            {
                var u = name.ToUpperInvariant();
                if (u.Contains("DNP") || u.Contains("DS620") || u.Contains("DS-620") || u.Contains("DS 620") || u.Contains("RX1"))
                    return name;
            }
            return null;
        }

        // Manual override → DNP auto-detect → null (system default).
        public string? EffectivePrinterName()
        {
            if (!string.IsNullOrWhiteSpace(_settings.PrinterName))
                return _settings.PrinterName;
            return FindDnpPrinter();
        }

        // Opens the driver's Printing Preferences for whichever printer is currently
        // in use (manual override, else DNP auto-detect). Returns false if no printer
        // could be resolved, so the caller can show a status message instead.
        public bool OpenPrinterProperties()
        {
            string? printerName = EffectivePrinterName();
            if (printerName == null) return false;

            _adapter.OpenPrinterProperties(printerName);
            return true;
        }

        public Task PrintStripAsync(Bitmap strip)
        {
            // PrintDocument and some drivers (e.g. DNP) require an STA thread.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    PrintOnCurrentThread(strip);
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return tcs.Task;
        }

        private void PrintOnCurrentThread(Bitmap strip)
        {
            string? printerName = EffectivePrinterName();
            Log.Debug("Installed printers: {Printers}",
                string.Join(", ", _adapter.GetInstalledPrinterNames()));

            PaperSize? paperSize = null;
            if (printerName != null)
            {
                var sizes = _adapter.GetPaperSizes(printerName).ToList();
                paperSize = Find4x6PortraitSize(sizes);
                if (paperSize == null)
                {
                    Log.Warning("No 4×6 paper size found — using driver default. Available: {Sizes}",
                        string.Join(", ", sizes.Select(s => s.PaperName)));
                }
            }

            _adapter.SubmitJob(printerName, paperSize, strip);
        }

        internal static PaperSize? Find4x6PortraitSize(IEnumerable<PaperSize> sizes)
        {
            var list = sizes as IList<PaperSize> ?? new List<PaperSize>(sizes);

            // DNP driver names the portrait 4×6 as "PR(4x6)" — try exact prefix first.
            foreach (PaperSize s in list)
            {
                var n = s.PaperName.ToUpperInvariant();
                if (n.StartsWith("PR(4") || n.StartsWith("PR (4"))
                    return s;
            }
            // Fallback: any name containing 4×6.
            foreach (PaperSize s in list)
            {
                var n = s.PaperName.ToUpperInvariant();
                if (n.Contains("4X6") || n.Contains("4 X 6"))
                    return s;
            }
            // Fallback: match physical dimensions (hundredths of an inch: 400×600).
            foreach (PaperSize s in list)
            {
                if (s.Width == 400 && s.Height == 600)
                    return s;
            }
            return null;
        }
    }
}
