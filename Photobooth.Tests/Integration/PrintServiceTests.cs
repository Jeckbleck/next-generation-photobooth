using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Threading.Tasks;
using Moq;
using Photobooth.Print;
using Photobooth.Services;
using Xunit;

namespace Photobooth.Tests.Integration;

public sealed class PrintServiceTests : IDisposable
{
    private readonly Mock<IPrintAdapter> _adapter = new();
    private readonly SettingsManager _settings;
    private readonly string _tempFile;

    public PrintServiceTests()
    {
        _tempFile = Path.GetTempFileName();
        File.Delete(_tempFile);
        _settings = new SettingsManager(_tempFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private PrintService CreateSut() => new PrintService(_settings, _adapter.Object);

    // ── FindDnpPrinter ──────────────────────────────────────────────────────────

    [Fact]
    public void FindDnpPrinter_ReturnsNull_WhenNoPrintersInstalled()
    {
        _adapter.Setup(a => a.GetInstalledPrinterNames()).Returns(Array.Empty<string>());
        var sut = CreateSut();
        Assert.Null(sut.FindDnpPrinter());
    }

    [Fact]
    public void FindDnpPrinter_ReturnsNull_WhenNoDnpPrinterPresent()
    {
        _adapter.Setup(a => a.GetInstalledPrinterNames())
                .Returns(new[] { "HP LaserJet", "Canon iP4200", "Microsoft Print to PDF" });
        var sut = CreateSut();
        Assert.Null(sut.FindDnpPrinter());
    }

    [Theory]
    [InlineData("DNP DS620")]
    [InlineData("DS620 Photo Printer")]
    [InlineData("DS-620 Color")]
    [InlineData("DS 620 DNP")]
    [InlineData("DNP RX1 Printer")]
    public void FindDnpPrinter_ReturnsName_WhenDnpPrinterPresent(string printerName)
    {
        _adapter.Setup(a => a.GetInstalledPrinterNames())
                .Returns(new[] { "HP LaserJet", printerName });
        var sut = CreateSut();
        Assert.Equal(printerName, sut.FindDnpPrinter());
    }

    // ── EffectivePrinterName ────────────────────────────────────────────────────

    [Fact]
    public void EffectivePrinterName_ReturnsSettingsOverride_WhenSet()
    {
        _settings.SetPrinterName("My Custom Printer");
        // Adapter should not even be called because settings override wins.
        _adapter.Setup(a => a.GetInstalledPrinterNames()).Returns(Array.Empty<string>());
        var sut = CreateSut();
        Assert.Equal("My Custom Printer", sut.EffectivePrinterName());
        _adapter.Verify(a => a.GetInstalledPrinterNames(), Times.Never);
    }

    [Fact]
    public void EffectivePrinterName_FallsBackToDnpAutoDetect_WhenNoSettingsOverride()
    {
        _settings.SetPrinterName(null);
        _adapter.Setup(a => a.GetInstalledPrinterNames())
                .Returns(new[] { "DNP DS620A" });
        var sut = CreateSut();
        Assert.Equal("DNP DS620A", sut.EffectivePrinterName());
    }

    [Fact]
    public void EffectivePrinterName_ReturnsNull_WhenNoDnpAndNoSettingsOverride()
    {
        _settings.SetPrinterName(null);
        _adapter.Setup(a => a.GetInstalledPrinterNames())
                .Returns(new[] { "HP LaserJet" });
        var sut = CreateSut();
        Assert.Null(sut.EffectivePrinterName());
    }

    // ── Find4x6PortraitSize ─────────────────────────────────────────────────────

    [Fact]
    public void Find4x6PortraitSize_MatchesDnpPrPrefix()
    {
        var sizes = new[]
        {
            new PaperSize("Letter", 850, 1100),
            new PaperSize("PR(4x6)", 400, 600),
        };
        var result = PrintService.Find4x6PortraitSize(sizes);
        Assert.NotNull(result);
        Assert.Equal("PR(4x6)", result!.PaperName);
    }

    [Fact]
    public void Find4x6PortraitSize_FallsBackToNameContaining4x6()
    {
        var sizes = new[]
        {
            new PaperSize("Letter", 850, 1100),
            new PaperSize("4x6 Photo", 400, 600),
        };
        var result = PrintService.Find4x6PortraitSize(sizes);
        Assert.NotNull(result);
        Assert.Equal("4x6 Photo", result!.PaperName);
    }

    [Fact]
    public void Find4x6PortraitSize_FallsBackToDimensions()
    {
        var sizes = new[]
        {
            new PaperSize("Letter", 850, 1100),
            new PaperSize("Custom100x150mm", 400, 600),
        };
        var result = PrintService.Find4x6PortraitSize(sizes);
        Assert.NotNull(result);
        Assert.Equal("Custom100x150mm", result!.PaperName);
    }

    [Fact]
    public void Find4x6PortraitSize_ReturnsNull_WhenNoMatch()
    {
        var sizes = new[]
        {
            new PaperSize("Letter", 850, 1100),
            new PaperSize("A4", 827, 1169),
        };
        var result = PrintService.Find4x6PortraitSize(sizes);
        Assert.Null(result);
    }

    // ── PrintStripAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PrintStripAsync_CallsSubmitJob_WithCorrectPrinterAndPaperSize()
    {
        var printerName = "DNP DS620A";
        var paperSize   = new PaperSize("PR(4x6)", 400, 600);

        _adapter.Setup(a => a.GetInstalledPrinterNames())
                .Returns(new[] { printerName });
        _adapter.Setup(a => a.GetPaperSizes(printerName))
                .Returns(new[] { new PaperSize("Letter", 850, 1100), paperSize });

        var sut = CreateSut();

        using var bmp = new Bitmap(100, 150);
        await sut.PrintStripAsync(bmp);

        _adapter.Verify(a => a.SubmitJob(printerName, It.Is<PaperSize>(p => p.PaperName == "PR(4x6)"), bmp), Times.Once);
    }

    [Fact]
    public async Task PrintStripAsync_SubmitsWithNullPaperSize_WhenNoPrinterResolved()
    {
        // No printers at all → printerName is null → no paper size lookup, null paperSize passed.
        _adapter.Setup(a => a.GetInstalledPrinterNames()).Returns(Array.Empty<string>());

        var sut = CreateSut();

        using var bmp = new Bitmap(100, 150);
        await sut.PrintStripAsync(bmp);

        _adapter.Verify(a => a.SubmitJob(null, null, bmp), Times.Once);
    }

    // ── OpenPrinterProperties ───────────────────────────────────────────────────

    [Fact]
    public void OpenPrinterProperties_ReturnsTrueAndCallsAdapter_WhenPrinterResolved()
    {
        _settings.SetPrinterName("DNP DS620A");
        var sut = CreateSut();

        bool result = sut.OpenPrinterProperties();

        Assert.True(result);
        _adapter.Verify(a => a.OpenPrinterProperties("DNP DS620A"), Times.Once);
    }

    [Fact]
    public void OpenPrinterProperties_UsesDnpAutoDetect_WhenNoSettingsOverride()
    {
        _settings.SetPrinterName(null);
        _adapter.Setup(a => a.GetInstalledPrinterNames())
                .Returns(new[] { "DNP DS620A" });
        var sut = CreateSut();

        bool result = sut.OpenPrinterProperties();

        Assert.True(result);
        _adapter.Verify(a => a.OpenPrinterProperties("DNP DS620A"), Times.Once);
    }

    [Fact]
    public void OpenPrinterProperties_ReturnsFalse_WhenNoPrinterResolved()
    {
        _settings.SetPrinterName(null);
        _adapter.Setup(a => a.GetInstalledPrinterNames()).Returns(Array.Empty<string>());
        var sut = CreateSut();

        bool result = sut.OpenPrinterProperties();

        Assert.False(result);
        _adapter.Verify(a => a.OpenPrinterProperties(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PrintStripAsync_PropagatesException_WhenSubmitJobThrows()
    {
        var printerName = "DNP DS620A";
        _adapter.Setup(a => a.GetInstalledPrinterNames()).Returns(new[] { printerName });
        _adapter.Setup(a => a.GetPaperSizes(printerName)).Returns(Array.Empty<PaperSize>());
        _adapter.Setup(a => a.SubmitJob(It.IsAny<string?>(), It.IsAny<PaperSize?>(), It.IsAny<Bitmap>()))
                .Throws(new InvalidOperationException("Printer offline"));

        var sut = CreateSut();
        using var bmp = new Bitmap(100, 150);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.PrintStripAsync(bmp));
        Assert.Equal("Printer offline", ex.Message);
    }
}
