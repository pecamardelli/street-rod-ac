# Time System

## Purpose
Game time progression with scheduled tasks that execute at intervals.

## Core Components

### GameState.Date
The in-game date and time. Advances when the player does things (`GameAction` tariffs in
`Services/Time/GameAction.cs`). The day runs from 8:00 to 22:00 (`GameTimeService`); time spent past 22:00 ends
the day and the game picks up at 8:00 the next morning.

### Ending the day
The garage's calendar panel has an **End Day** button: after asking, `GameTimeService.EndDayAsync` skips the
rest of the day to the next morning, the night's tasks run, and the game is saved.

### GameTimeScheduler
Executes registered tasks when time advances.

### IScheduledTask
Interface for tasks that run on schedule.

| Property | Type | Purpose |
|----------|------|---------|
| TaskId | string | Unique identifier |
| IntervalDays | int | How often to run |
| ExecuteAsync | method | Task logic |

## Task Execution Flow
1. Time advances from `previousDate` to `newDate`
2. Scheduler checks each registered task
3. If `daysSinceLastRun >= IntervalDays`: execute
4. Update `ScheduledTaskState.LastExecuted`

## ScheduledTaskState (Persisted)
Stored in `GameState.ScheduledTasks`:
- `TaskId` - Which task
- `LastExecuted` - When it last ran

## Current Tasks

| Task | Interval | Purpose |
|------|----------|---------|
| OpponentReviewTask | 1 day | Rivals buy, repair, tune, go broke and come back |
| RaceSimulatorTask | 1 day | Rivals race each other |
| EventGenerationTask | 1 day | Clears expired invitations and opens new ones; counts `DaysPlayed` |
| CarAdsReviewTask | 1 day | Buyers answer the player's car ads |
| MarketRefreshTask | 1 day | Refresh used car market |
| PartsAdsRefreshTask | 1 day | Refresh the used parts ads |

## Adding a New Task

1. Create class implementing `IScheduledTask`:
   ```csharp
   public class MyTask : IScheduledTask
   {
       public string TaskId => "my_task";
       public int IntervalDays => 1;
       public Task ExecuteAsync(GameState state, DateTime date) { ... }
   }
   ```

2. Register in `App.xaml.cs`:
   ```csharp
   Scheduler.RegisterTask(new MyTask(dependencies));
   ```

## Time Advancement
Activities that advance time call `IGameTimeService.SpendTimeAsync` (a `GameAction` or minutes), which runs the
scheduler once for each day that turns over. Nothing calls the scheduler directly.

## Files
- `Services/Scheduler/GameTimeScheduler.cs`
- `Services/Scheduler/IGameTimeScheduler.cs`
- `Services/Scheduler/IScheduledTask.cs`
- `Services/Scheduler/Tasks/*.cs`
- `Services/Time/GameTimeService.cs`
- `Models/GameState/ScheduledTaskState.cs`

## Travel

Visiting a dealer costs that dealer's `travelHours` (`Assets/Dealers/dealers.json`), spent through
`App.SpendTimeAsync(int minutes)` rather than a `GameAction`, because the cost is per dealer and not a
fixed tariff. The map checks `GetRemainingMinutesToday` first and asks before a trip that would run past
the end of the day. See `docs/screens/dealer-lot.md`.
