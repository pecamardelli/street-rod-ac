# Career UI Implementation Plan

This document details the implementation of the Career screen and Events integration for Street Rod AC.

## Overview

Two main UI additions:
1. **Career Screen** - View victory progress, milestones, and stats (accessible from Game screen)
2. **Race Invitations in Newspaper** - Browse and enter special events with car requirements

## Architecture Summary

```
Game Screen
    ├── Newspaper → Race Invitations (events list)
    ├── Garage
    ├── Career (NEW) → Victory progress, milestones, stats
    └── Hit the Streets → Diner
```

---

## Part 1: Career Screen

### Files to Create

```
Screens/Career/
├── CareerScreenView.xaml
├── CareerScreenView.xaml.cs
├── CareerScreenViewModel.cs
├── VictoryDisplayViewModel.cs      # VM for victory condition display
└── MilestoneDisplayViewModel.cs    # VM for milestone display
```

### CareerScreenViewModel

```csharp
public class CareerScreenViewModel : BaseScreenViewModel
{
    // Dependencies
    private readonly NavigationService _navigationService;
    private readonly IVictoryConditionService _victoryService;
    private readonly IMilestoneService _milestoneService;
    private readonly GameState _gameState;

    // Commands
    public RelayCommand BackCommand { get; }
    public RelayCommand<string> SetActiveVictoryCommand { get; }

    // Victory Display
    public ObservableCollection<VictoryDisplayViewModel> Victories { get; }
    public VictoryDisplayViewModel? ActiveVictory { get; }

    // Milestone Display
    public ObservableCollection<MilestoneDisplayViewModel> Milestones { get; }
    public int CompletedMilestoneCount { get; }
    public int TotalMilestoneCount { get; }

    // Stats Display
    public int TotalWins => _gameState.Player.Stats.Wins;
    public int TotalLosses => _gameState.Player.Stats.Losses;
    public int Reputation => _gameState.Player.Stats.Reputation;
    public string ReputationTier => _gameState.Player.Stats.GetReputationTier();
    public int CarsOwned => _gameState.Player.Cars.Count;
    public int PinkSlipsWon => _gameState.Player.Stats.PinkSlipsWon;
    public int OpponentsDefeated => _gameState.Career.DefeatedOpponentIds.Count;
    public decimal TotalEarnings => _gameState.Player.Stats.TotalEarnings;

    // Bankroll
    public string BankrollDisplay => $"${_gameState.Player.Money:N0}";
}
```

### VictoryDisplayViewModel

```csharp
public class VictoryDisplayViewModel : INotifyPropertyChanged
{
    public string VictoryType { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    // State
    public bool IsUnlocked { get; set; }
    public bool IsActive { get; set; }      // Player is pursuing this
    public bool IsAchieved { get; set; }    // Already won via this

    // Progress
    public float ProgressPercentage { get; set; }
    public string ProgressText { get; set; }
    public List<string> CompletedSteps { get; set; }
    public List<string> RemainingSteps { get; set; }

    // Visual
    public string StatusIcon => IsAchieved ? "✓" : IsActive ? "★" : IsUnlocked ? "○" : "🔒";
    public string StatusColor => IsAchieved ? "#90EE90" : IsActive ? "#FFD700" : IsUnlocked ? "#FFFFFF" : "#808080";
    public double Opacity => IsUnlocked ? 1.0 : 0.5;
}
```

### MilestoneDisplayViewModel

```csharp
public class MilestoneDisplayViewModel : INotifyPropertyChanged
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Category { get; set; }

    // Progress
    public bool IsCompleted { get; set; }
    public int CurrentValue { get; set; }
    public int TargetValue { get; set; }
    public float ProgressPercentage { get; set; }
    public string ProgressText { get; set; }  // "5/10" or "Completed!"

    // Visual
    public string StatusIcon => IsCompleted ? "✓" : "○";
    public string StatusColor => IsCompleted ? "#90EE90" : "#FFFFFF";
}
```

### Career Screen Layout (XAML Structure)

