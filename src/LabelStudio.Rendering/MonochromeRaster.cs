namespace LabelStudio.Rendering;

public sealed class MonochromeRaster
{
    private readonly byte[] _data;

    public int Width { get; }
    public int Height { get; }
    public int BytesPerRow { get; }
    public ReadOnlyMemory<byte> Data => _data;

    public MonochromeRaster(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

        Width = width;
        Height = height;
        BytesPerRow = (width + 7) / 8;
        _data = new byte[BytesPerRow * height];
    }

    public bool GetPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        return (_data[y * BytesPerRow + x / 8] & (0x80 >> (x % 8))) != 0;
    }

    public void SetPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        _data[y * BytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
    }

    public void ClearPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        _data[y * BytesPerRow + x / 8] &= (byte)~(0x80 >> (x % 8));
    }

    public void Clear()
    {
        Array.Clear(_data, 0, _data.Length);
    }

    public void ClearOutsideHorizontalRange(int left, int width)
    {
        int first = Math.Clamp(left, 0, Width);
        int last = Math.Clamp(left + width, first, Width);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < first; x++)
            {
                ClearPixel(x, y);
            }

            for (int x = last; x < Width; x++)
            {
                ClearPixel(x, y);
            }
        }
    }

    public ReadOnlySpan<byte> GetRow(int y)
    {
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
        return _data.AsSpan(y * BytesPerRow, BytesPerRow);
    }

    public void DrawHorizontalLine(int x, int y, int length, int thickness)
    {
        for (int t = 0; t < thickness; t++)
        {
            int py = y + t;
            if (py < 0 || py >= Height) continue;
            for (int i = 0; i < length; i++)
            {
                int px = x + i;
                if (px >= 0 && px < Width) SetPixel(px, py);
            }
        }
    }

    public void DrawVerticalLine(int x, int y, int length, int thickness)
    {
        for (int t = 0; t < thickness; t++)
        {
            int px = x + t;
            if (px < 0 || px >= Width) continue;
            for (int i = 0; i < length; i++)
            {
                int py = y + i;
                if (py >= 0 && py < Height) SetPixel(px, py);
            }
        }
    }

    public void DrawRectangle(int x, int y, int width, int height, int thickness = 1)
    {
        DrawHorizontalLine(x, y, width, thickness);
        DrawHorizontalLine(x, y + height - thickness, width, thickness);
        DrawVerticalLine(x, y, height, thickness);
        DrawVerticalLine(x + width - thickness, y, height, thickness);
    }

    public void FillRectangle(int x, int y, int width, int height)
    {
        for (int dy = 0; dy < height; dy++)
        {
            int py = y + dy;
            if (py < 0 || py >= Height) continue;
            for (int dx = 0; dx < width; dx++)
            {
                int px = x + dx;
                if (px >= 0 && px < Width) SetPixel(px, py);
            }
        }
    }

    public byte[] ToPbmBytes()
    {
        using MemoryStream ms = new();
        using StreamWriter writer = new(ms);
        writer.Write($"P4\n{Width} {Height}\n");
        writer.Flush();
        ms.Write(_data, 0, _data.Length);
        return ms.ToArray();
    }

    private void ValidateCoordinates(int x, int y)
    {
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
    }
}
