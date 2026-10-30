using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
    private string? _input, _lastOutput;
    private bool _busy, _autoOutput = true, _updatingOutput;
    private CancellationTokenSource? _jobCts, _probeCts;
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _frames, _total;
    private AspectPreset SelectedAspect => (AspectPreset?)AspectBox.SelectedItem ?? Presets.All.Single(p => p.Id == "16:9");
    private bool High => HighRadio?.IsChecked == true;
    private Resolution SelectedResolution => High ? SelectedAspect.High : SelectedAspect.Standard;
    public MainWindow()
    {
        InitializeComponent(); App.DarkCaption(this);
        AspectBox.ItemsSource = Presets.All;
        AspectBox.SelectedItem = Presets.All.Single(p => p.Id == "16:9");
        _timer.Tick += (_, _) => { if (_busy) FrameProgress.Text = $"{_frames:N0} / {_total:N0} fotogrammi  ·  {_elapsed.Elapsed:mm\\:ss} trascorsi"; };
        Loaded += async (_, _) => {
            try { GpuLabel.Text = await ProcessRunner.CaptureAsync("nvidia-smi.exe", ["--query-gpu=name", "--format=csv,noheader"], AppPaths.Root, CancellationToken.None); }
            catch { GpuLabel.Text = "Verifica il driver NVIDIA"; }
        };
    }
    private void Choose_Click(object sender, RoutedEventArgs e) => ChooseVideo();
    private void Drop_Click(object sender, MouseButtonEventArgs e) { if (!_busy) ChooseVideo(); }
    private void ChooseVideo()
    {
        var dialog = new OpenFileDialog { Title = "Scegli il video da migliorare", Filter = "Video|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v|Tutti i file|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) _ = OpenVideoAsync(dialog.FileName);
    }
    private void Video_DragOver(object sender, DragEventArgs e) { e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Video_Drop(object sender, DragEventArgs e)
    {
        if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) _ = OpenVideoAsync(files[0]);
        else if (!_busy) MessageBox.Show(this, "Trascina un solo video alla volta.", "Scegli un video");
    }
    public async Task OpenVideoAsync(string path)
    {
        _probeCts?.Cancel(); _probeCts = new(); var token = _probeCts.Token;
        if (!File.Exists(path)) return;
        _input = null; StartButton.IsEnabled = false;
        var folder = Path.Combine(AppPaths.Data, "previews"); Directory.CreateDirectory(folder);
        var thumbnail = Path.Combine(folder, Guid.NewGuid() + ".jpg");
        StatusTitle.Text = "Lettura del video…"; StatusDetail.Text = Path.GetFileName(path);
        try {
            await new WorkerService().RunAsync(["--probe", path, "--thumbnail", thumbnail], item => {
                if (item.GetProperty("event").GetString() != "probe") return;
                Dispatcher.Invoke(() => {
                    if (token.IsCancellationRequested) return;
                    _input = Path.GetFullPath(path);
                    var width = item.GetProperty("width").GetInt32(); var height = item.GetProperty("height").GetInt32();
                    _total = item.GetProperty("frames").GetInt32();
                    var duration = TimeSpan.FromSeconds(item.GetProperty("duration").GetDouble());
                    VideoName.Text = Path.GetFileName(path); VideoName.ToolTip = path;
                    VideoInfo.Text = $"{width} × {height}  ·  {item.GetProperty("fps").GetDouble():0.##} fps  ·  {duration:mm\\:ss}  ·  {_total} fotogrammi";
                    if (File.Exists(thumbnail)) { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(thumbnail); image.EndInit(); image.Freeze(); Preview.Source = image; DropHint.Visibility = Visibility.Collapsed; }
                    AspectBox.SelectedItem = Presets.All.MinBy(p => { var a = p.Id.Split(':'); return Math.Abs(Math.Log((double)width / height / (double.Parse(a[0]) / double.Parse(a[1])))); });
                    _autoOutput = true; UpdateOptions();
                    StatusTitle.Text = "Il tuo video è pronto."; StatusDetail.Text = "Scegli la qualità e avvia l’upscale.";
                    StartButton.IsEnabled = true; JobProgress.Value = 0; FrameProgress.Text = "";
                });
            }, AddLog, token);
        } catch (OperationCanceledException) { }
        catch (Exception error) { StatusTitle.Text = "Impossibile leggere il video"; StatusDetail.Text = error.Message; }
        finally { if (File.Exists(thumbnail)) File.Delete(thumbnail); }
    }
    private void Options_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateOptions();
    private void Quality_Changed(object sender, RoutedEventArgs e) => UpdateOptions();
    private void UpdateOptions()
    {
        if (StandardSize == null || AspectBox?.SelectedItem == null || HighSize == null) return;
        StandardSize.Text = SelectedAspect.Standard.NativeLabel;
        HighSize.Text = SelectedAspect.High.NativeLabel;
        var p = SelectedResolution;
        GeometryInfo.Text = $"Elaborazione {p.NativeLabel}\nUscita {p.Width} × {p.Height}  ·  {SelectedAspect.Id} esatto";
        HighWarning.Visibility = High ? Visibility.Visible : Visibility.Collapsed;
        if (_autoOutput && _input != null) {
            _updatingOutput = true;
            try { OutputBox.Text = Path.Combine(AppPaths.DefaultOutput, $"{Path.GetFileNameWithoutExtension(_input)}_FlashVSR_{p.Width}x{p.Height}.mp4"); }
            finally { _updatingOutput = false; }
        }
    }
    private void Output_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (!_updatingOutput) _autoOutput = false; }
    private void Output_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Salva il video migliorato", Filter = "Video MP4|*.mp4", DefaultExt = ".mp4", AddExtension = true, OverwritePrompt = true, FileName = Path.GetFileName(OutputBox.Text) };
        var folder = Path.GetDirectoryName(OutputBox.Text); if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(this) == true) { OutputBox.Text = dialog.FileName; _autoOutput = false; }
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_input == null || _busy) return;
        string output;
        try {
            output = Path.GetFullPath(OutputBox.Text.Trim());
            if (!output.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Scegli un file di uscita con estensione .mp4.");
            if (output.Equals(_input, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Il video originale non può essere sovrascritto.");
            if (File.Exists(output) && MessageBox.Show(this, "Il file esiste già. Sostituirlo solo quando la nuova elaborazione sarà completata?", "File già presente", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        } catch (Exception error) { MessageBox.Show(this, error.Message, "Percorso di salvataggio"); return; }
        var job = Path.Combine(AppPaths.Data, "jobs", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
        Directory.CreateDirectory(Path.Combine(AppPaths.Data, "logs"));
        using var log = new StreamWriter(Path.Combine(AppPaths.Data, "logs", $"{DateTime.Now:yyyyMMdd-HHmmss}.log")) { AutoFlush = true };
        void Log(string line) { lock (log) log.WriteLine(line); AddLog(line); }
        var completed = false; _jobCts = new(); SetBusy(true); _frames = 0; _elapsed.Restart(); _timer.Start();
        JobProgress.Value = 0; LogBox.Clear(); OpenVideoButton.Visibility = OpenFolderButton.Visibility = Visibility.Collapsed;
        try {
            await new WorkerService().RunAsync(["--input", _input, "--output", output, "--aspect", SelectedAspect.Id, "--quality", High ? "high" : "standard", "--work", job], item => {
                Dispatcher.Invoke(() => {
                    var kind = item.GetProperty("event").GetString();
                    if (item.TryGetProperty("progress", out var percent)) JobProgress.Value = percent.GetDouble();
                    if (item.TryGetProperty("message", out var message)) { if (kind != "log") StatusTitle.Text = message.GetString(); Log(message.GetString() ?? ""); }
                    if (item.TryGetProperty("frames", out var frames)) _frames = frames.GetInt32();
                    if (kind == "complete") { completed = true; _lastOutput = output; }
                    StatusDetail.Text = $"{SelectedResolution.Width} × {SelectedResolution.Height}  ·  {SelectedAspect.Id}  ·  nessun ritaglio";
                });
            }, Log, _jobCts.Token);
            if (!completed) throw new InvalidOperationException("Il motore non ha confermato il completamento del video.");
        } catch (OperationCanceledException) { StatusTitle.Text = "Elaborazione interrotta."; StatusDetail.Text = "Il video originale e gli eventuali file precedenti sono rimasti intatti."; }
        catch (Exception error) { StatusTitle.Text = "L’elaborazione non è riuscita."; StatusDetail.Text = error.Message; Log(error.ToString()); }
        finally {
            _timer.Stop(); _elapsed.Stop(); SetBusy(false); _jobCts.Dispose(); _jobCts = null;
            try { Directory.Delete(job, recursive: true); } catch (IOException) { }
            if (completed) { StatusTitle.Text = "Il tuo video è pronto."; StatusDetail.Text = $"{SelectedResolution.Width} × {SelectedResolution.Height}  ·  salvato senza ritagli"; FrameProgress.Text = $"{_frames} fotogrammi  ·  completato in {_elapsed.Elapsed:mm\\:ss}"; JobProgress.Value = 100; OpenVideoButton.Visibility = OpenFolderButton.Visibility = Visibility.Visible; }
        }
    }
    private void SetBusy(bool value) { _busy = value; OptionsPanel.IsEnabled = ChangeButton.IsEnabled = SetupButton.IsEnabled = !value; DropZone.IsEnabled = !value; StartButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible; CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed; StartButton.IsEnabled = !value && _input != null; }
    private void AddLog(string line) => Dispatcher.BeginInvoke(() => { if (LogBox.Text.Length > 80000) LogBox.Text = LogBox.Text[^40000..]; LogBox.AppendText(line + Environment.NewLine); LogBox.ScrollToEnd(); });
    private void Cancel_Click(object sender, RoutedEventArgs e) { _jobCts?.Cancel(); CancelButton.IsEnabled = false; StatusTitle.Text = "Interruzione in corso…"; CancelButton.IsEnabled = true; }
    private void Setup_Click(object sender, RoutedEventArgs e) => new SetupWindow(AppPaths.Root) { Owner = this }.ShowDialog();
    private void OpenVideo_Click(object sender, RoutedEventArgs e) { if (_lastOutput != null) AppPaths.OpenPath(_lastOutput); }
    private void OpenFolder_Click(object sender, RoutedEventArgs e) { if (_lastOutput != null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastOutput}\"") { UseShellExecute = true }); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy) { if (MessageBox.Show(this, "Interrompere l’elaborazione e chiudere l’applicazione?", "Elaborazione in corso", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; return; } _jobCts?.Cancel(); }
        _probeCts?.Cancel();
    }
}
