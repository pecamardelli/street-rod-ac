# Dialog System

## Core Principle
Dialogs are modal overlays within the main window. Never nested. Never windows.

## Mental Model
- Main Screen (game state)
- One Modal Dialog Overlay (optional)

That's it. Two layers maximum.

## Dialog Types

| Type | Purpose |
|------|---------|
| ConfirmationDialog | Yes/No decisions |
| InformationDialog | Read-only message, single OK |
| FormDialog | Simple user input |

## DialogService
Central service for showing/hiding dialogs.

| Method | Purpose |
|--------|---------|
| ShowDialog(viewModel) | Display dialog |
| CloseDialog() | Hide current dialog |

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
| Return results via callbacks | Native MessageBox |
| Dim background | Multiple modal windows |

## Escalation Rule
If dialog becomes complex → close it → navigate to a dedicated screen.

## Files
- `Dialogs/DialogService.cs`
- `Dialogs/Confirmation/ConfirmationDialogViewModel.cs`
- `Dialogs/Confirmation/ConfirmationDialogView.xaml`
- `Dialogs/Information/InformationDialogViewModel.cs`
- `Dialogs/Information/InformationDialogView.xaml`
