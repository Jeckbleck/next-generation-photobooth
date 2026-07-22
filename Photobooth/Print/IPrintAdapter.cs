using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;

namespace Photobooth.Print;

public interface IPrintAdapter
{
    IEnumerable<string> GetInstalledPrinterNames();
    IEnumerable<PaperSize> GetPaperSizes(string printerName);
    void SubmitJob(string? printerName, PaperSize? paperSize, Bitmap bitmap);
    void OpenPrinterProperties(string printerName);
}
