using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Automation;

namespace PlatformTools.App.Controls;

public sealed class ActionCard : Button
{
    public static readonly StyledProperty<string> HeadingProperty = AvaloniaProperty.Register<ActionCard, string>(nameof(Heading), "");
    public static readonly StyledProperty<string> DescriptionProperty = AvaloniaProperty.Register<ActionCard, string>(nameof(Description), "");
    public static readonly StyledProperty<Geometry?> IconDataProperty = AvaloniaProperty.Register<ActionCard, Geometry?>(nameof(IconData));
    public string Heading { get => GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public Geometry? IconData { get => GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HeadingProperty) AutomationProperties.SetName(this, Heading);
    }
}
