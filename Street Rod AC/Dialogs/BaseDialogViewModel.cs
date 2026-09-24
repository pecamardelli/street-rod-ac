using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs
{
    public abstract class BaseDialogViewModel : ObservableObject, IDialog
    {
        public virtual void OnOpened()
        {
        }

        public virtual void OnClosed()
        {
        }
    }
}
