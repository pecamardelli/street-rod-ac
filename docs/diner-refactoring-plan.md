# Diner Screen Refactoring Plan

## Overview

Refactor the diner screen to improve layout, add track selection cards, implement a "talk container" for opponent dialogue, and consolidate the race setup flow by removing the ChallengeSetupDialog.

## Current State

The diner screen currently has:
- **Left panel**: Opponents list (ItemsControl with custom card buttons)
- **Right panel**: Vertically stacked layout with portrait, name, reputation, record card, car card, and challenge button
- **Challenge flow**: Opens `ChallengeSetupDialog` for race type, track, and bet configuration

## Target State

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ [Garage]                                                                    │
├───────────────────────┬─────────────────────────────────────────────────────┤
│                       │  RACER HEADER                                       │
│  OPPONENTS AT DINER   │  ┌──────────────────────┬────────────┬────────────┐ │
│  ┌─────────────────┐  │  │ Portrait  │ Speech   │  RECORD    │ CURRENT    │ │
│  │ [Card]          │  │  │ 150x150   │ Bubble   │  ────────  │ CAR        │ │
│  │ Big Mike        │  │  │           │ "You     │  5W - 2L   │ ─────────  │ │
│  │ "The Mountain"  │  │  │ Name      │ think    │            │ '69 Camaro │ │
│  │ Rep: 45         │  │  │ Nickname  │ you can  │            │ SS 396     │ │
│  └─────────────────┘  │  │ Rep Badge │ beat me?"│            │            │ │
│  ┌─────────────────┐  │  └──────────────────────┴────────────┴────────────┘ │
│  │ [Card]          │  ├─────────────────────────────────────────────────────┤
│  │ Sally Thunder   │  │  RACE OPTIONS                                       │
│  └─────────────────┘  │  ┌─────────────────────────────────────────────────┐ │
│  ┌─────────────────┐  │  │  DRAG RACE              │  ROAD RACE            │ │
│  │ [Card]          │  │  │  ┌───────┐ ┌───────┐   │  ┌───────┐ ┌───────┐  │ │
│  │ Johnny Speed    │  │  │  │Track 1│ │Track 2│   │  │Track 1│ │Track 2│  │ │
│  └─────────────────┘  │  │  └───────┘ └───────┘   │  └───────┘ └───────┘  │ │
│                       │  └─────────────────────────────────────────────────┘ │
│                       │  ┌─────────────────────────────────────────────────┐ │
│                       │  │  MATCHUP STATS                                  │ │
│                       │  │  You: 285HP, 3200lbs  vs  Them: 310HP, 3100lbs  │ │
│                       │  └─────────────────────────────────────────────────┘ │
│                       ├─────────────────────────────────────────────────────┤
│                       │  BET OPTIONS                                        │
│                       │  ○ Cash Wager [$____]    ○ Pink Slips              │
│                       ├─────────────────────────────────────────────────────┤
│                       │              [ CHALLENGE TO A RACE ]                │
└───────────────────────┴─────────────────────────────────────────────────────┘
```

---

## Phase 1: Layout Refactoring

### Goal
Restructure the racer container with portrait/name left-aligned and info cards on the right.

### Files to Modify
- `Screens/Diner/DinerScreenView.xaml`

### Changes

#### 1.1 Racer Header Section
Replace the current vertical StackPanel with a horizontal Grid:

```xml
<!-- Racer Header Section -->
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>  <!-- Portrait + Talk -->
        <ColumnDefinition Width="*"/>      <!-- Info Cards -->
    </Grid.ColumnDefinitions>

    <!-- Left: Portrait Section -->
    <StackPanel Grid.Column="0" Margin="0,0,20,0">
        <!-- Portrait (150x150) -->
        <!-- Name -->
        <!-- Nickname -->
        <!-- Reputation Badge -->
    </StackPanel>

    <!-- Right: Info Cards (same row) -->
    <UniformGrid Grid.Column="1" Columns="2" Rows="1">
        <!-- Record Card -->
        <!-- Current Car Card -->
    </UniformGrid>