```xml
<UserControl x:Class="Street_Rod_AC.Screens.Career.CareerScreenView">
    <Grid Background="{StaticResource MainBackgroundBrush}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- Header -->
            <RowDefinition Height="*"/>      <!-- Content -->
            <RowDefinition Height="Auto"/>  <!-- Bottom bar -->
        </Grid.RowDefinitions>

        <!-- Header with Back button and Title -->
        <Grid Row="0">
            <Button Style="{StaticResource ExitButtonStyle}"
                    Command="{Binding BackCommand}"/>
            <TextBlock Text="CAREER" HorizontalAlignment="Center"/>
        </Grid>

        <!-- Main Content -->
        <Grid Row="1" Margin="20">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="2*"/>  <!-- Victories -->
                <ColumnDefinition Width="1*"/>  <!-- Stats -->
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="2*"/>    <!-- Victories -->
                <RowDefinition Height="1*"/>    <!-- Milestones -->
            </Grid.RowDefinitions>

            <!-- Victory Progress Panel -->
            <Border Grid.Column="0" Grid.Row="0">
                <StackPanel>
                    <TextBlock Text="VICTORY PROGRESS"/>
                    <ItemsControl ItemsSource="{Binding Victories}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <!-- Victory card with progress bar -->
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>

            <!-- Stats Panel -->
            <Border Grid.Column="1" Grid.Row="0">
                <StackPanel>
                    <TextBlock Text="YOUR STATS"/>
                    <!-- Stats list -->
                </StackPanel>
            </Border>

            <!-- Milestones Panel -->
            <Border Grid.Column="0" Grid.ColumnSpan="2" Grid.Row="1">
                <StackPanel>
                    <TextBlock Text="MILESTONES"/>
                    <WrapPanel>
                        <!-- Milestone chips/badges -->
                    </WrapPanel>
                </StackPanel>
            </Border>
        </Grid>

        <!-- Bottom Bar -->
        <bottomBar:BottomBarView Grid.Row="2"
                                  BankrollDisplay="{Binding BankrollDisplay}"/>
    </Grid>
</UserControl>
```

### Navigation Integration

**NavigationService.cs** - Add method:

```csharp
public void NavigateToCareer(GameState gameState)
{
    var app = (App)Application.Current;
    var viewModel = new CareerScreenViewModel(
        this,
        app.VictoryConditionService,
        app.MilestoneService,
        gameState
    );
    Navigate(new CareerScreenView { DataContext = viewModel });
}
```

**GameScreenViewModel.cs** - Add command:

```csharp
public RelayCommand CareerCommand { get; }

// In constructor:
CareerCommand = new RelayCommand(OnCareer);

private void OnCareer()
{
    _navigationService.NavigateToCareer(_gameState);
}
```

**GameScreenView.xaml** - Add button:

```xml
<!-- Add alongside Newspaper and Garage buttons -->
<Button Style="{StaticResource GameButtonStyle}"
        Background="{StaticResource CareerButtonBrush}"
        Command="{Binding CareerCommand}"
        Tag="View career"/>
```

---

## Part 2: Race Invitations in Newspaper

### Files to Create/Modify

```
Screens/Newspaper/
├── NewspaperScreenView.xaml          # MODIFY: Add events section
├── NewspaperScreenViewModel.cs       # MODIFY: Add event loading
└── EventInvitationViewModel.cs       # NEW: VM for event display

Dialogs/EventEntry/
├── EventEntryDialogView.xaml         # NEW: Car selection for event
├── EventEntryDialogView.xaml.cs
└── EventEntryDialogViewModel.cs
```

### EventInvitationViewModel

