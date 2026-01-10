using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Information;

namespace Street_Rod_AC;

public partial class ShowroomOverlay : Window
{
    private readonly DialogService _dialogService;
    private Process? _showroomProcess;
    private DispatcherTimer? _positionTimer;
    private IntPtr _showroomWindowHandle = IntPtr.Zero;

    // Windows API imports
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public ShowroomOverlay()
    {
        InitializeComponent();

        var app = (App)System.Windows.Application.Current;
        _dialogService = app.DialogService;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Launch AC Showroom
            LaunchShowroom();

            // Start timer to track showroom window position
            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _positionTimer.Tick += PositionTimer_Tick;
            _positionTimer.Start();
        }
        catch (Exception ex)
        {
            var errorDialog = new InformationDialogViewModel(
                _dialogService,
                $"Failed to launch showroom:\n\n{ex.Message}",
                "Error");
            _dialogService.ShowDialog(errorDialog);
            Close();
        }
    }

    private void LaunchShowroom()
    {
        // Start the showroom process in windowed mode
        _showroomProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = @"C:\GAMES\Street Rod AC\acShowroom.exe",
                Arguments = "--windowed",  // Launch in windowed mode so overlay can appear on top
                WorkingDirectory = @"C:\GAMES\Street Rod AC",
                UseShellExecute = false,
                CreateNoWindow = false
            }
        };

        _showroomProcess.Start();
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        // Try to find the showroom window if we don't have it yet
        if (_showroomWindowHandle == IntPtr.Zero)
        {
            // Try to find the main window by process
            if (_showroomProcess != null && !_showroomProcess.HasExited)
            {
                _showroomProcess.Refresh();
                if (_showroomProcess.MainWindowHandle != IntPtr.Zero)
                {
                    _showroomWindowHandle = _showroomProcess.MainWindowHandle;
                }
            }
        }

        // Update overlay position if we have the showroom window
        if (_showroomWindowHandle != IntPtr.Zero && IsWindow(_showroomWindowHandle))
        {
            if (GetWindowRect(_showroomWindowHandle, out RECT rect))
            {
                // Position overlay to match showroom window
                Left = rect.Left;
                Top = rect.Top;
                Width = rect.Right - rect.Left;
                Height = rect.Bottom - rect.Top;
            }
        }
        else
        {
            // Showroom window is gone, close overlay
            if (_showroomProcess != null && _showroomProcess.HasExited)
            {
                Close();
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseShowroom();
    }

    private void CloseShowroom()
    {
        try
        {
            if (_showroomProcess != null && !_showroomProcess.HasExited)
            {
                _showroomProcess.Kill();
                _showroomProcess.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            var errorDialog = new InformationDialogViewModel(
                _dialogService,
                $"Failed to close showroom:\n\n{ex.Message}",
                "Error");
            _dialogService.ShowDialog(errorDialog);
        }
        finally
        {
            Close();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _positionTimer?.Stop();

        // Close showroom if still running
        if (_showroomProcess != null && !_showroomProcess.HasExited)
        {
            try
            {
                _showroomProcess.Kill();
            }
            catch
            {
                // Ignore errors on cleanup
            }
        }

        _showroomProcess?.Dispose();
    }
}
