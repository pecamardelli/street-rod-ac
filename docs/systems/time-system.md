# Time System

## Purpose
Game time progression with scheduled tasks that execute at intervals.

## Core Components

### GameState.CurrentDate
The in-game date. Advances when player performs activities.

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
| MarketRefreshTask | 1 day | Refresh used car market |

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
Activities that advance time should call:
```csharp
await Scheduler.OnTimeAdvancedAsync(gameState, oldDate, newDate);
```

## Files
- `Services/Scheduler/GameTimeScheduler.cs`
- `Services/Scheduler/IGameTimeScheduler.cs`
- `Services/Scheduler/IScheduledTask.cs`
- `Services/Scheduler/Tasks/MarketRefreshTask.cs`
- `Models/GameState/ScheduledTaskState.cs`
