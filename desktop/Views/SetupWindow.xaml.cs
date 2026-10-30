using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using FlashVsr.Desktop.Services;

namespace FlashVsr.Desktop.Views;
public sealed class SetupStep : INotifyPropertyChanged
{
    public required string Name { get; init; }
    private string _status = "In attesa", _symbol = "○";
    private Brush _color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 142, 164));
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string Symbol { get => _symbol; set { _symbol = value; Changed(); } }
    public Brush Color { get => _color; set { _color = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public partial class SetupWindow : Window
{
    private readonly ObservableCollection<SetupStep> _steps = new(InstallerService.Steps.Select(s => new SetupStep { Name = s }));
    private CancellationTokenSource? _cts;
    private bool _installed;
    public SetupWindow(string? root = null)
    {
        InitializeComponent(); App.DarkCaption(this); RootBox.Text = root ?? AppPaths.DefaultRoot; StepsList.ItemsSource = _steps;
    }
    private void Root_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Scegli la cartella dedicata a FlashVSR Desktop" }; if (dialog.ShowDialog(this) == true) RootBox.Text = Path.Combine(dialog.FolderName, "FlashVSR Desktop"); }
    private void Reuse_Click(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog { Title = "Scegli una cartella FlashVSR con ckpts" }; if (dialog.ShowDialog(this) == true) ReuseBox.Text = dialog.FolderName; }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_installed) { Process.Start(new ProcessStartInfo(Path.Combine(RootBox.Text, "FlashVSRDesktop.exe")) { UseShellExecute = true }); Close(); return; }
        _cts = new(); FolderPanel.IsEnabled = false; InstallButton.IsEnabled = false; CancelButton.Content = "Interrompi";
        foreach (var step in _steps) { step.Status = "In attesa"; step.Symbol = "○"; }
        var service = new InstallerService(update => Dispatcher.Invoke(() => {
            var step = _steps[update.Step]; step.Status = update.Done ? "Pronto" : "In corso";
            step.Symbol = update.Done ? "✓" : "●"; step.Color = update.Done ? Brushes.LightGreen : (Brush)FindResource("Accent");
            SetupStatus.Text = update.Message;
            SetupProgress.IsIndeterminate = update.Progress == null;
            SetupProgress.Value = 100 * (update.Step + (update.Progress ?? 0)) / _steps.Count;
        }), line => Dispatcher.BeginInvoke(() => { if (SetupLog.Text.Length > 70000) SetupLog.Text = SetupLog.Text[^35000..]; SetupLog.AppendText(line + Environment.NewLine); SetupLog.ScrollToEnd(); }));
        try {
            await service.InstallAsync(RootBox.Text.Trim(), ReuseBox.Text.Trim(), DesktopCheck.IsChecked == true, _cts.Token);
            _installed = true; SetupStatus.Text = "Tutto pronto. Puoi iniziare a migliorare i tuoi video."; SetupProgress.IsIndeterminate = false; SetupProgress.Value = 100; InstallButton.Content = "Apri FlashVSR";
        } catch (OperationCanceledException) { SetupStatus.Text = "Installazione interrotta. Puoi riprendere quando vuoi."; }
        catch (Exception error) { SetupStatus.Text = error.Message; SetupLog.AppendText(error + Environment.NewLine); InstallButton.Content = "Riprova"; }
        finally { _cts.Dispose(); _cts = null; InstallButton.IsEnabled = true; FolderPanel.IsEnabled = !_installed; CancelButton.Content = "Chiudi"; SetupProgress.IsIndeterminate = false; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { if (_cts != null) { _cts.Cancel(); SetupStatus.Text = "Interruzione in corso…"; } else Close(); }
    private async void Driver_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) return;
        _cts = new(); InstallButton.IsEnabled = false;
        try { await DriverService.InstallOfficialManagerAsync((message, fraction) => Dispatcher.Invoke(() => { SetupStatus.Text = message; SetupProgress.IsIndeterminate = fraction == null; if(fraction != null) SetupProgress.Value = fraction.Value * 100; }), _cts.Token); }
        catch (OperationCanceledException) { SetupStatus.Text = "Download NVIDIA interrotto."; }
        catch (Exception error) { SetupStatus.Text = error.Message; AppPaths.OpenPath("https://www.nvidia.com/en-us/software/nvidia-app/"); }
        finally { _cts.Dispose(); _cts = null; InstallButton.IsEnabled = true; SetupProgress.IsIndeterminate = false; }
    }
    private void Window_Closing(object? sender, CancelEventArgs e) { if (_cts != null) { _cts.Cancel(); e.Cancel = true; SetupStatus.Text = "Interruzione in corso. Attendi, poi chiudi la finestra."; } }
}
