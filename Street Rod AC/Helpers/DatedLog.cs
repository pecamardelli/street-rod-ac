namespace Street_Rod_AC.Helpers;

/// <summary>A log kept by date, like the street talk and the paper's pieces: the newest at the end, the old ones dropped</summary>
public static class DatedLog
{
    /// <summary>
    /// <paramref name="items"/> at the end of <paramref name="log"/>; then whatever is more than <paramref name="days"/>
    /// days older than <paramref name="date"/> goes, and the oldest go past <paramref name="max"/> items
    /// </summary>
    public static void Append<T>(List<T> log, IEnumerable<T> items, Func<T, DateTime> dateOf, DateTime date, int days, int max)
    {
        log.AddRange(items);
        log.RemoveAll(item => (date - dateOf(item)).TotalDays > days);
        if (log.Count > max) log.RemoveRange(0, log.Count - max);
    }
}