</Grid>
```

#### 1.2 Portrait Section (Left-Aligned)
Move portrait and identity info to left-aligned StackPanel:
- Portrait: 150x150 with fallback "?" placeholder
- Name: 24pt bold, left-aligned
- Nickname: 16pt gold, left-aligned
- Reputation badge: left-aligned

#### 1.3 Info Cards (Right Side)
Place Record and Current Car cards in a horizontal arrangement:
- Use `UniformGrid Columns="2"` or `StackPanel Orientation="Horizontal"`
- Cards should have equal width and fill available space

### Acceptance Criteria
- [ ] Portrait and name are left-aligned
- [ ] Record and Car cards are on the same horizontal line as portrait
- [ ] Layout is responsive and doesn't break at different window sizes
- [ ] Visual consistency maintained with existing card styling

---

## Phase 2: Talk Container (Static)

### Goal
Add a speech bubble UI component for opponent dialogue with static/preset messages.

### Files to Create
- `Services/Talk/ITalkService.cs` - Interface
- `Services/Talk/StaticTalkService.cs` - Preset dialogue implementation
- `Models/TalkMessage.cs` - Message model (optional)

### Files to Modify
- `Screens/Diner/DinerScreenView.xaml` - Add speech bubble UI
- `Screens/Diner/DinerScreenViewModel.cs` - Add talk message property and loading

### Changes

#### 2.1 Speech Bubble UI
Add speech bubble next to portrait:

```xml
<!-- Speech Bubble Container -->
<Border Background="#2A2A2A"
        BorderBrush="#404040"
        BorderThickness="1"
        CornerRadius="10"
        Padding="15"
        Margin="10,0,0,0"
        MaxWidth="250">
    <!-- Triangle pointer (pointing left toward portrait) -->
    <Border.Effect>
        <DropShadowEffect BlurRadius="5" Opacity="0.3"/>
    </Border.Effect>

    <TextBlock Text="{Binding OpponentMessage}"
               FontSize="14"
               FontStyle="Italic"
               Foreground="{StaticResource TextPrimaryBrush}"
               TextWrapping="Wrap"/>
</Border>

<!-- Triangle pointer via Path or separate element -->
<Path Data="M 0,10 L 10,0 L 10,20 Z"
      Fill="#2A2A2A"
      Margin="-10,0,0,0"/>
```

#### 2.2 ITalkService Interface

```csharp
public interface ITalkService
{
    /// <summary>
    /// Get a contextual message from an opponent
    /// </summary>
    Task<string> GetMessageAsync(TalkContext context);

    /// <summary>
    /// Check if the service is available
    /// </summary>
    bool IsAvailable { get; }
}

public class TalkContext
{
    public Opponent Opponent { get; set; }
    public Player Player { get; set; }
    public CarDefinition? OpponentCar { get; set; }
    public CarDefinition? PlayerCar { get; set; }
    public TalkTrigger Trigger { get; set; }
}

public enum TalkTrigger
{
    OpponentSelected,      // Player clicked on opponent
    TrackSelected,         // Player selected a track
    ChallengeHover,        // Mouse over challenge button
    BetTypeChanged,        // Changed to pink slips
    Custom                 // For AI-generated contextual responses
}
```

#### 2.3 StaticTalkService Implementation

```csharp
public class StaticTalkService : ITalkService
{
    private readonly Random _random = new();

    public bool IsAvailable => true;

    public Task<string> GetMessageAsync(TalkContext context)
    {
        var messages = GetMessagesForContext(context);
        var message = messages[_random.Next(messages.Count)];
        return Task.FromResult(message);
    }

    private List<string> GetMessagesForContext(TalkContext context)
    {
        var repDiff = context.Opponent.Stats.Reputation - context.Player.Stats.Reputation;

        // High reputation opponent (cocky)
        if (repDiff > 20)
            return new List<string>
            {
                "You sure you want to do this, kid?",
                "I've eaten guys like you for breakfast.",
                "Nice car. It'll look good in my garage.",
                "Don't waste my time unless you're serious."
            };

        // Lower reputation opponent (defensive)
        if (repDiff < -20)
            return new List<string>
            {
                "I've got nothing to lose here.",
                "Everyone underestimates me. Big mistake.",
                "I'm faster than my record shows.",
                "Ready to be surprised?"
            };

        // Matched reputation (competitive)
        return new List<string>
        {
            "This should be a good race.",
            "I've heard about you. Let's see what you've got.",
            "May the best driver win.",
            "You look like you know what you're doing."
        };
    }
}
```

#### 2.4 ViewModel Changes

```csharp
// DinerScreenViewModel.cs

