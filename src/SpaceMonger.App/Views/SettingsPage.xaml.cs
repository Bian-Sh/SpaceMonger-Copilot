using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SpaceMonger.Core.Models.Theme;

namespace SpaceMonger.App.Views;

public partial class SettingsPage : UserControl
{
    public event Action? BackRequested;
    public event Action? SettingsChanged;
    private readonly DispatcherTimer _toastTimer;
    private bool _isLoaded;
    private bool _suppressAutoSave;
    private bool _isShaking;
    private bool _isAnimating;
    private Button[]? _navButtons;

    public SettingsPage()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isLoaded = true;
                UpdateBottomScrollSpacer();
                UpdateActiveSection();
            }), DispatcherPriority.Loaded);
        };
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            if (DataContext is ViewModels.SettingsViewModel vm)
            {
                vm.HideSaveToast();
            }
        };
    }

    private IReadOnlyList<Button> NavButtons => _navButtons ??= [ApiNavButton, GeneralNavButton, ThemeNavButton];

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sectionName })
            return;

        if (FindName(sectionName) is not FrameworkElement target)
            return;

        SnapToSection(target);
        SetActiveNavButton(sectionName);
    }

    private void SettingsContentPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBottomScrollSpacer();
    }

    private void SettingsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
        {
            UpdateBottomScrollSpacer();
            UpdateActiveSection();
        }
    }

    private void UpdateBottomScrollSpacer()
    {
        if (!SettingsScrollViewer.IsLoaded || SettingsScrollViewer.ViewportHeight <= 0)
            return;

        var themeOffset = GetSectionOffset(ThemeSectionAnchor);
        var contentHeightWithoutSpacer = SettingsContentPanel.ActualHeight - BottomScrollSpacer.ActualHeight;
        var requiredContentHeight = themeOffset + SettingsScrollViewer.ViewportHeight;
        BottomScrollSpacer.Height = Math.Max(0, requiredContentHeight - contentHeightWithoutSpacer);
    }

    private void SnapToSection(FrameworkElement section)
    {
        var targetY = GetSectionOffset(section);
        SettingsScrollViewer.ScrollToVerticalOffset(Math.Clamp(targetY, 0, SettingsScrollViewer.ScrollableHeight));
    }

    private void UpdateActiveSection()
    {
        const double activationOffset = 64;
        var scrollOffset = SettingsScrollViewer.VerticalOffset + activationOffset;
        var activeSection = ApiSectionAnchor;

        foreach (var section in new[] { ApiSectionAnchor, GeneralSectionAnchor, ThemeSectionAnchor })
        {
            if (GetSectionOffset(section) <= scrollOffset)
            {
                activeSection = section;
            }
        }

        SetActiveNavButton(activeSection.Name);
    }

    private double GetSectionOffset(FrameworkElement section)
    {
        return section.TransformToAncestor(SettingsContentPanel).Transform(new Point(0, 0)).Y;
    }

    private void SetActiveNavButton(string sectionName)
    {
        foreach (var button in NavButtons)
        {
            var isActive = string.Equals(button.Tag as string, sectionName, StringComparison.Ordinal);
            button.Background = isActive ? (Brush)FindResource("VP.SurfaceHoverBrush") : Brushes.Transparent;
            button.Foreground = (Brush)FindResource(isActive ? "VP.TextPrimaryBrush" : "VP.TextSecondaryBrush");
            button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Medium;
        }
    }

    public void AnimateIn()
    {
        _isAnimating = true;
        Visibility = Visibility.Visible;

        var duration = TimeSpan.FromMilliseconds(350);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        CardBorder.RenderTransform = new ScaleTransform(0.9, 0.9);
        CardBorder.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleXAnim = new DoubleAnimation(0.9, 1, duration) { EasingFunction = easing };
        var scaleYAnim = new DoubleAnimation(0.9, 1, duration) { EasingFunction = easing };

        scaleXAnim.Completed += (_, _) => _isAnimating = false;
        CardBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        CardBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
    }

    public void AnimateOut(Action? onComplete)
    {
        _isAnimating = true;
        var duration = TimeSpan.FromMilliseconds(250);
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        CardBorder.RenderTransform = new ScaleTransform(1, 1);
        CardBorder.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleXAnim = new DoubleAnimation(1, 0.9, duration) { EasingFunction = easing };
        var scaleYAnim = new DoubleAnimation(1, 0.9, duration) { EasingFunction = easing };

        scaleXAnim.Completed += (_, _) =>
        {
            _isAnimating = false;
            Visibility = Visibility.Collapsed;
            onComplete?.Invoke();
        };
        CardBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        CardBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
    }

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isAnimating)
            return;

        if (e.OriginalSource is Grid grid && grid == RootGrid)
        {
            ShakeCard();
            e.Handled = true;
        }
    }

    private void ShakeCard()
    {
        if (_isShaking)
            return;

        _isShaking = true;

        var timeline = new ThicknessAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(350)
        };
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0), TimeSpan.Zero));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(-8, 0, 8, 0), TimeSpan.FromMilliseconds(70)));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(6, 0, -6, 0), TimeSpan.FromMilliseconds(140)));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(-4, 0, 4, 0), TimeSpan.FromMilliseconds(210)));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(2, 0, -2, 0), TimeSpan.FromMilliseconds(280)));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0), TimeSpan.FromMilliseconds(350)));

        Storyboard.SetTarget(timeline, CardBorder);
        Storyboard.SetTargetProperty(timeline, new PropertyPath(MarginProperty));

        var storyboard = new Storyboard();
        storyboard.Children.Add(timeline);
        storyboard.Completed += (_, _) => _isShaking = false;
        storyboard.Begin();
    }

    public void SavePendingChanges()
    {
        if (DataContext is ViewModels.SettingsViewModel vm)
        {
            vm.SaveWithToast();
            SettingsChanged?.Invoke();
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }

    public void ReloadSettingsForOpen()
    {
        if (DataContext is not ViewModels.SettingsViewModel vm)
            return;

        _toastTimer.Stop();
        vm.HideSaveToast();

        _suppressAutoSave = true;
        vm.LoadSettings();

        Dispatcher.BeginInvoke(
            new Action(() => _suppressAutoSave = false),
            DispatcherPriority.Loaded);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        SavePendingChanges();
        BackRequested?.Invoke();
    }

    private void AutoSaveOnLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoaded && !_suppressAutoSave)
            SavePendingChanges();
    }

    private void AutoSaveOnChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoaded && !_suppressAutoSave)
            SavePendingChanges();
    }

    private void ThemePreset_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is ThemeProfile preset)
        {
            if (DataContext is ViewModels.SettingsViewModel vm)
            {
                vm.ApplyThemePresetCommand.Execute(preset);
                SavePendingChanges();
            }

            // Visual feedback: brief scale pulse
            var scaleTransform = new ScaleTransform(1, 1);
            border.RenderTransformOrigin = new Point(0.5, 0.5);
            border.RenderTransform = scaleTransform;

            var pulseIn = new DoubleAnimation(1, 1.05, TimeSpan.FromMilliseconds(80))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var pulseOut = new DoubleAnimation(1.05, 1, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

            pulseIn.Completed += (_, _) =>
            {
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseOut);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseOut);
            };
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseIn);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseIn);
        }
    }
}


