using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lensly.Controls;

public class GlassBorder : Border
{
    public static readonly StyledProperty<double> BlurRadiusProperty =
        AvaloniaProperty.Register<GlassBorder, double>(nameof(BlurRadius), 20.0);

    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<GlassBorder, Color>(nameof(TintColor), Colors.White);

    public static readonly StyledProperty<double> TintOpacityProperty =
        AvaloniaProperty.Register<GlassBorder, double>(nameof(TintOpacity), 0.7);

    public static readonly StyledProperty<double> NoiseOpacityProperty =
        AvaloniaProperty.Register<GlassBorder, double>(nameof(NoiseOpacity), 0.02);

    public double BlurRadius
    {
        get => GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public Color TintColor
    {
        get => GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public double TintOpacity
    {
        get => GetValue(TintOpacityProperty);
        set => SetValue(TintOpacityProperty, value);
    }

    public double NoiseOpacity
    {
        get => GetValue(NoiseOpacityProperty);
        set => SetValue(NoiseOpacityProperty, value);
    }

    static GlassBorder()
    {
        BackgroundProperty.OverrideDefaultValue<GlassBorder>(null);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == TintColorProperty || 
            change.Property == TintOpacityProperty)
        {
            UpdateBackground();
        }
    }

    private void UpdateBackground()
    {
        var color = TintColor;
        var opacity = TintOpacity;
        Background = new SolidColorBrush(Color.FromArgb(
            (byte)(opacity * 255),
            color.R,
            color.G,
            color.B));
    }
}
