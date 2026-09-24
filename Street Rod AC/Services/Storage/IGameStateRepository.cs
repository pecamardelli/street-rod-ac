using LiteDB;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Storage
{
    /// <summary>
    /// The saves. Holds the open database of the save in use, so it is disposed on exit, after the last save.
    /// </summary>
    public interface IGameStateRepository : IDisposable
    {
        GameState? Load(string saveName);
        void Save(GameState state, string saveName);

        /// <summary>
        /// Saves the state and, in the same transaction, whatever <paramref name="sameTransaction"/> writes to
        /// the save's database (a race's session record): both are written, or neither is.
        /// </summary>
        void Save(GameState state, string saveName, Action<LiteDatabase>? sameTransaction);

        bool Exists(string saveName);
        void Delete(string saveName);
        List<string> ListSaves();

        /// <summary>
        /// What the Load screen shows of each save, newest first, read without loading the whole game and
        /// without switching the open save. A save that cannot be read is logged and left out.
        /// </summary>
        /// <remarks>
        /// The default loads each save whole and is there only for implementations with no cheaper way (test
        /// doubles); <see cref="GameStateRepository"/> reads just the header fields.
        /// </remarks>
        IReadOnlyList<SaveHeader> ListSaveHeaders()
        {
            var headers = new List<SaveHeader>();
            foreach (var saveName in ListSaves())
            {
                GameState? state;
                try
                {
                    state = Load(saveName);
                }
                catch (Exception)
                {
                    continue;
                }

                if (state != null)
                    headers.Add(new SaveHeader(saveName, state.Player.Name, state.Player.Money, state.Date, state.LastPlayedDate));
            }
            return headers;
        }

        GameState CreateNew(string saveName, string playerName, GameRules? rules = null);
    }

    /// <summary>A save as the Load screen lists it: its player, money, game date and when it was last played</summary>
    public sealed record SaveHeader(string SaveName, string PlayerName, decimal Money, DateTime Date, DateTime LastPlayedDate);
}