```csharp
public class EventInvitationViewModel : INotifyPropertyChanged
{
    // Event definition
    public string EventId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string RequirementsDescription { get; set; }  // "Ford only", "300+ HP American"
    public string RewardDescription { get; set; }        // "$750 + 10 rep"
    public RaceType RaceType { get; set; }
    public bool IsPinkSlip { get; set; }

    // Instance state
    public DateTime? ExpiresAt { get; set; }
    public string ExpiresInText { get; set; }  // "5 days", "12 hours"
    public bool IsExpiringSoon { get; set; }   // < 24 hours

    // Eligibility
    public bool HasEligibleCars { get; set; }
    public List<EligibleCarViewModel> EligibleCars { get; set; }
    public string EligibilityText { get; set; }  // "2 cars eligible" or "No eligible cars"

    // Visual
    public double Opacity => HasEligibleCars ? 1.0 : 0.5;
    public bool CanEnter => HasEligibleCars;
}

public class EligibleCarViewModel
{
    public Guid InstanceId { get; set; }
    public string DisplayName { get; set; }  // "'69 Camaro SS"
    public string ConditionText { get; set; } // "Good condition"
}
```

### NewspaperScreenViewModel Changes

```csharp
public class NewspaperScreenViewModel : BaseScreenViewModel
{
    // Existing...

    // NEW: Event-related
    private readonly IRaceEventService _eventService;
    private readonly ICarFilterService _filterService;
    private readonly IContentCatalogRepository _catalogRepository;

    public ObservableCollection<EventInvitationViewModel> RaceInvitations { get; }
    public bool HasRaceInvitations => RaceInvitations.Count > 0;
    public RelayCommand<EventInvitationViewModel> EnterEventCommand { get; }
    public RelayCommand ViewAllEventsCommand { get; }  // Optional: separate events screen

    private void LoadRaceInvitations()
    {
        RaceInvitations.Clear();

        var activeEvents = _eventService.GetActiveEvents(
            _gameState.Career,
            _gameState.Date
        );

        foreach (var eventState in activeEvents)
        {
            var definition = _eventService.GetEventDefinition(eventState.EventDefinitionId);
            if (definition == null) continue;

            // Find eligible cars
            var eligibleCars = FindEligibleCars(definition);

            var vm = new EventInvitationViewModel
            {
                EventId = definition.Id,
                Name = definition.Name,
                Description = definition.Description,
                RequirementsDescription = definition.GetEntryRequirementsDescription(),
                RewardDescription = definition.Reward.GetDescription(),
                RaceType = definition.RaceType,
                IsPinkSlip = definition.IsPinkSlip,
                ExpiresAt = eventState.ExpiresAt,
                ExpiresInText = CalculateExpiresIn(eventState.ExpiresAt),
                HasEligibleCars = eligibleCars.Any(),
                EligibleCars = eligibleCars,
                EligibilityText = eligibleCars.Any()
                    ? $"{eligibleCars.Count} car(s) eligible"
                    : "No eligible cars"
            };

            RaceInvitations.Add(vm);
        }
    }

    private List<EligibleCarViewModel> FindEligibleCars(RaceEventDefinition definition)
    {
        var eligible = new List<EligibleCarViewModel>();

        foreach (var car in _gameState.Player.Cars)
        {
            var carDef = _catalogRepository.GetCar(car.DefinitionId);
            if (carDef == null) continue;

            // Check filter (if any)
            if (definition.EntryRequirements != null)
            {
                if (!_filterService.Matches(definition.EntryRequirements, carDef, car))
                    continue;
            }

            eligible.Add(new EligibleCarViewModel
            {
                InstanceId = car.InstanceId,
                DisplayName = $"{carDef.Brand} {carDef.Name}",
                ConditionText = GetConditionText(car)
            });
        }

        return eligible;
    }

    private void OnEnterEvent(EventInvitationViewModel eventVm)
    {
        if (!eventVm.CanEnter) return;

        // Show event entry dialog with car selection
        var dialog = new EventEntryDialogViewModel(
            _dialogService,
            eventVm,
            _gameState,
            OnEventEntryComplete
        );
        _dialogService.ShowDialog(dialog);
    }
}
```

### EventEntryDialogViewModel

Similar to ChallengeSetupDialog but for events:

