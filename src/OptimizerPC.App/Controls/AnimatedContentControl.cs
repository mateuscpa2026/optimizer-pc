using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OptimizerPC.App.Controls;

/// <summary>
/// Hospeda a tela atual e aplica uma transicao curta quando o conteudo muda.
/// Com "Reduzir animacoes" ativo a troca e instantanea.
/// </summary>
public sealed class AnimatedContentControl : ContentControl
{
    private static readonly Duration TransitionDuration = new(TimeSpan.FromMilliseconds(190));

    private readonly TranslateTransform _translate = new();

    public AnimatedContentControl()
    {
        RenderTransform = _translate;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset),
        typeof(double),
        typeof(AnimatedContentControl),
        new PropertyMetadata(9d));

    /// <summary>Deslocamento vertical inicial da transicao, em pixels.</summary>
    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        BeginAnimation(OpacityProperty, null);
        _translate.BeginAnimation(TranslateTransform.YProperty, null);

        if (!Motion.Enabled || oldContent is null || newContent is null)
        {
            Opacity = 1d;
            _translate.Y = 0d;
            return;
        }

        Opacity = 1d;
        _translate.Y = 0d;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0d, 1d, TransitionDuration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });

        _translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(Offset, 0d, TransitionDuration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });
    }
}
