using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Services;

namespace Street_Rod_AC.ViewModels;

/// <summary>
/// ViewModel for the main window displaying AC content
/// </summary>
public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IAssettoCorsaContentService _contentService;
    private readonly IAssettoCorsaLauncher _launcher;
    private CarInfo? _selectedCar;
    private TrackInfo? _selectedTrack;
    private string _statusText = "Ready";

    public MainWindowViewModel(IAssettoCorsaContentService contentService, IAssettoCorsaLauncher launcher)
    {
        _contentService = contentService;
        _launcher = launcher;

        // Initialize menu commands
        NewGameCommand = new RelayCommand(NewGame);
        LoadGameCommand = new RelayCommand(LoadGame);
        ExitCommand = new RelayCommand(Exit);

        // Initialize LaunchRaceCommand
        LaunchRaceCommand = new AsyncRelayCommand(LaunchRaceAsync, CanLaunchRace);

        // Initialize View3DCommand
        View3DCommand = new RelayCommand(View3D, CanView3D);
    }

    public ObservableCollection<CarInfo> Cars { get; } = new ObservableCollection<CarInfo>();
    public ObservableCollection<TrackInfo> Tracks { get; } = new ObservableCollection<TrackInfo>();

    public CarInfo? SelectedCar
    {
        get => _selectedCar;
        set
        {
            _selectedCar = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public TrackInfo? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            _selectedTrack = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public RelayCommand NewGameCommand { get; }
    public RelayCommand LoadGameCommand { get; }
    public RelayCommand ExitCommand { get; }
    public AsyncRelayCommand LaunchRaceCommand { get; }
    public RelayCommand View3DCommand { get; }

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Loads content from the service and populates collections
    /// </summary>
    public async Task LoadContentAsync()
    {
        StatusText = "Loading content...";

        try
        {
            var loadedCars = _contentService.GetCars();
            var loadedTracks = _contentService.GetTracks();

            Console.WriteLine($"ViewModel: Got {loadedCars.Count} cars and {loadedTracks.Count} tracks from service");

            Cars.Clear();
            Tracks.Clear();

            foreach (var car in loadedCars.OrderBy(c => c.Brand).ThenBy(c => c.Name))
            {
                Cars.Add(car);
            }

            foreach (var track in loadedTracks.OrderBy(t => t.Name))
            {
                Tracks.Add(track);
            }

            Console.WriteLine($"ViewModel: Added {Cars.Count} cars and {Tracks.Count} tracks to collections");
            StatusText = $"Loaded {Cars.Count} cars and {Tracks.Count} tracks";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            Console.WriteLine($"ViewModel Error: {ex}");
        }
    }

    private bool CanLaunchRace()
    {
        return SelectedCar != null && SelectedTrack != null;
    }

    private async Task LaunchRaceAsync()
    {
        if (SelectedCar == null || SelectedTrack == null)
            return;

        try
        {
            StatusText = "Launching Assetto Corsa...";

            // For drag tracks with variants, use the first configuration if available
            string? trackConfig = null;
            if (SelectedTrack.Configurations.Count > 0)
            {
                trackConfig = SelectedTrack.Configurations[0].FolderName;
            }

            await _launcher.LaunchRaceAsync(SelectedCar, SelectedTrack, trackConfig);

            StatusText = $"Race completed with {SelectedCar.Name} at {SelectedTrack.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Launch failed: {ex.Message}";
            System.Windows.MessageBox.Show($"Failed to launch Assetto Corsa:\n\n{ex.Message}",
                "Launch Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool CanView3D()
    {
        return SelectedCar != null;
    }

    private void View3D()
    {
        if (SelectedCar == null)
            return;

        try
        {
            var rendererWindow = new CarRendererWindow(SelectedCar);
            rendererWindow.Show();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open 3D viewer: {ex.Message}";
            System.Windows.MessageBox.Show($"Failed to open 3D viewer:\n\n{ex.Message}",
                "3D Viewer Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void NewGame()
    {
        StatusText = "New Game started";
        // TODO: Implement new game logic
        System.Windows.MessageBox.Show("New Game - To be implemented",
            "New Game",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void LoadGame()
    {
        StatusText = "Load Game selected";
        // TODO: Implement load game logic
        System.Windows.MessageBox.Show("Load Game - To be implemented",
            "Load Game",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Exit()
    {
        System.Windows.Application.Current.Shutdown();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
