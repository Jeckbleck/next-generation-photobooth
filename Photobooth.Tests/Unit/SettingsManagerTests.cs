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

    // --- Camera presets ---

    [Fact]
    public void DefaultCameraPresets_SeededOnFirstLoad()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        Assert.Equal(3, sm.CameraPresets.Count);

        var outdoor = sm.CameraPresets.Single(p => p.Name == "Outdoor Daylight");
        Assert.Equal((uint)0x00000048, outdoor.Iso);
        Assert.Equal((uint)0x00000070, outdoor.Tv);
        Assert.Equal((uint)0x00000030, outdoor.Av);

        var indoor = sm.CameraPresets.Single(p => p.Name == "Indoor");
        Assert.Equal((uint)0x00000058, indoor.Iso);
        Assert.Equal((uint)0x00000068, indoor.Tv);
        Assert.Equal((uint)0x00000028, indoor.Av);

        var lowLight = sm.CameraPresets.Single(p => p.Name == "Low Light");
        Assert.Equal((uint)0x00000060, lowLight.Iso);
        Assert.Equal((uint)0x00000068, lowLight.Tv);
        Assert.Equal((uint)0x00000020, lowLight.Av);
    }

    [Fact]
    public void SaveCameraPreset_AddsNewPreset()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        sm.SaveCameraPreset("Custom", 0x60, 0x60, 0x20);

        Assert.Contains(sm.CameraPresets,
            p => p.Name == "Custom" && p.Iso == 0x60 && p.Tv == 0x60 && p.Av == 0x20);
    }

    [Fact]
    public void SaveCameraPreset_OverwritesExistingByName_CaseInsensitive()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        sm.SaveCameraPreset("outdoor daylight", 0x11, 0x22, 0x33);

        var matches = sm.CameraPresets
            .Where(p => string.Equals(p.Name, "Outdoor Daylight", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(matches);
        Assert.Equal((uint)0x11, matches[0].Iso);
    }

    [Fact]
    public void DeleteCameraPreset_RemovesByName()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        sm.DeleteCameraPreset("Indoor");

        Assert.DoesNotContain(sm.CameraPresets, p => p.Name == "Indoor");
    }

    [Fact]
    public void DeleteCameraPreset_NonexistentName_IsNoOp()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        int before = sm.CameraPresets.Count;
        sm.DeleteCameraPreset("Nonexistent");

        Assert.Equal(before, sm.CameraPresets.Count);
    }

    [Fact]
    public void CameraPresets_PersistAcrossReload()
    {
        File.Delete(_tempFile);
        var sm = new SettingsManager(_tempFile);
        sm.SaveCameraPreset("Custom", 0x60, 0x60, 0x20);

        var sm2 = new SettingsManager(_tempFile);
        Assert.Contains(sm2.CameraPresets, p => p.Name == "Custom");
    }
}
