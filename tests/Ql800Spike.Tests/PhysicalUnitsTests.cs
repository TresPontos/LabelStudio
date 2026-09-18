using Ql800Spike.Core.Geometry;

namespace Ql800Spike.Tests;

public sealed class PhysicalUnitsTests
{
    [Theory]
    [InlineData(25_400, 300, 300)]
    [InlineData(62_000, 300, 732)]
    [InlineData(50_000, 300, 591)]
    [InlineData(10_000, 300, 118)]
    public void MicrometresToDotsUsesDeterministicPhysicalConversion(
        int micrometres,
        int dpi,
        int expected)
    {
        Assert.Equal(expected, PhysicalUnits.MicrometresToDots(micrometres, dpi));
    }

    [Fact]
    public void MillimetresToMicrometresUsesAwayFromZeroRounding()
    {
        Assert.Equal(1_235, PhysicalUnits.MillimetresToMicrometres(1.2345m));
    }
}
