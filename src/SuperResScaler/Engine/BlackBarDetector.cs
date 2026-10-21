namespace SuperResScaler.Engine;

/// <summary>
/// Finds the non-black part of the image (e.g. a 4:3 game pillarboxed in a wide window).
/// Detections are accumulated as a union so that a dark scene never zooms the picture in:
/// the crop can only grow until <see cref="Reset"/> is called.
/// </summary>
internal sealed class BlackBarDetector
{
    /// <summary>A channel at or below this value counts as black (allows for dithering/noise).</summary>
    private const int BlackThreshold = 24;

    /// <summary>Content smaller than this fraction of the image is ignored (e.g. an almost black screen).</summary>
    private const double MinContentFraction = 0.25;

    /// <summary>Percentage of lit pixels for a row/column to count as content.</summary>
    private const int MinLitPercent = 4;

    private Size _imageSize;
    private Rectangle? _content;

    /// <summary>Accumulated content rectangle, relative to the analysed image.</summary>
    public Rectangle? Content => _content;

    public void Reset()
    {
        _content = null;
        _imageSize = Size.Empty;
    }

    /// <summary>Analyses tightly packed BGRA pixels and merges the result into <see cref="Content"/>.</summary>
    public void Analyze(byte[] bgra, int width, int height)
    {
        if (_imageSize != new Size(width, height))
        {
            _content = null;
            _imageSize = new Size(width, height);
        }

        var rowCounts = new int[height];
        var columnCounts = new int[width];
        for (int y = 0; y < height; y++)
        {
            int offset = y * width * 4;
            for (int x = 0; x < width; x++, offset += 4)
            {
                if (bgra[offset] > BlackThreshold || bgra[offset + 1] > BlackThreshold || bgra[offset + 2] > BlackThreshold)
                {
                    rowCounts[y]++;
                    columnCounts[x]++;
                }
            }
        }

        // A line belongs to the content when at least 4% of it is lit. This ignores a cursor,
        // an OSD dot and the rounded window corners Windows 11 draws inside the client area.
        int minPerRow = Math.Max(2, width * MinLitPercent / 100);
        int minPerColumn = Math.Max(2, height * MinLitPercent / 100);
        int top = Array.FindIndex(rowCounts, c => c >= minPerRow);
        int bottom = Array.FindLastIndex(rowCounts, c => c >= minPerRow);
        int left = Array.FindIndex(columnCounts, c => c >= minPerColumn);
        int right = Array.FindLastIndex(columnCounts, c => c >= minPerColumn);
        if (top < 0 || left < 0)
            return;

        var detected = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        if (detected.Width < width * MinContentFraction || detected.Height < height * MinContentFraction)
            return;

        _content = _content is Rectangle current ? Rectangle.Union(current, detected) : detected;
    }

    /// <summary>Restricts <paramref name="region"/> to the detected content (offsets are relative to it).</summary>
    public Rectangle Apply(Rectangle region)
    {
        if (_content is not Rectangle content || _imageSize != region.Size)
            return region;
        return new Rectangle(region.X + content.X, region.Y + content.Y, content.Width, content.Height);
    }
}
