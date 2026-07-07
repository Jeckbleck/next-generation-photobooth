using System.Text.Json;
using Photobooth.Data.Models;
using Xunit;

namespace Photobooth.Tests.Unit;

public sealed class StripTemplateConfigTests
{
    [Fact]
    public void RoundTrip_PreservesBackgroundColorAndTextElements()
    {
        var config = new StripTemplateConfig
        {
            Slots = new()
            {
                new StripSlotDefinition { Index = 1, X = 0.1, Y = 0.1, Width = 0.3, Height = 0.3, Rotation = 90 },
            },
            BackgroundColor = "#112233",
            TextElements = new()
            {
                new TextElementDefinition
                {
                    Content  = "Hello Event",
                    X        = 0.0,
                    Y        = 0.8,
                    Width    = 1.0,
                    Height   = 0.1,
                    Color    = "#FFFFFF",
                    FontSize = 32,
                },
            },
        };

        var json      = JsonSerializer.Serialize(config);
        var roundTrip = JsonSerializer.Deserialize<StripTemplateConfig>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal("#112233", roundTrip!.BackgroundColor);
        Assert.Single(roundTrip.TextElements);
        Assert.Equal("Hello Event", roundTrip.TextElements[0].Content);
        Assert.Equal(32, roundTrip.TextElements[0].FontSize);
        Assert.Single(roundTrip.Slots);
        Assert.Equal(90, roundTrip.Slots[0].Rotation);
    }

    [Fact]
    public void Defaults_AreEmptyNotNull()
    {
        var config = new StripTemplateConfig();
        Assert.Null(config.BackgroundColor);
        Assert.Empty(config.TextElements);
        Assert.Empty(config.Slots);
    }
}
