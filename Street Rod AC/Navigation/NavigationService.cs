using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Street_Rod_AC.Navigation
{
    public class NavigationService : INotifyPropertyChanged
    {
        private IScreen _currentScreen;

        public IScreen CurrentScreen
        {
            get => _currentScreen;
            private set
            {
                if (_currentScreen != value)
                {
                    _currentScreen = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void NavigateTo(IScreen screen)
        {
            CurrentScreen?.Exit();
            CurrentScreen = screen;
            CurrentScreen?.Enter();
        }
    }
}
