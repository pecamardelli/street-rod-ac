namespace Street_Rod_AC.Dialogs
{
    public interface IDialog
    {
        void OnOpened();
        void OnClosed();

        /// <summary>
        /// Two dialogs of the same type with the same key ask the same thing: the second is not shown while the
        /// first is up or waiting. Null for a dialog that is always shown.
        /// </summary>
        string? DuplicateKey { get; }
    }
}
