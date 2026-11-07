using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FlashVsr.Desktop.Views;
public partial class ComparisonPlayer : UserControl
{
    private MediaClock? _clock;
    private readonly MediaPlayer _media = new() { Volume = .8, ScrubbingEnabled = true };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private double _duration, _split = .5, _loadPosition;
    private bool _playing, _updatingSeek, _seeking, _resumeAfterSeek, _hasAfter, _ended;
    private Window? _fullscreen;
    public bool HasVideo => _clock != null;
    public event EventHandler? BrowseRequested;
    public bool CanBrowse { get => EmptyState.IsEnabled; set => EmptyState.IsEnabled = value; }
    private void Browse_Click(object sender, RoutedEventArgs e) { e.Handled = true; BrowseRequested?.Invoke(this, EventArgs.Empty); }
    public ComparisonPlayer()
    {
        InitializeComponent();
        _media.MediaOpened += Media_Opened;
        _media.MediaFailed += (_, _) => Media_Failed();
        _timer.Tick += (_, _) => {
            if (_clock == null || _seeking) return;
            var seconds = _ended ? _duration : Math.Clamp(_clock.CurrentTime?.TotalSeconds ?? 0, 0, _duration);
            _updatingSeek = true; SeekBar.Value = seconds; _updatingSeek = false;
            TimeLabel.Text = $"{Format(seconds)} / {Format(_duration)}";
        };
    }
    private static string Format(double seconds) => seconds >= 3600 ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");
    public void SetPoster(BitmapSource image)
    {
        Poster.Source = image; Poster.Visibility = Visibility.Visible; EmptyState.Visibility = Visibility.Collapsed;
        Message("Preparing preview…");
    }
    public void Message(string? text)
    {
        PreviewMessageText.Text = text;
        PreviewMessage.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }
    public void Load(string path, bool comparison, double duration)
    {
        var position = _clock?.CurrentTime?.TotalSeconds ?? 0;
        var resume = _playing;
        ReleaseClocks();
        _duration = Math.Max(.01, duration); _hasAfter = comparison;
        _loadPosition = Math.Min(position, _duration);
        SeekBar.Maximum = _duration;
        PlayButton.IsEnabled = SeekBar.IsEnabled = false;
        var timeline = new MediaTimeline(new Uri(path)) { Duration = TimeSpan.FromSeconds(_duration) };
        _clock = (MediaClock)timeline.CreateClock(true);
        _ended = false;
        _clock.Completed += (_, _) => { _playing = false; _ended = true; UpdatePlayIcon(); };
        _media.Clock = _clock;
        _clock.Controller!.Begin(); _clock.Controller.Pause();
        _clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(Math.Min(position, _duration)), TimeSeekOrigin.BeginTime);
        _resumeAfterSeek = resume;
        EmptyState.Visibility = Visibility.Collapsed;
        AfterMode.IsEnabled = SlideMode.IsEnabled = _hasAfter;
        if (_hasAfter) SlideMode.IsChecked = true; else BeforeMode.IsChecked = true;
        UpdateMode(); _timer.Start();
    }
    private async void Media_Opened(object? sender, EventArgs e)
    {
        var drawing = new VideoDrawing { Player = _media, Rect = new Rect(0,0,_media.NaturalVideoWidth,_media.NaturalVideoHeight) };
        var half = _media.NaturalVideoWidth / 2d;
        BeforeFrame.Fill = RegionBrush(drawing, new Rect(0,0,_hasAfter ? half : _media.NaturalVideoWidth,_media.NaturalVideoHeight));
        AfterFrame.Fill = RegionBrush(drawing, new Rect(half,0,half,_media.NaturalVideoHeight));
        var clock = _clock;
        // Prime the decoder silently before showing the first paused frame.
        // MediaOpened alone does not guarantee that a video frame is ready.
        _media.Volume = 0; clock?.Controller?.Resume();
        await Task.Delay(180);
        if (!ReferenceEquals(clock,_clock)) return;
        clock?.Controller?.Pause();
        clock?.Controller?.SeekAlignedToLastTick(TimeSpan.FromSeconds(_loadPosition), TimeSeekOrigin.BeginTime);
        await Task.Delay(100);
        if (!ReferenceEquals(clock,_clock)) return;
        _media.Volume = .8;
        Poster.Visibility = Visibility.Collapsed; Message(null);
        PlayButton.IsEnabled = SeekBar.IsEnabled = true;
        if (_resumeAfterSeek) { _playing = true; _clock?.Controller?.Resume(); }
        UpdatePlayIcon();
    }
    private static DrawingBrush RegionBrush(VideoDrawing drawing, Rect region)
    {
        // A brush viewbox alone does not clip drawing outside the viewbox.
        // Clip the source group as well, so letterboxing never leaks pixels
        // from the other half of the packed comparison stream.
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(region) };
        group.Children.Add(drawing);
        return new DrawingBrush(group) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = region, Stretch = Stretch.Uniform };
    }
    private void Media_Failed()
    {
        _clock?.Controller?.Pause(); _playing = false; UpdatePlayIcon();
        PlayButton.IsEnabled = SeekBar.IsEnabled = false;
        Message("Preview unavailable. You can still upscale and open the saved video.");
    }
    public void Clear()
    {
        ReleaseClocks(); _hasAfter = false; Poster.Source = null; Poster.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible; Message(null);
        PlayButton.IsEnabled = SeekBar.IsEnabled = AfterMode.IsEnabled = SlideMode.IsEnabled = false;
        _updatingSeek = true; SeekBar.Value = 0; _updatingSeek = false;
        TimeLabel.Text = "00:00 / 00:00"; BeforeMode.IsChecked = true; UpdateMode();
    }
    private void ReleaseClocks()
    {
        _timer.Stop(); _clock?.Controller?.Remove(); _clock = null;
        _media.Clock = null; _media.Close(); BeforeFrame.Fill = AfterFrame.Fill = null;
        _playing = false; UpdatePlayIcon();
    }
    public void TogglePlayback()
    {
        if (_clock == null || !PlayButton.IsEnabled) return;
        if (_playing) _clock.Controller!.Pause();
        else {
            if (_ended || (_clock.CurrentTime?.TotalSeconds ?? 0) >= _duration - .02)
                _clock.Controller!.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
            _ended = false;
            _clock.Controller!.Resume();
        }
        _playing = !_playing; UpdatePlayIcon();
    }
    public void Pause() { if (_playing) TogglePlayback(); }
    private void UpdatePlayIcon() { if (PlayIcon != null) PlayIcon.Text = _playing ? "\uE769" : "\uE768"; }
    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlayback();
    private void Seek_Down(object sender, MouseButtonEventArgs e) { _seeking = true; _resumeAfterSeek = _playing; _clock?.Controller?.Pause(); }
    private void Seek_Up(object sender, MouseButtonEventArgs e) { _seeking = false; if (_resumeAfterSeek) _clock?.Controller?.Resume(); }
    private void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSeek || _clock == null) return;
        _ended = false;
        _clock.Controller!.SeekAlignedToLastTick(TimeSpan.FromSeconds(e.NewValue), TimeSeekOrigin.BeginTime);
        TimeLabel.Text = $"{Format(e.NewValue)} / {Format(_duration)}";
    }
    private void Mode_Changed(object sender, RoutedEventArgs e) => UpdateMode();
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMode();
    private void Split_DragDelta(object sender, DragDeltaEventArgs e) { _split = Math.Clamp(_split + e.HorizontalChange / Math.Max(1, Viewport.ActualWidth), 0, 1); UpdateMode(); }
    private void UpdateMode()
    {
        if (AfterFrame == null || Divider == null) return;
        var slide = _hasAfter && SlideMode.IsChecked == true;
        var after = _hasAfter && AfterMode.IsChecked == true;
        // Both brushes read the SAME decoded frame. View changes, seeking,
        // buffering and full screen cannot desynchronize before and after.
        AfterFrame.Opacity = slide || after ? 1 : 0;
        AfterFrame.Clip = slide ? new RectangleGeometry(new Rect(Viewport.ActualWidth * _split, 0, Math.Max(0, Viewport.ActualWidth * (1 - _split)), Viewport.ActualHeight)) : null;
        Divider.Visibility = slide ? Visibility.Visible : Visibility.Collapsed;
        Divider.Margin = new Thickness(Viewport.ActualWidth * _split - 16, 0, 0, 0);
        BeforeBadge.Visibility = slide ? Visibility.Visible : Visibility.Collapsed;
        AfterBadge.Visibility = slide ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    public void ToggleFullscreen()
    {
        if (_fullscreen != null) { _fullscreen.Close(); return; }
        if (Parent is not ContentControl host) return;
        var owner = Window.GetWindow(this); host.Content = null;
        _fullscreen = new Window { Title = "FlashVSR — Preview", Owner = owner, Background = Brushes.Black, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, WindowState = WindowState.Maximized, Content = this };
        _fullscreen.PreviewKeyDown += (_, e) => {
            if (e.Key is Key.Escape or Key.F11) { e.Handled = true; _fullscreen?.Close(); }
            else if (e.Key == Key.Space) { e.Handled = true; TogglePlayback(); }
        };
        FullscreenIcon.Text = "\uE73F";
        FullscreenButton.ToolTip = "Exit full screen (Esc)";
        System.Windows.Automation.AutomationProperties.SetName(FullscreenButton, "Exit full screen");
        _fullscreen.Closed += (_, _) => {
            _fullscreen!.Content = null; _fullscreen = null; host.Content = this;
            FullscreenIcon.Text = "\uE740"; FullscreenButton.ToolTip = "Full screen (F11)";
            System.Windows.Automation.AutomationProperties.SetName(FullscreenButton, "Full screen");
            owner?.Activate();
        };
        _fullscreen.Show();
    }
    public void Dispose() { _fullscreen?.Close(); ReleaseClocks(); }
}
