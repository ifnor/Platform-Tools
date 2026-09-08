using System;

namespace PlatformTools.App.Services;

internal readonly record struct WindowLayout(double Width, double Height, double MaxWidth, double MaxHeight, double MinWidth, double MinHeight);

internal static class WindowLayoutPolicy
{
    public static WindowLayout ForScreen(double pixelWidth, double pixelHeight, double scaling)
    {
        if (!double.IsFinite(scaling) || scaling <= 0) scaling = 1;
        var workWidth = Math.Max(1, pixelWidth / scaling);
        var workHeight = Math.Max(1, pixelHeight / scaling);
        // Leave room for native window decorations and a margin around the window.
        var maxWidth = Math.Max(1, workWidth - 32);
        var maxHeight = Math.Max(1, workHeight - 64);
        var width = Math.Min(1040, Math.Min(maxWidth, workWidth * 0.88));
        var height = Math.Min(680, Math.Min(maxHeight, workHeight * 0.88 - 32));
        height = Math.Max(1, height);
        return new(width, height, maxWidth, maxHeight, Math.Min(720, width), Math.Min(440, height));
    }

    public static double ContentScale(double width, double height)
    {
        if (width <= 0 || height <= 0) return 1;
        // Avoid enlarging text on large monitors or making it unreadable on small ones.
        // Existing page scroll viewers handle vertical overflow at the lower bound.
        return Math.Clamp(Math.Min(width / 1180, height / 760), 0.70, 1);
    }
}
