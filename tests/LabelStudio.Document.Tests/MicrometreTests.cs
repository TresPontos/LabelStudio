using LabelStudio.Document.Units;

namespace LabelStudio.Document.Tests;

public class MicrometreTests
{
    [Fact]
    public void FromMillimetres_ConvertsCorrectly()
    {
        Assert.Equal(1000, Micrometre.FromMillimetres(1.0).Value);
        Assert.Equal(62000, Micrometre.FromMillimetres(62.0).Value);
        Assert.Equal(53900, Micrometre.FromMillimetres(53.9).Value);
    }

    [Fact]
    public void FromMillimetres_RoundsAwayFromZero()
    {
        Assert.Equal(1, Micrometre.FromMillimetres(0.0006).Value);
        Assert.Equal(-1, Micrometre.FromMillimetres(-0.0006).Value);
        Assert.Equal(0, Micrometre.FromMillimetres(0.0004).Value);
        Assert.Equal(0, Micrometre.FromMillimetres(0.0).Value);
    }

    [Fact]
    public void ToMillimetres_ConvertsCorrectly()
    {
        Assert.Equal(1.0, Micrometre.FromMillimetres(1.0).ToMillimetres());
        Assert.Equal(62.0, Micrometre.FromMillimetres(62.0).ToMillimetres());
    }

    [Fact]
    public void ArithmeticOperatorsWork()
    {
        Micrometre a = new(1000);
        Micrometre b = new(500);
        Assert.Equal(1500, (a + b).Value);
        Assert.Equal(500, (a - b).Value);
        Assert.Equal(-500, (-b).Value);
    }

    [Fact]
    public void ComparisonOperatorsWork()
    {
        Micrometre a = new(1000);
        Micrometre b = new(500);
        Assert.True(a > b);
        Assert.True(b < a);
        Assert.True(a >= b);
        Assert.True(b <= a);
    }

    [Fact]
    public void MinMax()
    {
        Micrometre a = new(1000);
        Micrometre b = new(500);
        Assert.Equal(b, Micrometre.Min(a, b));
        Assert.Equal(a, Micrometre.Max(a, b));
    }

    [Fact]
    public void Clamp()
    {
        Micrometre value = new(100);
        Micrometre min = new(200);
        Micrometre max = new(800);
        Assert.Equal(200, value.Clamp(min, max).Value);

        Micrometre high = new(900);
        Assert.Equal(800, high.Clamp(min, max).Value);

        Micrometre mid = new(500);
        Assert.Equal(500, mid.Clamp(min, max).Value);
    }
}