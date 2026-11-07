using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using FlashVsr.Desktop.Models;
using FlashVsr.Desktop.Services;

namespace FlashVsr.Desktop.Views;
public partial class MainWindow : Window
{
    private string? _input, _lastOutput, _previewPath, _comparisonPath;
    private bool _busy, _autoName = true, _updatingName;
    private CancellationTokenSource? _jobCts, _probeCts;
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _frames, _total, _width = 1920, _height = 1080;
    private double _duration;
    private bool High => HighRadio?.IsChecked == true;
    private Resolution SelectedResolution => Presets.ForVideo(_width, _height, High);
    public MainWindow()
    {
        InitializeComponent(); App.DarkCaption(this);
        FolderBox.Text = AppPaths.DefaultOutput;
        Player.BrowseRequested += (_, _) => { if (!_busy) ChooseVideo(); };
        _timer.Tick += (_, _) => { if (_busy) FrameProgress.Text = $"{_frames:N0} / {_total:N0} frames · {_elapsed.Elapsed:mm\\:ss}"; };
        UpdateOptions();
    }
    private void Choose_Click(object sender, RoutedEventArgs e) { if (!_busy) ChooseVideo(); }
    private void ChooseVideo()
    {
        var dialog = new OpenFileDialog { Title = "Open video", Filter = "Video|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v|All files|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) _ = OpenVideoAsync(dialog.FileName);
    }
    private void Video_DragOver(object sender, DragEventArgs e) { e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Video_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) _ = OpenVideoAsync(files[0]);
    }
    public async Task OpenVideoAsync(string path)
    {
        if (_busy || !File.Exists(path)) return;
        _probeCts?.Cancel();
        var cts = new CancellationTokenSource(); _probeCts = cts; var token = cts.Token;
        _input = _lastOutput = null; StartButton.IsEnabled = false; Player.Clear();
        DeletePreview(_comparisonPath); _comparisonPath = null;
        var oldPreview = _previewPath; _previewPath = null; DeletePreview(oldPreview);
        var folder = Path.Combine(AppPaths.Data, "previews"); Directory.CreateDirectory(folder);
        var preview = Path.Combine(folder, Guid.NewGuid() + ".mp4");
        var thumbnail = Path.ChangeExtension(preview, ".jpg");
        StatusTitle.Text = "Reading video…"; FrameProgress.Text = ""; OpenFolderButton.Visibility = Visibility.Collapsed;
        JobProgress.Visibility = Visibility.Collapsed;
        try {
            await new WorkerService().RunAsync(["--probe", path, "--thumbnail", thumbnail], item => {
                if (item.GetProperty("event").GetString() != "probe") return;
                Dispatcher.Invoke(() => {
                    if (token.IsCancellationRequested) return;
                    _input = Path.GetFullPath(path);
                    _width = item.GetProperty("width").GetInt32(); _height = item.GetProperty("height").GetInt32();
                    _total = item.GetProperty("frames").GetInt32(); _duration = item.GetProperty("duration").GetDouble();
                    VideoName.Text = Path.GetFileName(path); VideoName.ToolTip = path;
                    VideoInfo.Text = $"{_width} × {_height} · {item.GetProperty("fps").GetDouble():0.##} fps · {TimeSpan.FromSeconds(_duration):mm\\:ss}";
                    VideoInfo.Visibility = Visibility.Visible;
                    if (File.Exists(thumbnail)) {
                        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                        image.UriSource = new Uri(thumbnail); image.EndInit(); image.Freeze(); Player.SetPoster(image);
                    }
                    _autoName = true; UpdateOptions(); StatusTitle.Text = ""; StartButton.IsEnabled = true;
                });
            }, AddLog, token);
            await new WorkerService().RunAsync(["--preview", path, "--output", preview], _ => { }, AddLog, token);
            token.ThrowIfCancellationRequested();
            _previewPath = preview;
            Player.Load(_comparisonPath ?? preview, _comparisonPath != null, _duration);
        } catch (OperationCanceledException) { DeletePreview(preview); }
        catch (Exception error) {
            if (!token.IsCancellationRequested) {
                if (_input == null) StatusTitle.Text = error.Message;
                else Player.Message("Preview unavailable. You can still upscale this video.");
                AddLog(error.ToString());
            }
            DeletePreview(preview);
        } finally { DeletePreview(thumbnail); if (ReferenceEquals(_probeCts, cts)) _probeCts = null; cts.Dispose(); }
    }
    private static void DeletePreview(string? path) { if (path == null) return; try { File.Delete(path); if (path.EndsWith(".mp4")) File.Delete(Path.ChangeExtension(path,".partial.mp4")); } catch (IOException) { } }
    private void Quality_Changed(object sender, RoutedEventArgs e) => UpdateOptions();
    private void UpdateOptions()
    {
        if (StandardSize == null || HighSize == null || NameBox == null) return;
        StandardSize.Text = Presets.ForVideo(_width, _height, false).Label;
        HighSize.Text = Presets.ForVideo(_width, _height, true).Label;
        var p = SelectedResolution;
        if (_autoName && _input != null) {
            _updatingName = true;
            try { NameBox.Text = OutputDestination.DefaultName(_input, p); }
            finally { _updatingName = false; }
        }
    }
    private void Name_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (!_updatingName) _autoName = string.IsNullOrWhiteSpace(NameBox.Text); }
    private void Name_LostFocus(object sender, KeyboardFocusChangedEventArgs e) { if (string.IsNullOrWhiteSpace(NameBox.Text)) { _autoName = true; UpdateOptions(); } }
    private void Folder_LostFocus(object sender, KeyboardFocusChangedEventArgs e) { if (string.IsNullOrWhiteSpace(FolderBox.Text)) FolderBox.Text = AppPaths.DefaultOutput; }
    private void Output_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose output folder" };
        if (Directory.Exists(FolderBox.Text)) dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_input == null || _busy) return;
        string output;
        try {
            output = OutputDestination.Resolve(_input, NameBox.Text, FolderBox.Text, SelectedResolution, AppPaths.DefaultOutput);
            if (File.Exists(output) && MessageBox.Show(this, "Replace this file once upscaling completes?", "File already exists", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        } catch (Exception error) { MessageBox.Show(this, error.Message, "Output file"); return; }
        Player.Clear(); DeletePreview(_comparisonPath); _comparisonPath = null;
        if (_previewPath != null) Player.Load(_previewPath, false, _duration);
        _lastOutput = null; Player.Pause();
        var job = Path.Combine(AppPaths.Data, "jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
        Directory.CreateDirectory(Path.Combine(AppPaths.Data, "logs"));
        using var log = new StreamWriter(Path.Combine(AppPaths.Data, "logs", $"{DateTime.Now:yyyyMMdd-HHmmss}.log")) { AutoFlush = true };
        void Log(string line) { lock (log) log.WriteLine(line); AddLog(line); }
        var completed = false; _jobCts = new(); SetBusy(true); _frames = 0; _elapsed.Restart(); _timer.Start();
        JobProgress.Value = 0; JobProgress.Visibility = Visibility.Visible; LogBox.Clear(); OpenFolderButton.Visibility = Visibility.Collapsed;
        try {
            await new WorkerService().RunAsync(["--input", _input, "--output", output, "--aspect", "native", "--quality", High ? "high" : "standard", "--work", job], item => {
                Dispatcher.Invoke(() => {
                    var kind = item.GetProperty("event").GetString();
                    if (item.TryGetProperty("progress", out var percent)) JobProgress.Value = percent.GetDouble();
                    if (item.TryGetProperty("message", out var message)) { if (kind != "log") StatusTitle.Text = message.GetString(); Log(message.GetString() ?? ""); }
                    if (item.TryGetProperty("frames", out var frames)) _frames = frames.GetInt32();
                    if (kind == "complete") { completed = true; _lastOutput = output; }
                });
            }, Log, _jobCts.Token);
            if (!completed) throw new InvalidOperationException("The engine did not confirm that the video was completed.");
            StatusTitle.Text = "Preparing comparison…"; JobProgress.IsIndeterminate = true;
            var comparison = Path.Combine(AppPaths.Data, "previews", Guid.NewGuid() + ".mp4");
            try {
                await new WorkerService().RunAsync(["--compare",_input,"--result",output,"--output",comparison], _=>{}, Log, _jobCts.Token);
                _comparisonPath = comparison;
            } catch (Exception error) { DeletePreview(comparison); Log(error.ToString()); }
        } catch (OperationCanceledException) { StatusTitle.Text = "Cancelled"; }
        catch (Exception error) { StatusTitle.Text = error.Message; Log(error.ToString()); }
        finally {
            _timer.Stop(); _elapsed.Stop(); SetBusy(false); JobProgress.IsIndeterminate = false; _jobCts.Dispose(); _jobCts = null;
            try { Directory.Delete(job, recursive: true); } catch (IOException) { }
            if (completed) {
                StatusTitle.Text = "Complete"; FrameProgress.Text = $"{_frames} frames · {_elapsed.Elapsed:mm\\:ss}";
                JobProgress.Value = 100; OpenFolderButton.Visibility = Visibility.Visible;
                if (_comparisonPath != null) Player.Load(_comparisonPath, true, _duration);
                else Player.Message("Comparison unavailable. The upscaled video is saved.");
            }
        }
    }
    private void SetBusy(bool value)
    {
        _busy = value; OptionsPanel.IsEnabled = SetupButton.IsEnabled = VideoName.IsEnabled = !value; Player.CanBrowse = !value;
        StartButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible; CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        StartButton.IsEnabled = !value && _input != null; CancelButton.IsEnabled = true;
    }
    private void AddLog(string line) => Dispatcher.BeginInvoke(() => { if (LogBox.Text.Length > 80000) LogBox.Text = LogBox.Text[^40000..]; LogBox.AppendText(line + Environment.NewLine); LogBox.ScrollToEnd(); });
    private void Cancel_Click(object sender, RoutedEventArgs e) { _jobCts?.Cancel(); CancelButton.IsEnabled = false; StatusTitle.Text = "Cancelling…"; }
    private void Setup_Click(object sender, RoutedEventArgs e) => new SetupWindow(AppPaths.Root) { Owner = this }.ShowDialog();
    private void OpenFolder_Click(object sender, RoutedEventArgs e) { if (_lastOutput != null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastOutput}\"") { UseShellExecute = true }); }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
        if (e.Key == Key.Space) { Player.TogglePlayback(); e.Handled = true; }
        else if (e.Key == Key.F11) { Player.ToggleFullscreen(); e.Handled = true; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy) {
            if (MessageBox.Show(this, "Cancel upscaling and close the application?", "Upscaling in progress", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; return; }
            _jobCts?.Cancel();
        }
        _probeCts?.Cancel(); Player.Dispose(); DeletePreview(_previewPath); DeletePreview(_comparisonPath);
    }
}