```csharp
public class EventEntryDialogViewModel : BaseDialogViewModel
{
    // Event info
    public string EventName { get; }
    public string EventDescription { get; }
    public string Requirements { get; }
    public string Reward { get; }
    public bool IsPinkSlip { get; }

    // Car selection
    public ObservableCollection<EligibleCarViewModel> EligibleCars { get; }
    public EligibleCarViewModel? SelectedCar { get; set; }

    // Opponent info (assigned when entering)
    public string OpponentName { get; }
    public string OpponentCarDisplay { get; }
    public int OpponentReputation { get; }

    // Commands
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    // Result
    public Action<EventEntryResult?> OnComplete { get; }
}

public class EventEntryResult
{
    public string EventId { get; set; }
    public Guid PlayerCarInstanceId { get; set; }
    public Opponent Opponent { get; set; }
    public Car OpponentCar { get; set; }
}
```

### Newspaper XAML Changes

Add events section to NewspaperScreenView.xaml:

```xml
<!-- Existing: Used Parts / Used Cars buttons -->

<!-- NEW: Race Invitations Section -->
<Border Visibility="{Binding HasRaceInvitations, Converter={StaticResource BoolToVisibility}}"
        Background="#2A2A2A"
        CornerRadius="8"
        Padding="15"
        Margin="20">
    <StackPanel>
        <TextBlock Text="RACE INVITATIONS"
                   FontSize="18"
                   FontWeight="Bold"
                   Foreground="{StaticResource TextPrimaryBrush}"
                   Margin="0,0,0,10"/>

        <ItemsControl ItemsSource="{Binding RaceInvitations}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Background="#363636"
                            CornerRadius="6"
                            Padding="12"
                            Margin="0,0,0,8"
                            Opacity="{Binding Opacity}">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>

                            <StackPanel Grid.Column="0">
                                <!-- Event name and expiry -->
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="{Binding Name}"
                                               FontWeight="Bold"
                                               FontSize="16"/>
                                    <TextBlock Text="{Binding ExpiresInText}"
                                               Foreground="#FFA500"
                                               Margin="15,0,0,0"/>
                                </StackPanel>

                                <!-- Requirements -->
                                <TextBlock Text="{Binding RequirementsDescription}"
                                           Foreground="#AAAAAA"
                                           Margin="0,4,0,0"/>

                                <!-- Reward -->
                                <TextBlock Text="{Binding RewardDescription}"
                                           Foreground="#90EE90"
                                           Margin="0,4,0,0"/>

                                <!-- Eligibility -->
                                <TextBlock Text="{Binding EligibilityText}"
                                           Foreground="{Binding HasEligibleCars,
                                               Converter={StaticResource EligibilityColorConverter}}"
                                           FontStyle="Italic"
                                           Margin="0,4,0,0"/>
                            </StackPanel>

                            <!-- Enter button -->
                            <Button Grid.Column="1"
                                    Content="ENTER"
                                    Command="{Binding DataContext.EnterEventCommand,
                                        RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                    CommandParameter="{Binding}"
                                    IsEnabled="{Binding CanEnter}"
                                    Style="{StaticResource AccentButtonStyle}"
                                    VerticalAlignment="Center"/>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Border>
```

---

## Part 3: Event Opponents (Mixed Approach)

### Concept

Events can have opponents from two sources:
1. **Pool Opponents** - Regular opponents from `Racers.ReadyToRace` that are eligible
2. **Event-Only Opponents** - Special opponents not tracked in game state

### Model Changes

**RaceEventDefinition.cs** - Add:

```csharp
public class RaceEventDefinition
{
    // Existing...

    /// <summary>
    /// If set, uses these special opponents instead of pool
    /// </summary>
    public List<EventOpponent>? SpecialOpponents { get; set; }

    /// <summary>
    /// If true, only use special opponents (no pool mixing)
    /// </summary>
    public bool ExclusiveOpponents { get; set; }
}

/// <summary>
/// Special opponent for events only - not tracked in game state
/// </summary>
public class EventOpponent
{
    public string Name { get; set; }
    public string Nickname { get; set; }
    public int Skill { get; set; }       // AC AI level (80-100)
    public int Aggression { get; set; }  // AC AI aggression (0-100)
    public string CarDefinitionId { get; set; }
    public string? CarSkin { get; set; }
    public string? PortraitPath { get; set; }
}
```

### Opponent Selection Service

**Services/Career/IEventOpponentService.cs**

