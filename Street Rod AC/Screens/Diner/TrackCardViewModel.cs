using Street_Rod_AC.Models.AC;
using Street_Rod_AC.Models.Race;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Street_Rod_AC.Screens.Diner
{
    /// <summary>
    /// View model for displaying a track card in the diner screen
    /// </summary>
    public class TrackCardViewModel : INotifyPropertyChanged
    {
        public TrackInfo Track { get; set; } = new();
        public TrackConfiguration? Configuration { get; set; }

        /// <summary>
        /// Display name (configuration name or track name)
        /// </summary>
        public string DisplayName => Configuration?.Name ?? Track.Name;

        /// <summary>
        /// Track identifier
        /// </summary>
        public string TrackId => Track.TrackId;

        /// <summary>
        /// Configuration folder name (null for tracks without configurations)
        /// </summary>
        public string? ConfigurationId => Configuration?.FolderName;

        /// <summary>
        /// Path to the track preview image
        /// </summary>
        public string? PreviewPath { get; set; }

        /// <summary>
        /// Whether the track has a preview image
        /// </summary>
        public bool HasPreview => !string.IsNullOrEmpty(PreviewPath);

        /// <summary>
        /// Race type this track supports
        /// </summary>
        public RaceType RaceType { get; set; }

        private bool _isSelected;
        /// <summary>
        /// Whether this track is currently selected
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
