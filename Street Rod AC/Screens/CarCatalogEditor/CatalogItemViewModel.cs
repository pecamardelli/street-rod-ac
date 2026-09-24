using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.CarCatalogEditor
{
    /// <summary>
    /// View model for a single car catalog item in the editor
    /// Wraps CarDefinition + CarProfile for display and editing
    /// </summary>
    public class CatalogItemViewModel : ObservableObject
    {
        public CarDefinition Definition { get; }
        public CarProfile Profile { get; }

        private decimal _basePrice;
        private float _dealerPrecedence;
        private EngineOptionViewModel? _stockEngine;
        private bool _stockEngineResolved;
        private bool _isDirty;
        private readonly Lazy<IReadOnlyList<EngineOptionViewModel>> _engineOptions;
        private readonly bool _hasEngineCatalog;

        /// <param name="engineOptions">Engine builds the car could leave the factory with, best match first; empty without a parts catalog</param>
        public CatalogItemViewModel(CarDefinition definition, CarProfile profile, IReadOnlyList<EngineOptionViewModel>? engineOptions = null)
            : this(definition, profile, engineOptions is { Count: > 0 }, () => engineOptions ?? Array.Empty<EngineOptionViewModel>())
        {
        }

        /// <param name="hasEngineCatalog">There is a parts catalog to pick a factory engine from</param>
        /// <param name="engineOptions">
        /// Makes the engine builds, best match first. Called the first time the row needs them: ranking every
        /// build for every car up front is what made the editor slow to open, and a virtualized list only
        /// shows a screenful of rows.
        /// </param>
        public CatalogItemViewModel(CarDefinition definition, CarProfile profile, bool hasEngineCatalog, Func<IReadOnlyList<EngineOptionViewModel>> engineOptions)
        {
            Definition = definition;
            Profile = profile;
            _basePrice = profile.BasePrice;
            _dealerPrecedence = profile.DealerPrecedence;
            _hasEngineCatalog = hasEngineCatalog;
            _engineOptions = new Lazy<IReadOnlyList<EngineOptionViewModel>>(engineOptions, LazyThreadSafetyMode.None);
        }

        public IReadOnlyList<EngineOptionViewModel> EngineOptions => _engineOptions.Value;

        /// <summary>Whether the row offers a factory engine; answered without ranking the builds</summary>
        public bool HasEngineOptions => _hasEngineCatalog;

        /// <summary>The engine build the car leaves the factory with</summary>
        public EngineOptionViewModel? StockEngine
        {
            get
            {
                if (!_stockEngineResolved)
                {
                    _stockEngine = FindEngine(Profile.StockEngineBuildId);
                    _stockEngineResolved = true;
                }

                return _stockEngine;
            }
            set
            {
                if (ReferenceEquals(StockEngine, value)) return;

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

            // Only a row whose engine was looked at can have a new one; the others keep the profile's
            if (_stockEngineResolved && _stockEngine != null && !_stockEngine.BuildId.Equals(Profile.StockEngineBuildId, StringComparison.OrdinalIgnoreCase))
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
            _stockEngineResolved = false;
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
            _stockEngine = EngineOptions.FirstOrDefault() ?? StockEngine;
            _stockEngineResolved = true;
            OnPropertyChanged(nameof(StockEngine));
            IsDirty = true;
            OnPropertyChanged(nameof(BasePrice));
            OnPropertyChanged(nameof(BasePriceDisplay));
            OnPropertyChanged(nameof(DealerPrecedence));
            OnPropertyChanged(nameof(DealerPrecedencePercent));
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