```csharp
public interface IEventOpponentService
{
    /// <summary>
    /// Get an opponent for an event.
    /// Returns pool opponent if available, otherwise special opponent.
    /// </summary>
    EventOpponentResult? GetOpponentForEvent(
        RaceEventDefinition eventDef,
        GameState gameState
    );
}

public class EventOpponentResult
{
    public bool IsPoolOpponent { get; set; }

    // If pool opponent
    public Opponent? PoolOpponent { get; set; }
    public Car? PoolOpponentCar { get; set; }

    // If special opponent
    public EventOpponent? SpecialOpponent { get; set; }

    // Common
    public string OpponentName { get; set; }
    public string CarDefinitionId { get; set; }
    public string CarSkin { get; set; }
    public int Skill { get; set; }
    public int Aggression { get; set; }
}
```

**Services/Career/EventOpponentService.cs**

```csharp
public class EventOpponentService : IEventOpponentService
{
    private readonly ICarFilterService _filterService;
    private readonly IContentCatalogRepository _catalogRepository;
    private readonly Random _random = new();

    public EventOpponentResult? GetOpponentForEvent(
        RaceEventDefinition eventDef,
        GameState gameState)
    {
        // Try pool opponents first (unless exclusive)
        if (!eventDef.ExclusiveOpponents)
        {
            var poolOpponent = FindEligiblePoolOpponent(eventDef, gameState);
            if (poolOpponent != null)
                return poolOpponent;
        }

        // Fall back to special opponents
        if (eventDef.SpecialOpponents?.Count > 0)
        {
            var special = eventDef.SpecialOpponents[
                _random.Next(eventDef.SpecialOpponents.Count)
            ];

            return new EventOpponentResult
            {
                IsPoolOpponent = false,
                SpecialOpponent = special,
                OpponentName = special.Name,
                CarDefinitionId = special.CarDefinitionId,
                CarSkin = special.CarSkin ?? "default",
                Skill = special.Skill,
                Aggression = special.Aggression
            };
        }

        return null;
    }

    private EventOpponentResult? FindEligiblePoolOpponent(
        RaceEventDefinition eventDef,
        GameState gameState)
    {
        var candidates = new List<(Opponent, Car)>();

        foreach (var racer in gameState.Racers.ReadyToRace.Values.OfType<Opponent>())
        {
            var car = racer.Cars.FirstOrDefault();
            if (car == null) continue;

            // Check if opponent's car meets event requirements
            if (eventDef.EntryRequirements != null)
            {
                var carDef = _catalogRepository.GetCar(car.DefinitionId);
                if (carDef == null) continue;

                if (!_filterService.Matches(eventDef.EntryRequirements, carDef, car))
                    continue;
            }

            candidates.Add((racer, car));
        }

        if (candidates.Count == 0)
            return null;

        // Pick random eligible opponent
        var (opponent, opponentCar) = candidates[_random.Next(candidates.Count)];

        return new EventOpponentResult
        {
            IsPoolOpponent = true,
            PoolOpponent = opponent,
            PoolOpponentCar = opponentCar,
            OpponentName = opponent.Name,
            CarDefinitionId = opponentCar.DefinitionId,
            CarSkin = opponentCar.SkinId,
            Skill = opponent.Skill,
            Aggression = opponent.Aggression
        };
    }
}
```

### Sample Event with Special Opponents

```csharp
AddEvent(events, new RaceEventDefinition
{
    Id = "king_of_the_hill",
    Name = "King of the Hill",
    Description = "The mountain roads belong to the Hill Runners. Beat their champion.",
    RaceType = RaceType.Sprint,
    MinReputation = 60,
    ExclusiveOpponents = true,  // Only use special opponents
    SpecialOpponents = new List<EventOpponent>
    {
        new EventOpponent
        {
            Name = "Mountain Mike",
            Nickname = "The Summit",
            Skill = 95,
            Aggression = 70,
            CarDefinitionId = "ks_porsche_911_carrera_s",
            PortraitPath = "/Assets/Portraits/mountain_mike.png"
        }
    },
    Reward = new EventReward(2000, 20),
    Schedule = EventSchedule.OneTime
});
```

