namespace Street_Rod_AC.Navigation
{
    public interface IScreen
    {
        void Enter();
        void Exit();

        /// <summary>
        /// The screen is shown again after a navigation away from it failed. Unlike <see cref="Enter"/> this must
        /// not start anything (a race, a visit that spends time, a save): the screen still holds its state and
        /// only undoes what <see cref="Exit"/> let go of.
        /// </summary>
        void Resume();
    }
}