private readonly ITalkService _talkService;
private string _opponentMessage;

public string OpponentMessage
{
    get => _opponentMessage;
    set => SetProperty(ref _opponentMessage, value);
}

private async void OnOpponentSelected(OpponentDisplayViewModel opponent)
{
    SelectedOpponent = opponent;

    // Load talk message
    var context = new TalkContext
    {
        Opponent = opponent.Opponent,
        Player = _gameState.Player,
        OpponentCar = opponent.CarDefinition,
        PlayerCar = GetPlayerSelectedCar(),
        Trigger = TalkTrigger.OpponentSelected
    };

    OpponentMessage = await _talkService.GetMessageAsync(context);
}
```

### Acceptance Criteria
- [ ] Speech bubble appears next to portrait when opponent selected
- [ ] Messages are contextual based on reputation difference
- [ ] Different triggers can produce different messages
- [ ] Service interface allows for future AI implementation

---

## Phase 3: Race Options & Track Selection

### Goal
Add track selection cards organized by race type, load previews from AC.

### Files to Create
- `Screens/Diner/TrackCardViewModel.cs` - Track card display model

### Files to Modify
- `Screens/Diner/DinerScreenView.xaml` - Add track selection UI
- `Screens/Diner/DinerScreenViewModel.cs` - Add track loading and selection
- `Services/AssettoCorsaContentService.cs` - Add track preview path method

### Changes

#### 3.1 Track Preview Loading

```csharp
// IAssettoCorsaContentService.cs
string? GetTrackPreviewPath(string trackId);

// AssettoCorsaContentService.cs
public string? GetTrackPreviewPath(string trackId)
{
    if (string.IsNullOrEmpty(_acPath)) return null;

    var previewPath = Path.Combine(_acPath, "content", "tracks", trackId, "ui", "preview.png");
    return File.Exists(previewPath) ? previewPath : null;
}
```

#### 3.2 TrackCardViewModel

```csharp
public class TrackCardViewModel : INotifyPropertyChanged
{
    public TrackInfo Track { get; set; }
    public TrackConfiguration Configuration { get; set; }

    public string DisplayName => Configuration?.Name ?? Track.Name;
    public string TrackId => Track.Id;
    public string? ConfigurationId => Configuration?.Id;
    public string? PreviewPath { get; set; }
    public bool HasPreview => !string.IsNullOrEmpty(PreviewPath);

    public bool IsSelected { get; set; }
    public RaceType RaceType { get; set; }  // Drag or Road
}
```

#### 3.3 ViewModel Track Collections

```csharp
// DinerScreenViewModel.cs

public ObservableCollection<TrackCardViewModel> DragTracks { get; } = new();
public ObservableCollection<TrackCardViewModel> RoadTracks { get; } = new();

private TrackCardViewModel? _selectedTrack;
public TrackCardViewModel? SelectedTrack
{
    get => _selectedTrack;
    set
    {
        if (SetProperty(ref _selectedTrack, value))
        {
            // Update IsSelected on all tracks
            foreach (var track in DragTracks.Concat(RoadTracks))
                track.IsSelected = track == value;

            UpdateMatchupStats();
        }
    }
}

