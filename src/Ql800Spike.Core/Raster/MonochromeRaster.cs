namespace Ql800Spike.Core.Raster;

public sealed class MonochromeRaster
{
    private readonly byte[] data;

    public MonochromeRaster(int width, int height)
    {
        if (width <= 0 || width % 8 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive and byte-aligned.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        BytesPerRow = width / 8;
        data = new byte[checked(BytesPerRow * height)];
    }

    public int Width { get; }

    public int Height { get; }

    public int BytesPerRow { get; }

    public ReadOnlyMemory<byte> Data => data;

    public bool GetPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        int index = checked((y * BytesPerRow) + (x / 8));
        byte mask = (byte)(0x80 >> (x % 8));
        return (data[index] & mask) != 0;
    }

    public void SetPixel(int x, int y, bool black = true)
    {
        ValidateCoordinates(x, y);
        int index = checked((y * BytesPerRow) + (x / 8));
        byte mask = (byte)(0x80 >> (x % 8));

        if (black)
        {
            data[index] |= mask;
        }
        else
        {
            data[index] &= (byte)~mask;
        }
    }

    public ReadOnlySpan<byte> GetRow(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return data.AsSpan(y * BytesPerRow, BytesPerRow);
    }

    public void DrawHorizontalLine(int x, int y, int length, int thickness = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thickness);

        for (int dy = 0; dy < thickness; dy++)
        {
            for (int dx = 0; dx < length; dx++)
            {
                SetPixel(x + dx, y + dy);
            }
        }
    }

    public void DrawVerticalLine(int x, int y, int length, int thickness = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thickness);

        for (int dx = 0; dx < thickness; dx++)
        {
            for (int dy = 0; dy < length; dy++)
            {
                SetPixel(x + dx, y + dy);
            }
        }
    }

    public void DrawRectangle(int x, int y, int width, int height, int thickness = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thickness);

        DrawHorizontalLine(x, y, width, thickness);
        DrawHorizontalLine(x, y + height - thickness, width, thickness);
        DrawVerticalLine(x, y, height, thickness);
        DrawVerticalLine(x + width - thickness, y, height, thickness);
    }

    public void FillRectangle(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        for (int dy = 0; dy < height; dy++)
        {
            DrawHorizontalLine(x, y + dy, width);
        }
    }

    private void ValidateCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }
}