---

## Part 4: Scheduler Integration

### EventGenerationTask

**Services/Scheduler/Tasks/EventGenerationTask.cs**

```csharp
public class EventGenerationTask : IScheduledTask
{
    public string Id => "event_generation";
    public ScheduleFrequency Frequency => ScheduleFrequency.Daily;

    private readonly IRaceEventService _eventService;

    public EventGenerationTask(IRaceEventService eventService)
    {
        _eventService = eventService;
    }

    public Task ExecuteAsync(GameState gameState, DateTime triggerTime)
    {
        // Cleanup expired events
        var removed = _eventService.CleanupExpiredEvents(
            gameState.Career,
            gameState.Date
        );

        // Generate new events
        var newEvents = _eventService.GenerateEvents(
            gameState.Career,
            gameState.Date
        );

        // Update days played counter
        gameState.Career.IncrementCounter(MilestoneTrigger.DaysPlayed);

        return Task.CompletedTask;
    }
}
```

### Registration in App.xaml.cs

```csharp
// In App constructor, after creating RaceEventService:
Scheduler.RegisterTask(new EventGenerationTask(RaceEventService));
```

---

## Part 5: Milestone Notifications

### Approach: Check After Race, Show Dialog

**In RaceResultProcessor or a new CareerProgressService:**

```csharp
public class CareerProgressService
{
    private readonly IMilestoneService _milestoneService;
    private readonly IVictoryConditionService _victoryService;
    private readonly DialogService _dialogService;

    /// <summary>
    /// Check for career progress after a race. Shows notifications for:
    /// - Newly completed milestones
    /// - Newly unlocked victories
    /// - Victory achieved (game won)
    /// </summary>
    public void CheckProgressAfterRace(GameState gameState)
    {
        // Check milestones
        var newMilestones = _milestoneService.CheckForCompletedMilestones(
            gameState.Career
        );

        // Check victory unlocks
        var newVictoryUnlocks = _victoryService.CheckForNewUnlocks(
            gameState.Career
        );

        // Check for game victory
        var achievedVictory = _victoryService.CheckForVictory(gameState);

        // Show notifications (priority: victory > milestones > unlocks)
        if (achievedVictory != null && !gameState.Career.HasWonGame)
        {
            ShowVictoryDialog(achievedVictory, gameState);
        }
        else if (newMilestones.Count > 0)
        {
            ShowMilestoneDialog(newMilestones);
        }
        else if (newVictoryUnlocks.Count > 0)
        {
            ShowUnlockDialog(newVictoryUnlocks);
        }
    }

    private void ShowMilestoneDialog(List<MilestoneDefinition> milestones)
    {
        var message = milestones.Count == 1
            ? $"Milestone Achieved!\n\n{milestones[0].Name}\n{milestones[0].Description}"
            : $"Milestones Achieved!\n\n" +
              string.Join("\n", milestones.Select(m => $"• {m.Name}"));

        var dialog = new InformationDialogViewModel(
            _dialogService,
            message,
            "Milestone Complete"
        );
        _dialogService.ShowDialog(dialog);
    }
}
```

---

## Part 6: Event Race Flow

### Complete Flow Diagram

```
1. Player opens Newspaper
   └── NewspaperScreenViewModel.LoadRaceInvitations()
       └── Shows list of active events with eligibility

2. Player clicks "ENTER" on event
   └── OnEnterEvent(eventVm)
       └── EventOpponentService.GetOpponentForEvent()
           └── Returns pool or special opponent
       └── Shows EventEntryDialogView
           └── Player selects from eligible cars
           └── Shows opponent info

3. Player confirms entry
   └── OnEventEntryComplete(result)
       └── Create RaceContext with event info
       └── Navigate to RaceLoadingScreen
       └── Launch AC race

4. Race completes
   └── RaceResultProcessor.ProcessRaceResultAsync()
       └── UpdateMilestoneCounters()
       └── Update stats (if pool opponent)
       └── [Do NOT update opponent stats if special opponent]

   └── RaceEventService.CompleteEvent()
       └── Mark event as completed
       └── Return reward if won

   └── CareerProgressService.CheckProgressAfterRace()
       └── Show milestone/victory notifications

5. Player returns to Newspaper
   └── Event removed from list (completed or expired)
```