private void LoadTracks()
{
    DragTracks.Clear();
    RoadTracks.Clear();

    var tracks = _contentService.GetTracks();

    foreach (var track in tracks)
    {
        var raceType = track.Type == TrackType.Dragstrip
            ? RaceType.DragRace
            : RaceType.RoadRace;

        foreach (var config in track.Configurations)
        {
            var vm = new TrackCardViewModel
            {
                Track = track,
                Configuration = config,
                PreviewPath = _contentService.GetTrackPreviewPath(track.Id),
                RaceType = raceType
            };

            if (raceType == RaceType.DragRace)
                DragTracks.Add(vm);
            else
                RoadTracks.Add(vm);
        }
    }

    // Auto-select first drag track
    SelectedTrack = DragTracks.FirstOrDefault();
}
```

#### 3.4 Track Cards UI

```xml
<!-- Race Options Section -->
<Grid Margin="0,20,0,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>

    <!-- Drag Race Tracks -->
    <Border Grid.Column="0" Background="#1A1A1A" CornerRadius="5" Padding="10" Margin="0,0,5,0">
        <StackPanel>
            <TextBlock Text="DRAG RACE" FontWeight="Bold" FontSize="14" Margin="0,0,0,10"/>
            <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
                <ItemsControl ItemsSource="{Binding DragTracks}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal"/>
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <!-- Track Card -->
                            <Button Command="{Binding DataContext.SelectTrackCommand,
                                    RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                    CommandParameter="{Binding}">
                                <Border Width="120" Height="100" CornerRadius="5" Margin="5">
                                    <Border.Style>
                                        <Style TargetType="Border">
                                            <Setter Property="Background" Value="#2A2A2A"/>
                                            <Setter Property="BorderBrush" Value="#404040"/>
                                            <Setter Property="BorderThickness" Value="2"/>
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsSelected}" Value="True">
                                                    <Setter Property="BorderBrush" Value="#FFD700"/>
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Border.Style>
                                    <Grid>
                                        <!-- Preview Image -->
                                        <Image Source="{Binding PreviewPath}"
                                               Stretch="UniformToFill"
                                               Opacity="0.7"/>
                                        <!-- Track Name Overlay -->
                                        <Border Background="#AA000000"
                                                VerticalAlignment="Bottom"
                                                Padding="5">
                                            <TextBlock Text="{Binding DisplayName}"
                                                       FontSize="11"
                                                       TextWrapping="Wrap"
                                                       Foreground="White"/>
                                        </Border>
                                        <!-- Selection Indicator -->
                                        <TextBlock Text="*"
                                                   FontSize="20"
                                                   Foreground="#FFD700"
                                                   HorizontalAlignment="Right"
                                                   VerticalAlignment="Top"
                                                   Margin="5"
                                                   Visibility="{Binding IsSelected,
                                                       Converter={StaticResource BooleanToVisibilityConverter}}"/>
                                    </Grid>
                                </Border>
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </StackPanel>
    </Border>

    <!-- Road Race Tracks (same structure) -->
    <Border Grid.Column="1" ...>
        <!-- Similar to above -->
    </Border>
</Grid>
```

### Acceptance Criteria
- [ ] Tracks are loaded and categorized by type (Drag/Road)
- [ ] Track cards show preview images from AC
- [ ] Fallback placeholder for tracks without previews
- [ ] Single track selection with visual feedback
- [ ] Selection updates matchup stats

---

## Phase 4: Matchup Stats Display

### Goal
Show relevant car comparison stats based on selected race type.

### Files to Modify
- `Screens/Diner/DinerScreenView.xaml` - Add stats section
- `Screens/Diner/DinerScreenViewModel.cs` - Add stats calculation

### Changes

#### 4.1 Stats ViewModel Properties

```csharp
// DinerScreenViewModel.cs

public class MatchupStat
{
    public string Label { get; set; }
    public string PlayerValue { get; set; }
    public string OpponentValue { get; set; }
    public int Advantage { get; set; }  // -1 = opponent, 0 = equal, 1 = player

    public string AdvantageIndicator => Advantage switch
    {
        1 => "(+)",
        -1 => "(-)",
        _ => "(=)"
    };

    public string AdvantageColor => Advantage switch
    {
        1 => "#90EE90",   // Green
        -1 => "#FF6B6B", // Red
        _ => "#FFFFFF"   // White
    };
}

public ObservableCollection<MatchupStat> MatchupStats { get; } = new();

private void UpdateMatchupStats()
{
    MatchupStats.Clear();

    if (SelectedOpponent == null || SelectedTrack == null) return;

    var playerCar = GetPlayerSelectedCar();
    var opponentCar = SelectedOpponent.CarDefinition;

    if (playerCar == null || opponentCar == null) return;

    // Always show HP
    AddStat("Horsepower",
        playerCar.Specs?.Power ?? 0,
        opponentCar.Specs?.Power ?? 0,
        "HP", higherIsBetter: true);

    // Always show weight
    AddStat("Weight",
        playerCar.Specs?.Weight ?? 0,
        opponentCar.Specs?.Weight ?? 0,
        "lbs", higherIsBetter: false);

    // Show drivetrain for drag races
    if (SelectedTrack.RaceType == RaceType.DragRace)
    {
        MatchupStats.Add(new MatchupStat
        {
            Label = "Drivetrain",
            PlayerValue = playerCar.Specs?.Drivetrain ?? "?",
            OpponentValue = opponentCar.Specs?.Drivetrain ?? "?",
            Advantage = 0
        });
    }
}

