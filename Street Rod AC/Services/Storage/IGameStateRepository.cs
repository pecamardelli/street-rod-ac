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
        GameState CreateNew(string saveName, string playerName);
    }
}
