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
        private EngineOptionViewModel? _stockEngine;
        private bool _isDirty;

        /// <param name="engineOptions">Engine builds the car could leave the factory with, best match first; empty without a parts catalog</param>
        public CatalogItemViewModel(CarDefinition definition, CarProfile profile, IReadOnlyList<EngineOptionViewModel>? engineOptions = null)
        {
            Definition = definition;
            Profile = profile;
            _basePrice = profile.BasePrice;
            _dealerPrecedence = profile.DealerPrecedence;
            EngineOptions = engineOptions ?? Array.Empty<EngineOptionViewModel>();
            _stockEngine = FindEngine(profile.StockEngineBuildId);
        }

        public IReadOnlyList<EngineOptionViewModel> EngineOptions { get; }

        public bool HasEngineOptions => EngineOptions.Count > 0;

        /// <summary>The engine build the car leaves the factory with</summary>
        public EngineOptionViewModel? StockEngine
        {
            get => _stockEngine;
            set
            {
                if (ReferenceEquals(_stockEngine, value)) return;

                _stockEngine = value;
                IsDirty = true;
                OnPropertyChanged();
            }
        }

        private EngineOptionViewModel? FindEngine(string? buildId) =>
            EngineOptions.FirstOrDefault(o => o.BuildId.Equals(buildId, StringComparison.OrdinalIgnoreCase));

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
            if (_stockEngine != null && !_stockEngine.BuildId.Equals(Profile.StockEngineBuildId, StringComparison.OrdinalIgnoreCase))
            {
                Profile.StockEngineBuildId = _stockEngine.BuildId;
                Profile.StockEngineIsManual = true;
            }

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
            _stockEngine = FindEngine(Profile.StockEngineBuildId);
            IsDirty = false;
            OnPropertyChanged(nameof(StockEngine));
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

            // The options come best match first: the first one is what a fresh profile would get
            _stockEngine = EngineOptions.FirstOrDefault() ?? _stockEngine;
            OnPropertyChanged(nameof(StockEngine));
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

    /// <summary>An engine build on offer as a car's factory engine</summary>
    public class EngineOptionViewModel
    {
        public EngineOptionViewModel(string buildId, string label)
        {
            BuildId = buildId;
            Label = label;
        }

        public string BuildId { get; }

        /// <summary>E.g. "Chevrolet Camaro COPO 427 '69  ·  659 hp, 7.0 l"</summary>
        public string Label { get; }
    }
}
