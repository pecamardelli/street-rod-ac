using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Navigation
{
    public abstract class BaseScreenViewModel : ObservableObject, IScreen
    {
        public virtual void Enter()
        {
        }

        public virtual void Exit()
        {
        }
    }
}