### RaceContext Changes

Add event tracking to RaceContext:

```csharp
public class RaceContext
{
    // Existing...

    /// <summary>
    /// Event ID if this is an event race (null for regular races)
    /// </summary>
    public string? EventId { get; set; }

    /// <summary>
    /// True if opponent is event-only (don't track their stats)
    /// </summary>
    public bool IsEventOnlyOpponent { get; set; }
}
```

### RaceResultProcessor Changes

```csharp
// In ApplyStatUpdates, skip opponent stat updates for event-only opponents:
if (context != null && !context.IsEventOnlyOpponent)
{
    var opponent = FindRacer(gameState, context.OpponentName);
    if (opponent != null)
    {
        // Update opponent stats...
    }
}

// After processing, complete the event:
if (context?.EventId != null)
{
    var app = (App)Application.Current;
    var reward = app.RaceEventService.CompleteEvent(
        /* find instance id */,
        outcome.PlayerWon,
        gameState.Career,
        DateTime.Now
    );

    if (reward != null)
    {
        // Apply reward
        gameState.Player.Money += reward.Cash;
        gameState.Career.IncrementCounter(
            MilestoneTrigger.ReputationReached,
            reward.Reputation
        );
        // Handle special item reward...
    }
}
```

---

## Implementation Order

### Phase 1: Career Screen (Display Only)
1. Create `CareerScreenView.xaml` and ViewModel
2. Create `VictoryDisplayViewModel` and `MilestoneDisplayViewModel`
3. Add navigation from Game screen
4. Wire up to existing services

### Phase 2: Newspaper Events (Display Only)
1. Add `EventInvitationViewModel`
2. Modify `NewspaperScreenViewModel` to load events
3. Update `NewspaperScreenView.xaml` with events section
4. Test event display and eligibility checking

### Phase 3: Event Entry Flow
1. Create `EventEntryDialogView` and ViewModel
2. Create `EventOpponentService`
3. Implement opponent selection (pool + special)
4. Connect to race launch flow

### Phase 4: Event Completion & Rewards
1. Add event tracking to `RaceContext`
2. Modify `RaceResultProcessor` for event completion
3. Implement reward application
4. Handle event-only opponent stat exclusion

### Phase 5: Scheduler & Notifications
1. Create `EventGenerationTask`
2. Register in scheduler
3. Create `CareerProgressService`
4. Add milestone/victory notification dialogs

### Phase 6: Polish
1. Add animations and visual polish
2. Create button assets for Career screen
3. Test edge cases (no eligible cars, expired events, etc.)
4. Balance event generation rates

---

## Service Dependencies Summary

```
App.xaml.cs
├── CarFilterService (no deps)
├── MilestoneService (no deps)
├── VictoryConditionService (needs GameState for dynamic checks)
├── RaceEventService (needs CarFilterService, CatalogRepository)
├── EventOpponentService (NEW - needs CarFilterService, CatalogRepository)
└── CareerProgressService (NEW - needs MilestoneService, VictoryService, DialogService)
```

---

## Open Questions

1. **Event generation rate**: How many events should appear at once? Current impl uses 50% random chance per eligible event.
    - Maximum of 5 events at once. They should vary in type and difficulty.

2. **Pink slip events**: Should event pink slip races wager the player's car, or just be high-stakes reputation?
    - They should wager the player's car to increase risk/reward.

3. **Special opponent portraits**: Need to create or source portrait assets for event-only opponents.
    - We need to create a set of generic drivers with the basic data and the prompts for creating portraits with ComfyUI.

4. **Event rewards - special items**: How should "rare parts" work? Need parts system integration.
    - For now, we can just log the reward and display a message. Full parts system integration can be done later.

5. **Repeat events**: Can daily/weekly events be entered multiple times per period, or once per appearance?
    - They can be entered once per appearance. After completion, they are marked as completed until the next generation cycle.