private void AddStat(string label, double playerVal, double opponentVal,
    string unit, bool higherIsBetter)
{
    var advantage = playerVal.CompareTo(opponentVal);
    if (!higherIsBetter) advantage = -advantage;

    MatchupStats.Add(new MatchupStat
    {
        Label = label,
        PlayerValue = $"{playerVal:N0} {unit}",
        OpponentValue = $"{opponentVal:N0} {unit}",
        Advantage = Math.Sign(advantage)
    });
}
```

#### 4.2 Stats UI

```xml
<!-- Matchup Stats Section -->
<Border Background="#1A1A1A" CornerRadius="5" Padding="15" Margin="0,10,0,0">
    <StackPanel>
        <TextBlock Text="MATCHUP" FontWeight="Bold" FontSize="14" Margin="0,0,0,10"/>

        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <!-- Headers -->
            <TextBlock Grid.Column="0" Text="YOU" FontWeight="Bold" HorizontalAlignment="Center"/>
            <TextBlock Grid.Column="2" Text="THEM" FontWeight="Bold" HorizontalAlignment="Center"/>
        </Grid>

        <ItemsControl ItemsSource="{Binding MatchupStats}" Margin="0,10,0,0">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Grid Margin="0,3">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="80"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="30"/>
                        </Grid.ColumnDefinitions>

                        <TextBlock Grid.Column="0"
                                   Text="{Binding PlayerValue}"
                                   HorizontalAlignment="Center"/>
                        <TextBlock Grid.Column="1"
                                   Text="{Binding Label}"
                                   HorizontalAlignment="Center"
                                   Foreground="{StaticResource TextSecondaryBrush}"/>
                        <TextBlock Grid.Column="2"
                                   Text="{Binding OpponentValue}"
                                   HorizontalAlignment="Center"/>
                        <TextBlock Grid.Column="3"
                                   Text="{Binding AdvantageIndicator}"
                                   Foreground="{Binding AdvantageColor}"
                                   FontWeight="Bold"
                                   HorizontalAlignment="Center"/>
                    </Grid>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Border>
```

### Acceptance Criteria
- [ ] Stats update when track or opponent changes
- [ ] Relevant stats shown based on race type
- [ ] Clear visual indication of advantage/disadvantage
- [ ] Stats are accurate based on car definitions

---

## Phase 5: Bet Options & Dialog Removal

### Goal
Move bet configuration to diner screen and remove ChallengeSetupDialog.

### Files to Create
- None

### Files to Modify
- `Screens/Diner/DinerScreenView.xaml` - Add bet options
- `Screens/Diner/DinerScreenViewModel.cs` - Add bet properties and challenge logic

### Files to Remove/Deprecate
- `Dialogs/ChallengeSetup/ChallengeSetupDialogView.xaml` - Mark as deprecated or remove
- `Dialogs/ChallengeSetup/ChallengeSetupDialogViewModel.cs` - Mark as deprecated or remove

### Changes

#### 5.1 Bet Options UI

```xml
<!-- Bet Options Section -->
<Border Background="#1A1A1A" CornerRadius="5" Padding="15" Margin="0,10,0,0">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Cash Wager Option -->
        <RadioButton Grid.Column="0"
                     IsChecked="{Binding IsCashBet}"
                     GroupName="BetType">
            <StackPanel>
                <TextBlock Text="CASH WAGER" FontWeight="Bold"/>
                <Slider Value="{Binding CashWagerAmount}"
                        Minimum="{Binding MinWager}"
                        Maximum="{Binding MaxWager}"
                        IsEnabled="{Binding IsCashBet}"
                        Margin="0,10,0,0"/>
                <TextBlock Text="{Binding CashWagerDisplay}"
                           HorizontalAlignment="Center"/>
            </StackPanel>
        </RadioButton>

        <!-- Pink Slip Option -->
        <RadioButton Grid.Column="1"
                     IsChecked="{Binding IsPinkSlipBet}"
                     GroupName="BetType">
            <Border BorderBrush="#FF6B6B" BorderThickness="1" CornerRadius="5" Padding="10">
                <StackPanel>
                    <TextBlock Text="PINK SLIPS" FontWeight="Bold" Foreground="#FF6B6B"/>
                    <TextBlock Text="Winner takes loser's car!"
                               FontSize="11"
                               Foreground="{StaticResource TextSecondaryBrush}"
                               TextWrapping="Wrap"/>
                </StackPanel>
            </Border>
        </RadioButton>
    </Grid>
