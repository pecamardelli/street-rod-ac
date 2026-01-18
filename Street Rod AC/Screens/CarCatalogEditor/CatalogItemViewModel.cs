using System.ComponentModel;
using System.Runtime.CompilerServices;
using Street_Rod_AC.Models.Catalog;

namespace Street_Rod_AC.Screens.CarCatalogEditor
{
    /// <summary>
    /// View model for a single car catalog item in the editor
    /// Wraps CarDefinition + CarProfile for display and editing
    /// </summary>
    public class CatalogItemViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public CarDefinition Definition { get; }
        public CarProfile Profile { get; }

        private decimal _basePrice;
        private float _dealerPrecedence;
        private bool _isDirty;

        public CatalogItemViewModel(CarDefinition definition, CarProfile profile)
        {
            Definition = definition;
            Profile = profile;
            _basePrice = profile.BasePrice;
            _dealerPrecedence = profile.DealerPrecedence;
        }

        public string DisplayName => $"{Definition.Brand} {Definition.Name}";
        public string YearDisplay => Definition.Year?.ToString() ?? "Unknown";
        public string BrandDisplay => Definition.Brand;

        public decimal BasePrice
        {
            get => _basePrice;
            set
            {
                if (_basePrice != value)
                {
                    _basePrice = value;
                    IsDirty = true;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BasePriceDisplay));
                }
            }
        }

        public string BasePriceDisplay => $"${_basePrice:N0}";

        public float DealerPrecedence
        {
            get => _dealerPrecedence;
            set
            {
                var clamped = Math.Clamp(value, 0.0f, 1.0f);
                if (_dealerPrecedence != clamped)
                {
                    _dealerPrecedence = clamped;
                    IsDirty = true;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DealerPrecedencePercent));
                }
            }
        }

        public int DealerPrecedencePercent
        {
            get => (int)(_dealerPrecedence * 100);
            set
            {
                DealerPrecedence = value / 100f;
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Applies changes to the underlying profile
        /// </summary>
        public void ApplyChanges()
        {
            Profile.BasePrice = _basePrice;
            Profile.DealerPrecedence = _dealerPrecedence;
            Profile.LastUpdatedDate = DateTime.Now;
            Profile.Source = ProfileDataSource.Manual;
            IsDirty = false;
        }

        /// <summary>
        /// Resets to the current profile values
        /// </summary>
        public void ResetToProfile()
        {
            _basePrice = Profile.BasePrice;
            _dealerPrecedence = Profile.DealerPrecedence;
            IsDirty = false;
            OnPropertyChanged(nameof(BasePrice));
            OnPropertyChanged(nameof(BasePriceDisplay));
            OnPropertyChanged(nameof(DealerPrecedence));
            OnPropertyChanged(nameof(DealerPrecedencePercent));
        }

        /// <summary>
        /// Sets values from a regenerated profile (for reset to defaults)
        /// </summary>
        public void SetFromGeneratedProfile(CarProfile generatedProfile)
        {
            _basePrice = generatedProfile.BasePrice;
            _dealerPrecedence = generatedProfile.DealerPrecedence;
            IsDirty = true;
            OnPropertyChanged(nameof(BasePrice));
            OnPropertyChanged(nameof(BasePriceDisplay));
            OnPropertyChanged(nameof(DealerPrecedence));
            OnPropertyChanged(nameof(DealerPrecedencePercent));
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
