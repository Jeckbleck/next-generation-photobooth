using System.IO;
using System.Text.Json;
using Photobooth.Services;
using Xunit;

namespace Photobooth.Tests.Unit;

public sealed class SettingsManagerTests : IDisposable
{
    private readonly string _tempFile;

    public SettingsManagerTests()
    {
        _tempFile = Path.GetTempFileName();
    }

    public void Dispose() => File.Delete(_tempFile);

    // --- Round-trip ---

    [Fact]
    public void DefaultsWrittenOnFirstLoad()
    {
        // Delete temp file so SettingsManager creates it fresh
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        Assert.True(File.Exists(_tempFile));
    }

    [Fact]
    public void SaveAndReload_PreservesValues()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        sm.Settings.BrandingText = "TEST BOOTH";
        sm.Save();

        var sm2 = new SettingsManager(_tempFile);
        Assert.Equal("TEST BOOTH", sm2.Settings.BrandingText);
    }

    [Fact]
    public void MultipleProperties_RoundTrip()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        sm.Settings.CountdownSeconds = 7;
        sm.Settings.BrandingText = "ROUND TRIP";
        sm.Save();

        var sm2 = new SettingsManager(_tempFile);
        Assert.Equal(7, sm2.Settings.CountdownSeconds);
        Assert.Equal("ROUND TRIP", sm2.Settings.BrandingText);
    }

    // --- Defaults ---

    [Fact]
    public void Load_ReturnsDefaults_WhenFileAbsent()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        Assert.NotNull(sm.Settings);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileCorrupt()
    {
        File.WriteAllText(_tempFile, "not json {{{{");
        var sm = new SettingsManager(_tempFile);
        Assert.NotNull(sm.Settings);
    }

    // --- File isolation ---

    [Fact]
    public void TwoInstances_DifferentPaths_DoNotInterfere()
    {
        var file1 = Path.GetTempFileName();
        var file2 = Path.GetTempFileName();
        try
        {
            File.Delete(file1);
            File.Delete(file2);
            var sm1 = new SettingsManager(file1);
            var sm2 = new SettingsManager(file2);

            sm1.Settings.BrandingText = "BOOTH ONE";
            sm2.Settings.BrandingText = "BOOTH TWO";
            sm1.Save();
            sm2.Save();

            Assert.Equal("BOOTH ONE", new SettingsManager(file1).Settings.BrandingText);
            Assert.Equal("BOOTH TWO", new SettingsManager(file2).Settings.BrandingText);
        }
        finally
        {
            File.Delete(file1);
            File.Delete(file2);
        }
    }

    // --- JSON format ---

    [Fact]
    public void Save_WritesValidJson()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        sm.Save();

        var text = File.ReadAllText(_tempFile);
        var doc = JsonDocument.Parse(text); // throws if invalid JSON
        Assert.NotNull(doc);
    }
}
