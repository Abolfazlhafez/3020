using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FoxyVPN.Windows.Services;

/// <summary>Shared, cancellable UI motion helpers. Every helper is a no-op when motion is disabled.</summary>
internal static class Motion
{
    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseOutQuint = Freeze(new QuinticEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseInOut = Freeze(new SineEase { EasingMode = EasingMode.EaseInOut });

    /// <summary>False when the user enabled "Reduce motion" or Windows animations are turned off.</summary>
    public static bool Enabled
    {
        get
        {
            var services = App.Services;
            if (services is null) return false;
            return !services.Settings.Current.ReduceMotion && SystemParameters.ClientAreaAnimation;
        }
    }

    /// <summary>Fade + rise entrance, staggered by <paramref name="index"/>.</summary>
    public static void Reveal(UIElement element, int index, double distance = 22)
    {
        if (!Enabled)
        {
            Reset(element);
            return;
        }
        var shift = new TranslateTransform(0, distance);
        element.RenderTransform = shift;
        element.Opacity = 0;
        var delay = TimeSpan.FromMilliseconds(60 * index);
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = delay, EasingFunction = EaseOut };
        var rise = new DoubleAnimation(distance, 0, TimeSpan.FromMilliseconds(560)) { BeginTime = delay, EasingFunction = EaseOutQuint };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
        };
        rise.Completed += (_, _) =>
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        shift.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    /// <summary>Removes any entrance state so the element is fully visible and in place.</summary>
    public static void Reset(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        var shift = element.RenderTransform as TranslateTransform;
        if (shift is not null && !shift.IsFrozen)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
        }
    }

    /// <summary>Raises or lowers a card by a few pixels (hover feedback).</summary>
    public static void Lift(UIElement element, bool lifted)
    {
        if (!Enabled) return;
        var shift = element.RenderTransform as TranslateTransform;
        if (shift is null || shift.IsFrozen)
        {
            shift = new TranslateTransform();
            element.RenderTransform = shift;
        }
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(lifted ? -3 : 0, TimeSpan.FromMilliseconds(lifted ? 180 : 260)) { EasingFunction = EaseOut });
    }

    /// <summary>Slow opacity "breathing" for a live-status indicator.</summary>
    public static void Breathe(UIElement element, bool on)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (!on || !Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(1.3))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = EaseInOut
            });
    }

    /// <summary>Quick cross-fade used when a label's text changes.</summary>
    public static void Flash(UIElement element)
    {
        if (!Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = EaseOut });
    }

    private static T Freeze<T>(T easing) where T : Freezable
    {
        easing.Freeze();
        return easing;
    }
}
