using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace PowerHub.Views;

/// <summary>
/// Фон плитки, поки станція заряджається: тонкі світлі імпульси «струму» біжать зліва направо.
/// Анімується лише TranslateTransform.X, тож усе виконується в потоці композиції й не навантажує UI.
/// Вимикається, якщо в Windows вимкнено «Ефекти анімації».
/// </summary>
public sealed class ChargeFlowBackground : Canvas
{
    private const int StreakCount = 7;
    private const double StreakLength = 110;
    private const double StreakThickness = 2.5;

    /// <summary>Тривалість одного проходу імпульсу за рівнями потужності: до 100 Вт, до 500 Вт, більше</summary>
    private static readonly double[] PassSeconds = { 3.6, 2.4, 1.5 };

    private static readonly bool AnimationsEnabled = ReadAnimationsEnabled();

    private readonly List<(Rectangle Streak, TranslateTransform Move, GradientStop Glow)> _streaks = new();
    private readonly RectangleGeometry _clip = new();
    private readonly Random _random = new();
    private Storyboard? _storyboard;
    private bool _active;
    private int _level = -1;
    private Color _color;

    public ChargeFlowBackground()
    {
        IsHitTestVisible = false;
        Clip = _clip;
        Visibility = Visibility.Collapsed;

        for (var i = 0; i < StreakCount; i++)
        {
            var glow = new GradientStop { Offset = 0.7 };
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            brush.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 0 });
            brush.GradientStops.Add(glow);
            brush.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1 });

            var move = new TranslateTransform();
            var streak = new Rectangle
            {
                Width = StreakLength,
                Height = StreakThickness,
                RadiusX = StreakThickness / 2,
                RadiusY = StreakThickness / 2,
                Fill = brush,
                RenderTransform = move
            };
            Children.Add(streak);
            _streaks.Add((streak, move, glow));
        }

        SizeChanged += (_, _) =>
        {
            _clip.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
            if (_active)
                Restart();
        };
        Unloaded += (_, _) => Stop();
        Loaded += (_, _) =>
        {
            if (_active)
                Restart();
        };
    }

    /// <summary>
    /// Увімкнути або вимкнути анімацію. watts — потужність заряду (визначає швидкість),
    /// color — колір джерела (мережа або сонце)
    /// </summary>
    public void SetCharging(bool charging, int watts, Color color)
    {
        charging &= AnimationsEnabled;
        var level = watts < 100 ? 0 : watts < 500 ? 1 : 2;

        if (!charging)
        {
            _active = false;
            _level = -1;
            Stop();
            Visibility = Visibility.Collapsed;
            return;
        }

        // Перезапускаємо лише коли змінилося щось помітне, інакше анімація смикалась би кожні 10 с
        var changed = !_active || level != _level || color != _color;
        _active = true;
        _level = level;
        _color = color;
        Visibility = Visibility.Visible;
        if (changed && IsLoaded)
            Restart();
    }

    private void Restart()
    {
        Stop();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var pass = TimeSpan.FromSeconds(PassSeconds[Math.Clamp(_level, 0, PassSeconds.Length - 1)]);
        _storyboard = new Storyboard();

        for (var i = 0; i < _streaks.Count; i++)
        {
            var (streak, move, glow) = _streaks[i];

            // Імпульси рівномірно розкладено по висоті з невеликим розкидом; верх і низ лишаються вільними
            var band = (height - 40) / _streaks.Count;
            SetTop(streak, 20 + band * i + _random.NextDouble() * Math.Max(band - StreakThickness, 0));
            SetLeft(streak, 0);
            glow.Color = Color.FromArgb((byte)(_random.Next(70, 140)), _color.R, _color.G, _color.B);

            // Трохи різна швидкість і зсув у часі, щоб імпульси не йшли строєм
            var duration = pass * (0.8 + _random.NextDouble() * 0.5);
            var run = new DoubleAnimation
            {
                From = -StreakLength,
                To = width,
                Duration = new Duration(duration),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(_random.NextDouble() * duration.TotalMilliseconds)
            };
            Storyboard.SetTarget(run, move);
            Storyboard.SetTargetProperty(run, "X");
            _storyboard.Children.Add(run);
        }

        _storyboard.Begin();
    }

    private void Stop()
    {
        _storyboard?.Stop();
        _storyboard = null;
    }

    private static bool ReadAnimationsEnabled()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }
}
