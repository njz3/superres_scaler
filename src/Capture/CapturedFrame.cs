// Copyright (c) Benjamin Maurin (njz3) 2026

using Vortice.Direct3D11;

namespace SuperResScaler.Capture;

/// <summary>A captured frame. Its texture is only valid until the frame is disposed.</summary>
internal sealed class CapturedFrame : IDisposable
{
    private readonly Action? _release;

    /// <param name="release">Gives the frame back to its source (Windows.Graphics.Capture or the duplication).</param>
    /// <param name="sourceFrames">Frames the source produced since the previous one taken, this one included.</param>
    public CapturedFrame(ID3D11Texture2D texture, int contentWidth, int contentHeight, TimeSpan composedAt, Action? release, int sourceFrames = 1)
    {
        Texture = texture;
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
        ComposedAt = composedAt;
        _release = release;
        SourceFrames = Math.Max(1, sourceFrames);
    }

    /// <summary>Frames produced since the previous frame taken, this one included: more than 1 means some were never shown.</summary>
    public int SourceFrames { get; }

    public ID3D11Texture2D Texture { get; }

    /// <summary>Size of the picture inside the (possibly larger) texture.</summary>
    public int ContentWidth { get; }
    public int ContentHeight { get; }

    /// <summary>When Windows composed the frame, in the QueryPerformanceCounter clock.</summary>
    public TimeSpan ComposedAt { get; }

    public void Dispose()
    {
        Texture.Dispose();
        _release?.Invoke();
    }
}

/// <summary>Windows.Graphics.Capture or the desktop duplication, as the render loop sees them.</summary>
internal interface IFrameSource : IDisposable
{
    /// <summary>The capture method, for the log and the messages.</summary>
    CaptureMethod Method { get; }

    /// <summary>True once the captured window has been destroyed, or the display removed.</summary>
    bool IsClosed { get; }

    void SetCursorCapture(bool enabled);

    /// <summary>
    /// Returns the most recent frame (older ones are dropped), or null when the picture has not
    /// changed since the last call. The caller disposes the frame before calling this method again.
    /// </summary>
    CapturedFrame? TryGetLatestFrame();

    /// <summary>
    /// Like <see cref="TryGetLatestFrame"/>, but waits up to <paramref name="timeoutMs"/> for a new
    /// frame: the render loop runs at the pace of the captured picture.
    /// </summary>
    CapturedFrame? WaitForFrame(int timeoutMs);
}
