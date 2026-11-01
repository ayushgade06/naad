using Naad.Tray;

namespace Naad.Tests.Tray;

public sealed class VoiceBarsTests
{
    [Fact]
    public void Height_New_IsSilent()
    {
        var bars = new VoiceBars(5);

        Assert.All(Enumerable.Range(0, 5), i => Assert.Equal(0, bars.Height(i)));
    }

    [Fact]
    public void Push_NewestLevel_DrivesOnlyTheCentreBar()
    {
        var bars = new VoiceBars(5);

        bars.Push(1);

        Assert.Equal([0, 0, 1, 0, 0], Enumerable.Range(0, 5).Select(bars.Height));
    }

    [Fact]
    public void Push_OlderLevels_RippleOutwardSymmetrically()
    {
        var bars = new VoiceBars(5);

        bars.Push(1);
        bars.Push(0);

        Assert.Equal(0, bars.Height(2));
        Assert.True(bars.Height(1) > 0);
        Assert.Equal(bars.Height(1), bars.Height(3));
        Assert.Equal(0, bars.Height(0));
    }

    [Fact]
    public void Height_EdgesAreShorterThanTheCentreAtTheSameLevel()
    {
        var bars = new VoiceBars(5);

        for (var i = 0; i < 3; i++)
        {
            bars.Push(1);
        }

        Assert.True(bars.Height(0) < bars.Height(1));
        Assert.True(bars.Height(1) < bars.Height(2));
        Assert.True(bars.Height(0) > 0);
    }

    [Fact]
    public void Push_ClampsToZeroToOne()
    {
        var bars = new VoiceBars(1);

        bars.Push(5);
        Assert.Equal(1, bars.Height(0));

        bars.Push(-1);
        Assert.Equal(0, bars.Height(0));
    }

    [Fact]
    public void Reset_ReturnsToSilence()
    {
        var bars = new VoiceBars(3);
        bars.Push(1);
        bars.Push(1);

        bars.Reset();

        Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(0, bars.Height(i)));
    }

    [Fact]
    public void Constructor_EvenCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => new VoiceBars(4));
    }

    [Fact]
    public void Height_SilenceMidBreath_SwellsATenth()
    {
        var bars = new VoiceBars(5);

        Assert.Equal(0, bars.Breathing(2, seconds: 0), precision: 6);
        Assert.Equal(0.1, bars.Breathing(2, seconds: 0.8), precision: 6);
    }

    [Fact]
    public void Height_FullLevel_DoesNotBreathe()
    {
        var bars = new VoiceBars(5);
        bars.Push(1);

        Assert.Equal(1, bars.Breathing(2, seconds: 0.8), precision: 6);
    }

    [Fact]
    public void Height_Breath_RipplesOutward()
    {
        var bars = new VoiceBars(5);

        // The bar next to the centre reaches its peak a tenth of a second later.
        Assert.Equal(0.1, bars.Breathing(1, seconds: 0.9), precision: 6);
    }
}
