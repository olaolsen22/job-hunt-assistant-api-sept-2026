namespace JobHunt.Api.Data;

// Keeps a user-ordered list numbered 0, 1, 2... after a move or delete.
internal static class Positions
{
    public static void Renumber<T>(List<T> ordered, Action<T, int> setPosition)
    {
        for (var i = 0; i < ordered.Count; i++)
            setPosition(ordered[i], i);
    }

    public static void Move<T>(List<T> ordered, T item, int position, Action<T, int> setPosition)
    {
        ordered.Remove(item);
        ordered.Insert(position, item);
        Renumber(ordered, setPosition);
    }
}