</Border>
```

#### 5.2 ViewModel Bet Properties

```csharp
// DinerScreenViewModel.cs

private bool _isCashBet = true;
public bool IsCashBet
{
    get => _isCashBet;
    set => SetProperty(ref _isCashBet, value);
}

public bool IsPinkSlipBet
{
    get => !_isCashBet;
    set => IsCashBet = !value;
}

private decimal _cashWagerAmount = 50;
public decimal CashWagerAmount
{
    get => _cashWagerAmount;
    set => SetProperty(ref _cashWagerAmount, value);
}

public string CashWagerDisplay => $"${CashWagerAmount:N0}";

public decimal MinWager => SelectedTrack?.RaceType == RaceType.DragRace ? 10 : 25;
public decimal MaxWager => SelectedTrack?.RaceType == RaceType.DragRace ? 100 : 250;
```

#### 5.3 Direct Challenge Command

```csharp
// DinerScreenViewModel.cs

private async void OnChallenge()
{
    if (SelectedOpponent == null || SelectedTrack == null) return;

    // Build challenge setup directly
    var setup = new ChallengeSetup
    {
        Opponent = SelectedOpponent.Opponent,
        PlayerCar = GetPlayerSelectedCar(),
        OpponentCar = GetOpponentCar(),
        IsPinkSlip = IsPinkSlipBet,
        CashWager = IsCashBet ? CashWagerAmount : 0,
        TrackId = SelectedTrack.TrackId,
        TrackConfig = SelectedTrack.ConfigurationId,
        RaceType = SelectedTrack.RaceType
    };

    // Evaluate opponent response
    var response = _challengeService.EvaluateChallenge(
        SelectedOpponent.Opponent,
        setup
    );

    if (response.Accepted)
    {
        // Launch race directly
        await LaunchRace(setup);
    }
    else
    {
        // Show rejection message (could use talk container!)
        OpponentMessage = response.Message;
    }
}
```

### Acceptance Criteria
- [x] Bet type selection (Cash/Pink Slip) works on diner screen
- [x] Cash wager slider with appropriate min/max based on race type
- [x] Challenge button creates setup and launches race directly
- [x] ChallengeSetupDialog is no longer used (can be removed or kept for reference)
- [x] Opponent rejection uses talk container for message

---

## Phase 6: Polish & Integration

### Goal
Final integration, testing, and visual polish.

### Tasks

#### 6.1 Animation & Transitions
- Add fade animation when opponent message changes
- Smooth selection transition for track cards
- Loading state while AI generates message (Phase 7)

#### 6.2 Error Handling
- Handle missing track previews gracefully
- Handle case where player has no car selected
- Validate bet amounts

#### 6.3 Edge Cases
- No opponents at diner
- No tracks available
- Player bankroll too low for minimum bet

#### 6.4 Cleanup
- Remove or deprecate ChallengeSetupDialog files
- Update any navigation that referenced the dialog
- Update documentation

### Acceptance Criteria
- [x] All animations are smooth and not jarring
- [x] Error states are handled gracefully with user feedback
- [x] No console errors or exceptions during normal use
- [x] Code is clean and follows existing patterns

---

## Dependencies

| Phase | Depends On |
|-------|------------|
| Phase 1 | None |
| Phase 2 | Phase 1 (needs layout structure) |
| Phase 3 | Phase 1 (needs layout structure) |
| Phase 4 | Phase 3 (needs track selection) |
| Phase 5 | Phase 3, Phase 4 |
| Phase 6 | All previous phases |

**Recommended order:** 1 → 2 → 3 → 4 → 5 → 6

Phases 2 and 3 can be done in parallel after Phase 1.

---

## Future: Phase 7 - AI Integration

See `docs/ai-integration.md` for the AI-powered talk service implementation that will replace/enhance the static talk service from Phase 2.
