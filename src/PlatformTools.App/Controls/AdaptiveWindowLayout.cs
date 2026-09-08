using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PlatformTools.App.Services;

namespace PlatformTools.App.Controls;

internal sealed class AdaptiveWindowLayout : IDisposable
{
    private readonly Window _window;
    private readonly ScaleTransform _scale = new(1, 1);
    private PixelRect? _workingArea;
    private double _screenScaling;

    public AdaptiveWindowLayout(Window window, LayoutTransformControl content)
    {
        _window = window;
        content.LayoutTransform = _scale;
        FitToScreen(initial: true);
        window.Opened += OnScreenChanged;
        window.ScalingChanged += OnScreenChanged;
        window.PositionChanged += OnPositionChanged;
        window.Screens.Changed += OnScreenChanged;
        window.SizeChanged += OnSizeChanged;
        window.Closed += OnClosed;
        UpdateScale();
    }

    private void OnScreenChanged(object? sender, EventArgs e) => FitToScreen();
    private void OnPositionChanged(object? sender, PixelPointEventArgs e) => FitToScreen();
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateScale();
    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void FitToScreen(bool initial = false)
    {
        var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
        if (screen is null) return;
        if (!initial && _workingArea == screen.WorkingArea && _screenScaling == screen.Scaling) return;
        _workingArea = screen.WorkingArea;
        _screenScaling = screen.Scaling;
        var layout = WindowLayoutPolicy.ForScreen(screen.WorkingArea.Width, screen.WorkingArea.Height, screen.Scaling);
        _window.MinWidth = layout.MinWidth;
        _window.MinHeight = layout.MinHeight;
        if (_window.WindowState == WindowState.Normal)
        {
            _window.Width = initial ? layout.Width : Math.Min(_window.Width, layout.MaxWidth);
            _window.Height = initial ? layout.Height : Math.Min(_window.Height, layout.MaxHeight);
        }
        UpdateScale();
    }

    private void UpdateScale()
    {
        var size = _window.ClientSize;
        var scale = WindowLayoutPolicy.ContentScale(size.Width, size.Height);
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    public void Dispose()
    {
        _window.Opened -= OnScreenChanged;
        _window.ScalingChanged -= OnScreenChanged;
        _window.PositionChanged -= OnPositionChanged;
        _window.Screens.Changed -= OnScreenChanged;
        _window.SizeChanged -= OnSizeChanged;
        _window.Closed -= OnClosed;
    }
}
