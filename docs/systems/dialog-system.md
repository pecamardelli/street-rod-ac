# Dialog System

## Core Principle
Dialogs are modal overlays within the main window. Never nested. Never windows.

## Mental Model
- Main Screen (game state)
- One Modal Dialog Overlay (optional)

That's it. Two layers maximum. A dialog requested while another is open waits in a FIFO queue and shows when the one in
front closes: it never replaces it (a "Save Warning" is not lost behind "Purchase Successful").

## Dialog Types

| Type | Purpose |
|------|---------|
| ConfirmationDialog | Yes/No decisions |
| InformationDialog | Read-only message, single OK |
| EventEntryDialog | Pick a car to enter an event with |

## DialogService
Central service for showing/hiding dialogs.

| Method | Purpose |
|--------|---------|
| ShowDialog(viewModel) | Display the dialog, or queue it behind the open one |
| CloseDialog() | Hide the current dialog; the next queued one shows |
| CurrentDialog / IsDialogOpen | What is showing |

## Creating a Dialog

1. Create ViewModel in `Dialogs/[Name]/`:
   ```csharp
   public class MyDialogViewModel
   {
       public RelayCommand ConfirmCommand { get; }
       public RelayCommand CancelCommand { get; }
   }
   ```

2. Create View XAML:
   ```xml
   <UserControl x:Class="...MyDialogView">
       <!-- Dialog content -->
   </UserControl>
   ```

3. Add DataTemplate in `App.xaml`:
   ```xml
   <DataTemplate DataType="{x:Type dialogs:MyDialogViewModel}">
       <dialogs:MyDialogView/>
   </DataTemplate>
   ```

4. Show from screen:
   ```csharp
   var dialog = new MyDialogViewModel(_dialogService, ...);
   _dialogService.ShowDialog(dialog);
   ```

## Rules

| Do | Don't |
|----|-------|
| One dialog at a time | Nested dialogs |
| Inline confirmation states | Dialog opening dialog |
| Return results via callbacks (the dialog closes first, then calls back, so a callback may show the next dialog) | Native MessageBox |
| Dim background | Multiple modal windows |

## Escalation Rule
If dialog becomes complex → close it → navigate to a dedicated screen.

## Files
- `Dialogs/DialogService.cs`
- `Dialogs/Confirmation/ConfirmationDialogViewModel.cs`
- `Dialogs/Confirmation/ConfirmationDialogView.xaml`
- `Dialogs/Information/InformationDialogViewModel.cs`
- `Dialogs/Information/InformationDialogView.xaml`
