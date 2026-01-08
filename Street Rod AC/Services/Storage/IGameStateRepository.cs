using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Storage
{
    public interface IGameStateRepository
    {
        GameState? Load(string saveName);
        void Save(GameState state, string saveName);
        bool Exists(string saveName);
        void Delete(string saveName);
        List<string> ListSaves();
        GameState CreateNew(string saveName, string playerName);
    }
}
