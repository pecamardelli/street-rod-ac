using System.Collections.ObjectModel;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Logic;
using Street_Rod_AC.Services.Parts;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Garage
{
    /// <summary>
    /// Working on the selected car in the garage's parts view: what the engine comes to, the part that is
    /// clicked on and taking it off, the shelf of loose parts and putting one of them on.
    /// Parts are picked in the 3D view; this is everything around it.
    /// </summary>
    public class PartsWorkbenchViewModel : ObservableObject
    {
        private const int MinutesPerPart = 5;

        private readonly ICarPartsService _parts;
        private readonly Models.GameState.GameState _gameState;
        private readonly Action _save;
        private readonly Func<int, Task> _spendMinutes;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger(LogCategory.Parts);

        private Car? _car;
        private LiveTree? _live;
        private readonly List<(int CarSlot, LiveTree Tree)> _gearTrees = new();
        private int _evaluation;

        public PartsWorkbenchViewModel(ICarPartsService parts, Models.GameState.GameState gameState, Action save, Func<int, Task> spendMinutes)
        {
            _parts = parts;
            _gameState = gameState;
            _save = save;
            _spendMinutes = spendMinutes;

            RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => SelectedPart != null);
            CancelMountCommand = new RelayCommand(() => SelectedShelfItem = null, () => SelectedShelfItem != null);
            TakeApartCommand = new RelayCommand(TakeApart, () => CanTakeApart);
        }

        /// <summary>Raised when money or time moved, for the screen around the workbench</summary>
        public event Action? StateChanged;

        /// <summary>The work is done but the game could not be saved; the screen around the workbench says so</summary>
        public event Action<Exception>? SaveFailed;

        public RelayCommand RemoveSelectedCommand { get; }
        public RelayCommand CancelMountCommand { get; }
        public RelayCommand TakeApartCommand { get; }

        #region What the 3D view shows

        private bool _isOpen;

        /// <summary>The parts view is on: the car is a shell and its parts can be worked on</summary>
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (!SetProperty(ref _isOpen, value)) return;

                if (!value)
                {
                    SelectedPart = null;
                    SelectedShelfItem = null;
                }
            }
        }

        private PartsCatalog? _catalog;

        /// <summary>Null until the parts are loaded, and for good when there are none</summary>
        public PartsCatalog? Catalog
        {
            get => _catalog;
            private set
            {
                if (SetProperty(ref _catalog, value)) OnPropertyChanged(nameof(IsAvailable));
            }
        }

        /// <summary>There are parts to work with</summary>
        public bool IsAvailable => _catalog != null;

        private InstalledPart? _engine;

        public InstalledPart? Engine
        {
            get => _engine;
            private set => SetProperty(ref _engine, value);
        }

        private IReadOnlyList<CarPart>? _gear;

        /// <summary>What sits on the car's wheel slots, each with what is on it</summary>
        public IReadOnlyList<CarPart>? Gear
        {
            get => _gear;
            private set => SetProperty(ref _gear, value);
        }

        private IReadOnlyList<MountCandidate>? _candidates;

        public IReadOnlyList<MountCandidate>? Candidates
        {
            get => _candidates;
            private set => SetProperty(ref _candidates, value);
        }

        private string _placementDisplay = string.Empty;

        /// <summary>What the viewport's placement mode is doing, shown over the part card; empty when it is off</summary>
        public string PlacementDisplay
        {
            get => _placementDisplay;
            set => SetProperty(ref _placementDisplay, value);
        }

        private InstalledPart? _selectedPart;

        public InstalledPart? SelectedPart
        {
            get => _selectedPart;
            set
            {
                if (!SetProperty(ref _selectedPart, value)) return;

                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedName));
                OnPropertyChanged(nameof(SelectedKind));
                OnPropertyChanged(nameof(SelectedCondition));
                OnPropertyChanged(nameof(SelectedConditionDisplay));
                OnPropertyChanged(nameof(SelectedWorthDisplay));
                OnPropertyChanged(nameof(SelectedAttachedDisplay));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        #endregion

        #region Engine sheet

        private EngineReport? _report;

        public bool HasEngine => _engine != null;

        public string EngineName =>
            _engine == null ? "No engine" : (_engine.Definition.DisplayName ?? _engine.Definition.Name);

        public string PowerDisplay => _report?.Dyno is { MaxPowerHp: > 0 } dyno ? $"{dyno.MaxPowerHp:0} hp @ {dyno.MaxPowerRpm:0}" : "-";
        public string TorqueDisplay => _report?.Dyno is { MaxTorque: > 0 } dyno ? $"{dyno.MaxTorque:0} Nm @ {dyno.MaxTorqueRpm:0}" : "-";

        public string DisplacementDisplay => _report?.Dyno is { Displacement: > 0 } dyno
            ? $"{dyno.Displacement * 1000:0.0} l ({dyno.Displacement * 61023.7:0} cui), {dyno.Compression:0.0}:1"
            : "-";

        public string GearsDisplay => _report is { GearRatios.Count: > 0 } report
            ? $"{report.GearRatios.Count} speed, final {report.FinalRatio:0.00}"
            : "No transmission";

        public string MassDisplay => _report == null ? "-" : $"{_report.Mass:0} kg";

        /// <summary>Why the engine does not run, in the part scripts' own words; empty when it does</summary>
        public string StatusDisplay => _engine == null
            ? "The engine bay is empty."
            : _report == null
                ? "Checking the engine..."
                : _report.Runs ? string.Empty : Capitalize(_report.Problem ?? "the engine does not run.");

        #endregion

        #region Selected part

        public bool HasSelection => _selectedPart != null;
        public string SelectedName => _selectedPart?.Definition.DisplayName ?? _selectedPart?.Definition.Name ?? string.Empty;
        public string SelectedKind => _selectedPart == null ? string.Empty : PartKinds.GroupOf(_selectedPart.Definition);
        public double SelectedCondition => _selectedPart?.Wear ?? 0;
        public string SelectedConditionDisplay => _selectedPart == null
            ? string.Empty
            : $"{_selectedPart.Wear * 100:0}%" + (_selectedPart.Tear < 0.995 ? $", damaged ({_selectedPart.Tear * 100:0}% left)" : string.Empty);

        public string SelectedWorthDisplay => _selectedPart != null && SavedOf(_selectedPart) is { } saved
            ? $"${PartPricing.Round(PartPricing.Worth(_selectedPart.Definition, saved)):N0}"
            : string.Empty;

        public string SelectedAttachedDisplay
        {
            get
            {
                var attached = _selectedPart == null ? 0 : _selectedPart.SelfAndDescendants().Count() - 1;
                return attached switch
                {
                    0 => string.Empty,
                    1 => "Comes off with the part that is on it.",
                    _ => $"Comes off with the {attached} parts that are on it."
                };
            }
        }

        #endregion

        #region Shelf

        public ObservableCollection<ShelfItemViewModel> Shelf { get; } = new();

        public bool HasShelfItems => Shelf.Count > 0;

        private ShelfItemViewModel? _selectedShelfItem;

        /// <summary>The loose part picked to go on the car; the 3D view shows where it fits</summary>
        public ShelfItemViewModel? SelectedShelfItem
        {
            get => _selectedShelfItem;
            set
            {
                if (!SetProperty(ref _selectedShelfItem, value)) return;

                if (value != null) SelectedPart = null;
                OnPropertyChanged(nameof(CanTakeApart));
                UpdateCandidates();
                RelayCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The picked shelf part has parts on it that can come off right there on the shelf</summary>
        public bool CanTakeApart => _selectedShelfItem is { Part.Children.Count: > 0 };

        private string _shelfHint = string.Empty;

        public string ShelfHint
        {
            get => _shelfHint;
            private set => SetProperty(ref _shelfHint, value);
        }

        #endregion

        /// <summary>The car being worked on; null for an empty garage</summary>
        public async void SetCar(Car? car)
        {
            _car = car;
            SelectedPart = null;
            SelectedShelfItem = null;

            try
            {
                // The first call loads the catalog: keep that off the UI thread. The car and the save file
                // belong to the UI thread, so a car without parts gets them, and is saved, back on it.
                var available = await Task.Run(() => _parts.IsAvailable);
                if (available && car != null && await _parts.EnsurePartsAsync(car)) Save();

                if (!ReferenceEquals(_car, car)) return;

                Catalog = available ? _parts.Catalog : null;
                if (!available) IsOpen = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not get the parts of {Car}", car?.DefinitionId ?? "(none)");
            }

            Refresh();
        }

        public void OnPartClicked(InstalledPart? part)
        {
            if (SelectedShelfItem != null && part == null) return;

            SelectedShelfItem = null;
            SelectedPart = InCurrentTree(part);
        }

        /// <summary>
        /// The tree is made anew after every change, and the 3D view goes on showing the one before until the
        /// new model is built: a click in between brings a part of the old tree. The same part of the tree
        /// that counts now is known by its id; a part that has come off in the meantime is nothing to select.
        /// </summary>
        private InstalledPart? InCurrentTree(InstalledPart? part)
        {
            if (part == null || SavedOf(part) != null) return part;

            return part.InstanceId == Guid.Empty
                ? part
                : Trees.SelectMany(t => t.Root.SelfAndDescendants()).FirstOrDefault(p => p.InstanceId == part.InstanceId);
        }

        /// <summary>Every tree on the car: the engine and the running gear</summary>
        private IEnumerable<LiveTree> Trees => (_live == null ? Enumerable.Empty<LiveTree>() : new[] { _live }).Concat(_gearTrees.Select(g => g.Tree));

        /// <summary>The saved part behind a part on screen, whichever tree of the car it is in</summary>
        private PartInstance? SavedOf(InstalledPart part) => Trees.Select(t => t.Saved.GetValueOrDefault(part)).FirstOrDefault(p => p != null);

        public void OnCandidateClicked(MountCandidate candidate)
        {
            if (_car == null || candidate.Tag is not MountPlace place || SelectedShelfItem is not { } item) return;

            // The 3D view shows the places of the part picked before until those of this one are built:
            // a place is only good for the part it was found for
            if (_candidates?.Any(c => ReferenceEquals(c, candidate)) != true) return;

            Workbench.Mount(_car.Parts, place, item.Part);
            _gameState.Player.Parts.Remove(item.Part);
            _logger.Information("Mounted {Part} on {Car}", item.Part.DefinitionId, _car.DefinitionId);

            SelectedShelfItem = null;
            Finish(item.Part.SelfAndDescendants().Count());
        }

        private void RemoveSelected()
        {
            if (_car == null || _selectedPart == null || SavedOf(_selectedPart) is not { } saved) return;
            if (!Workbench.Remove(_car.Parts, saved)) return;

            _gameState.Player.Parts.Add(saved);
            _logger.Information("Took {Part} off {Car}", saved.DefinitionId, _car.DefinitionId);

            SelectedPart = null;
            Finish(saved.SelfAndDescendants().Count());
        }

        /// <summary>What is mounted on the picked shelf part comes off and lies next to it, each piece with what is on it in turn</summary>
        private void TakeApart()
        {
            if (_selectedShelfItem is not { } item || item.Part.Children.Count == 0) return;

            var pieces = item.Part.Children.ToList();
            item.Part.Children.Clear();

            var shelf = _gameState.Player.Parts;
            var index = shelf.IndexOf(item.Part);
            foreach (var piece in pieces)
            {
                piece.ParentSlot = 0;
                piece.OwnSlot = 0;
                shelf.Insert(++index, piece);
            }

            _logger.Information("Took {Count} part(s) off {Part} on the shelf", pieces.Count, item.Part.DefinitionId);
            SelectedShelfItem = null;
            Finish(pieces.Count);
        }

        /// <summary>After the car changed: show it, let the clock run for the work, and save it all</summary>
        private async void Finish(int partsHandled)
        {
            Refresh();

            try
            {
                // Before saving: the time the work took belongs in the save, and so does the new day it may end in
                await _spendMinutes(MinutesPerPart * Math.Min(partsHandled, 6));
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not spend the time for the work: {Error}", ex.Message);
            }

            Save();

            try
            {
                StateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The garage could not catch up with the work on the car");
            }
        }

        /// <summary>Nothing above this catches: a save that fails must not take the game down with it</summary>
        private void Save()
        {
            try
            {
                _save();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not save the game after working on the car");
                SaveFailed?.Invoke(ex);
            }
        }

        /// <summary>Brings the saved tree to life again, fills the shelf, and puts the engine on the dyno</summary>
        private async void Refresh()
        {
            // Everything in here runs on a saved tree the catalog may no longer be able to rebuild (a pack that
            // changed since the save): whatever throws is logged, and the garage stays up without the sheet
            try
            {
                var catalog = Catalog;
                _live = catalog != null && _car?.Engine is { } engine ? PartTrees.ToInstalled(catalog, engine) : null;
                Engine = _live?.Root;
                _report = null;

                _gearTrees.Clear();
                if (catalog != null && _car != null)
                {
                    foreach (var part in _car.Parts.Where(p => RunningGear.CornerOf(p.ParentSlot) >= 0))
                    {
                        if (PartTrees.ToInstalled(catalog, part) is { } tree) _gearTrees.Add((part.ParentSlot, tree));
                    }
                }

                Gear = _gearTrees.Select(g => new CarPart(g.CarSlot, g.Tree.Root)).ToList();

                Shelf.Clear();
                if (catalog != null)
                {
                    foreach (var part in _gameState.Player.Parts)
                    {
                        if (catalog.Get(part.DefinitionId) is { } definition) Shelf.Add(new ShelfItemViewModel(part, definition, catalog));
                    }
                }

                OnPropertyChanged(nameof(HasShelfItems));
                NotifySheet();

                var live = _live;
                if (catalog == null || live == null) return;

                var evaluation = ++_evaluation;
                var car = _car;

                // On a tree of its own: the one on screen belongs to the UI thread
                var saved = live.Saved[live.Root];
                var report = await Task.Run(() => EngineFactory.Evaluate(catalog, saved));
                if (evaluation != _evaluation) return;

                _report = report;

                // The car's dyno figure, as the rivals' is kept: the diner's matchup reads it, and the next save keeps it
                if (car != null) car.PowerHp = Services.Market.UsedCarMarketService.PowerOf(report);
                NotifySheet();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not show the parts of {Car}", _car?.DefinitionId ?? "(none)");
            }
        }

        private void UpdateCandidates()
        {
            var catalog = Catalog;
            var item = SelectedShelfItem;
            if (catalog == null || item == null || _car == null)
            {
                Candidates = null;
                ShelfHint = string.Empty;
                return;
            }

            var places = Workbench.FindPlaces(catalog, _car.Parts, item.Part);
            var installedOf = Trees.SelectMany(t => t.Saved).ToDictionary(p => p.Value, p => p.Key);

            var candidates = new List<MountCandidate>();
            foreach (var place in places)
            {
                // A tree of its own for every place: the view tells them apart by their parts
                var loose = PartTrees.ToInstalled(catalog, item.Part);
                var parent = place.Parent == null ? null : installedOf.GetValueOrDefault(place.Parent);
                if (loose == null || (place.Parent != null && parent == null)) continue;

                candidates.Add(new MountCandidate(loose.Root, parent, place.ParentSlot, place.OwnSlot, place));
            }

            Candidates = candidates;
            ShelfHint = candidates.Count switch
            {
                0 => "It does not fit anywhere on this car as it is.",
                1 => "Click the glowing part in the car to mount it.",
                _ => $"It fits in {candidates.Count} places: click the one you want."
            };
        }

        private void NotifySheet()
        {
            OnPropertyChanged(nameof(HasEngine));
            OnPropertyChanged(nameof(EngineName));
            OnPropertyChanged(nameof(PowerDisplay));
            OnPropertyChanged(nameof(TorqueDisplay));
            OnPropertyChanged(nameof(DisplacementDisplay));
            OnPropertyChanged(nameof(GearsDisplay));
            OnPropertyChanged(nameof(MassDisplay));
            OnPropertyChanged(nameof(StatusDisplay));
        }

        private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>A loose part on the shelf, with whatever is still mounted on it</summary>
    public class ShelfItemViewModel
    {
        public ShelfItemViewModel(PartInstance part, PartDefinition definition, PartsCatalog catalog)
        {
            Part = part;
            Name = definition.DisplayName ?? definition.Name;
            Kind = PartKinds.GroupOf(definition);

            Detail = PartTrees.Describe(part);
            WorthDisplay = $"${PartPricing.Round(PartPricing.WorthOfAssembly(catalog, part)):N0}";
        }

        public PartInstance Part { get; }
        public string Name { get; }
        public string Kind { get; }
        public string Detail { get; }
        public string WorthDisplay { get; }
    }
}
