using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Services;

namespace Street_Rod_AC.ViewModels;

/// <summary>
/// ViewModel for the main window displaying AC content
/// </summary>
public class MainWindowViewModel(IAssettoCorsaContentService contentService) : INotifyPropertyChanged
{
    private readonly IAssettoCorsaContentService _contentService = contentService;
    private CarInfo? _selectedCar;
    private TrackInfo? _selectedTrack;
    private string _statusText = "Ready";

    public ObservableCollection<CarInfo> Cars { get; } = new ObservableCollection<CarInfo>();
    public ObservableCollection<TrackInfo> Tracks { get; } = new ObservableCollection<TrackInfo>();

    public CarInfo? SelectedCar
    {
        get => _selectedCar;
        set
        {
            _selectedCar = value;
            OnPropertyChanged();
        }
    }

    public TrackInfo? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            _selectedTrack = value;
            OnPropertyChanged();
        }
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
