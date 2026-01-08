using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Street_Rod_AC.Navigation
{
    public abstract class BaseScreenViewModel : IScreen, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public virtual void Enter()
        {
        }

        public virtual void Exit()
        {
        }
    }
}
