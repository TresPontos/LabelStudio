using LabelStudio.Document.Units;

namespace LabelStudio.Document.Tests;

public class PhysicalUnitsTests
{
    [Fact]
    public void MicrometresToDots_At300Dpi()
    {
        Assert.Equal(0, PhysicalUnits.MicrometresToDots(0, 300));
        Assert.Equal(354, PhysicalUnits.MicrometresToDots(30_000, 300));
        Assert.Equal(709, PhysicalUnits.MicrometresToDots(60_000, 300));
        Assert.Equal(1181, PhysicalUnits.MicrometresToDots(100_000, 300));
    }

    [Fact]
    public void MicrometresToDots_RoundsAwayFromZero()
    {
        Assert.Equal(2, PhysicalUnits.MicrometresToDots(127, 300));
        Assert.Equal(1, PhysicalUnits.MicrometresToDots(126, 300));
    }

    [Fact]
    public void DotsToMicrometres_At300Dpi()
    {
        Assert.Equal(0, PhysicalUnits.DotsToMicrometres(0, 300));
        Assert.Equal(25400, PhysicalUnits.DotsToMicrometres(300, 300));
    }

    [Fact]
    public void DotsToMicrometres_ApproximatelyRoundTrips()
    {
        int original = 60_000;
        int dots = PhysicalUnits.MicrometresToDots(original, 300);
        int back = PhysicalUnits.DotsToMicrometres(dots, 300);
        Assert.True(Math.Abs(original - back) <= 50);
    }

    [Fact]
    public void MillimetresToMicrometres_RoundsCorrectly()
    {
        Assert.Equal(1000, PhysicalUnits.MillimetresToMicrometres(1.0));
        Assert.Equal(62000, PhysicalUnits.MillimetresToMicrometres(62.0));
    }

    [Fact]
    public void MillimetresToDots_At300Dpi()
    {
        Assert.Equal(354, PhysicalUnits.MillimetresToDots(30.0, 300));
        Assert.Equal(709, PhysicalUnits.MillimetresToDots(60.0, 300));
    }

    [Fact]
    public void MicrometresToMillimetres_ConvertsCorrectly()
    {
        Assert.Equal(1.0, PhysicalUnits.MicrometresToMillimetres(1000));
        Assert.Equal(62.0, PhysicalUnits.MicrometresToMillimetres(62000));
    }
}