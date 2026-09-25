using LiteDB;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Storage
{
    /// <summary>
    /// How a save's objects become documents. Two things differ from LiteDB's defaults:
    /// <list type="bullet">
    /// <item>A racer stored where a <see cref="Racer"/> is expected (an <see cref="Opponent"/> in
    /// <see cref="RacerCollection"/>) is tagged with a short, stable <c>_type</c> ("Opponent"), not the assembly-qualified
    /// name, so renaming the assembly or the namespace (the game is Street Corsa now) does not break every save. Saves
    /// written before still load: their assembly-qualified names are read too, by their class name when the old
    /// assembly or namespace no longer exists.</item>
    /// <item>Strings are stored as they are: an empty string stays empty and whitespace is not trimmed (a racer's name
    /// is also its key in <see cref="RacerCollection"/>). What older saves stored as null still reads as null.</item>
    /// </list>
    /// A mapper builds a type's mapping on first use and is not safe to share between threads while it does, so each
    /// <see cref="SaveDatabase"/> has its own and uses it under its lock.
    /// </summary>
    public static class SaveMapper
    {
        public static BsonMapper Create() => new(customTypeInstantiator: null, typeNameBinder: SaveTypeNames.Instance)
        {
            EmptyStringToNull = false,
            TrimWhitespace = false
        };
    }

    /// <summary>
    /// The <c>_type</c> names of the save's polymorphic types. A name here is part of the save format: never change one.
    /// </summary>
    public sealed class SaveTypeNames : ITypeNameBinder
    {
        public static readonly SaveTypeNames Instance = new();

        private static readonly Dictionary<string, Type> Types = new(StringComparer.Ordinal)
        {
            ["Racer"] = typeof(Racer),
            ["Player"] = typeof(Player),
            ["Opponent"] = typeof(Opponent)
        };

        private static readonly Dictionary<Type, string> Names = Types.ToDictionary(pair => pair.Value, pair => pair.Key);

        private SaveTypeNames()
        {
        }

        public string GetName(Type type) =>
            Names.TryGetValue(type, out var name) ? name : DefaultTypeNameBinder.Instance.GetName(type);

        public Type GetType(string name)
        {
            if (Types.TryGetValue(name, out var type)) return type;

            // "Street_Rod_AC.Models.GameState.Opponent, Street Rod AC", as saves were written before the short names
            try
            {
                var found = DefaultTypeNameBinder.Instance.GetType(name);
                if (found != null) return found;
            }
            catch (LiteException)
            {
                // The assembly or namespace it names is gone: by its class name below
            }

            var fullName = name.Split(',')[0].Trim();
            var className = fullName[(fullName.LastIndexOf('.') + 1)..];
            if (Types.TryGetValue(className, out type)) return type;

            // LiteDB's own message for a type it cannot find
            return DefaultTypeNameBinder.Instance.GetType(name);
        }
    }
}
