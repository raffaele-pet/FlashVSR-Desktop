using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FlashVsr.Desktop;
using FlashVsr.Desktop.Views;

// Integration test for the actual WPF decoder and clock, not a mock player.
// Run on Windows with Media Foundation and a packed comparison clip.
static class Program
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Length is < 1 or > 2) { Console.Error.WriteLine("Usage: PlayerTests comparison.mp4 [--inspect]"); return 2; }
        var app = new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/FlashVSRDesktop;component/Themes/Theme.xaml",UriKind.Relative) });
        var player = new ComparisonPlayer();
        var host = new ContentControl { Content=player };
        var window = new Window { Title="FlashVSR player verification", Width=960, Height=680, Content=host };
        var exit=1;
        window.Closed += (_,_) => { player.Dispose(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); };
        window.Loaded += async (_,_) => {
            if(args.Length==2 && args[1]=="--inspect") { player.Load(System.IO.Path.GetFullPath(args[0]),true,3); exit=0; return; }
            try {
                player.Load(System.IO.Path.GetFullPath(args[0]),true,3);
                var play=(Button)player.FindName("PlayButton");
                for(var i=0;i<200 && !play.IsEnabled;i++) await Task.Delay(100);
                Check(play.IsEnabled,"Media did not open within 20 seconds");
                var before=(Rectangle)player.FindName("BeforeFrame"); var after=(Rectangle)player.FindName("AfterFrame");
                var left=(DrawingGroup)((DrawingBrush)before.Fill).Drawing;
                var right=(DrawingGroup)((DrawingBrush)after.Fill).Drawing;
                var drawing=(VideoDrawing)left.Children[0];
                var media=drawing.Player; var clock=media.Clock;
                Check(ReferenceEquals(drawing,right.Children[0]),"The views do not share one decoded frame");
                var half=media.NaturalVideoWidth/2d; var height=media.NaturalVideoHeight;
                Check(((DrawingBrush)before.Fill).Viewbox==new Rect(0,0,half,height) && ((DrawingBrush)after.Fill).Viewbox==new Rect(half,0,half,height),"Comparison halves are not mapped correctly");
                Check(left.ClipGeometry.Bounds==new Rect(0,0,half,height) && right.ClipGeometry.Bounds==new Rect(half,0,half,height),"Packed frames leak into letterbox margins");
                player.TogglePlayback(); await Task.Delay(500);
                Check(clock.CurrentTime!.Value.TotalSeconds>.2,"Playback did not advance");
                ((RadioButton)player.FindName("BeforeMode")).IsChecked=true;
                await Task.Delay(100);
                ((RadioButton)player.FindName("AfterMode")).IsChecked=true;
                Check(ReferenceEquals(clock,media.Clock),"Switching view restarted playback");
                ((RadioButton)player.FindName("SlideMode")).IsChecked=true;
                Check(after.Clip is RectangleGeometry,"Slide does not clip the overlay");
                player.Pause(); await Task.Delay(100);
                var paused=clock.CurrentTime; await Task.Delay(250);
                Check(clock.CurrentTime==paused,"Pause did not stop the shared clock");
                ((Slider)player.FindName("SeekBar")).Value=1.75; await Task.Delay(200);
                Check(Math.Abs(clock.CurrentTime!.Value.TotalSeconds-1.75)<.05,"Paused seek did not move the shared clock");
                var divider=(Thumb)player.FindName("SplitThumb");
                divider.RaiseEvent(new DragDeltaEventArgs(100,0) { RoutedEvent=Thumb.DragDeltaEvent });
                Check(((RectangleGeometry)after.Clip).Rect.X>400,"Slide divider did not move");
                player.ToggleFullscreen(); await Task.Delay(200);
                Check(!ReferenceEquals(Window.GetWindow(player),window),"Full screen did not open");
                Check(ReferenceEquals(clock,media.Clock),"Full screen replaced the shared clock");
                player.ToggleFullscreen(); await Task.Delay(200);
                Check(ReferenceEquals(host.Content,player) && ReferenceEquals(clock,media.Clock),"Returning from full screen replaced the player");
                player.TogglePlayback(); await Task.Delay(250);
                Check(clock.CurrentTime!.Value.TotalSeconds>1.8,"Resume after seek did not advance");
                await Task.Delay(1800);
                Check(((TextBlock)player.FindName("PlayIcon")).Text=="\uE768","End of video did not reset the play control");
                player.TogglePlayback(); await Task.Delay(300);
                Check(clock.CurrentTime!.Value.TotalSeconds<.8,"Play at the end did not restart the video");
                player.Clear(); Check(!player.HasVideo && !play.IsEnabled,"Clear did not release playback");
                player.Load(System.IO.Path.GetFullPath(args[0]),true,3);
                await Task.Delay(30); player.Clear(); await Task.Delay(500);
                Check(!player.HasVideo && !play.IsEnabled,"A stale media-open callback re-enabled a cleared player");
                Console.WriteLine($"PASS: {_checks} real WPF player checks."); exit=0;
            } catch(Exception error) { Console.Error.WriteLine(error); }
            finally { player.Dispose(); window.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        };
        window.Show(); Dispatcher.Run(); return exit;
    }
}
