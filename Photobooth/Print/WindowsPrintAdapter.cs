using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using Serilog;

namespace Photobooth.Print;

public sealed class WindowsPrintAdapter : IPrintAdapter
{
    public IEnumerable<string> GetInstalledPrinterNames()
    {
        foreach (string name in PrinterSettings.InstalledPrinters)
            yield return name;
    }

    public IEnumerable<PaperSize> GetPaperSizes(string printerName)
    {
        var ps = new PrinterSettings { PrinterName = printerName };
        foreach (PaperSize size in ps.PaperSizes)
            yield return size;
    }

    public void SubmitJob(string? printerName, PaperSize? paperSize, Bitmap bitmap)
    {
        Log.Information("Printing strip on {Printer}", printerName ?? "(system default)");

        var doc = new PrintDocument();
        if (printerName != null)
            doc.PrinterSettings.PrinterName = printerName;

        Log.Debug("Resolved printer: {Printer} — IsValid={Valid}",
            doc.PrinterSettings.PrinterName,
            doc.PrinterSettings.IsValid);

        if (paperSize != null)
        {
            doc.DefaultPageSettings.PaperSize = paperSize;
            Log.Debug("Paper size set: {Name}", paperSize.PaperName);
        }

        doc.DefaultPageSettings.Landscape = false;

        var bmp = bitmap;
        doc.PrintPage += (_, e) =>
        {
            e.Graphics!.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode    = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(bmp, e.PageBounds);
            e.HasMorePages = false;
        };

        doc.Print();
        Log.Information("Print job submitted");
    }
}
