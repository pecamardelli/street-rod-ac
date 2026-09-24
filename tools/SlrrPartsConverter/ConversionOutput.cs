namespace Street_Rod_AC;

/// <summary>
/// How a run puts its output in place without leaving the content broken when it stops half way (an exception, a
/// locked file, the process killed): models and classes are made in a staging folder next to the output, a folder
/// that is replaced whole is swapped in by renames that can be undone, and a marker next to the output says a commit
/// is under way until it is over. Plain file system code with no part logic, so it can be tried on a temp folder.
/// </summary>
public static class ConversionOutput
{
    /// <summary>Ends the name of the folder next to the output that a run converts into before anything is moved in</summary>
    public const string StagingSuffix = ".converting";

    /// <summary>Ends the name of the file next to the output that is there while a run moves its output in</summary>
    public const string CommitSuffix = ".committing";

    /// <summary>Ends the name a folder being replaced goes by until its replacement is in</summary>
    public const string OldSuffix = ".old";

    /// <summary>The folder next to the output that a run converts into: same volume, so what is in it moves in by rename</summary>
    public static string StagingFolder(string output) => Sibling(output, StagingSuffix);

    /// <summary>
    /// The marker of a commit under way. Next to the output, not in it: the output is shipped content, and not in the
    /// staging folder, which every run clears
    /// </summary>
    public static string CommitMarker(string output) => Sibling(output, CommitSuffix);

    private static string Sibling(string output, string suffix)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        var parent = Path.GetDirectoryName(full) ?? full;
        return Path.Combine(parent, "." + Path.GetFileName(full) + suffix);
    }

    /// <summary>
    /// Takes away what a run left in its staging folder: a crashed run's before a run starts (strict: its models must
    /// not be moved in with the new ones), all of it once the run is over
    /// </summary>
    public static void ClearStaging(string staging, bool strict = false)
    {
        if (!Directory.Exists(staging)) return;

        try
        {
            Directory.Delete(staging, true);
        }
        catch (Exception ex) when (!strict && ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not remove {staging}: {ex.Message}");
        }
    }

    /// <summary>
    /// Replaces a folder whole with one made elsewhere on the same volume. The old folder is parked next to the
    /// target (never inside the staging folder, which is cleared whatever happens), the new one moved in, and only
    /// then the old one deleted; when the new one cannot be moved in, the old one goes back and the error is
    /// rethrown. Nothing staged, nothing replaced: the target is left as it is. Returns whether it was replaced.
    /// </summary>
    /// <remarks>
    /// A process killed between the renames leaves the old folder under <see cref="OldSuffix"/> with no target:
    /// <see cref="RecoverSwap"/> puts it back at the start of the next run
    /// </remarks>
    public static bool SwapIn(string target, string replacement)
    {
        if (!Directory.Exists(replacement)) return false;

        var old = target + OldSuffix;
        // A leftover next to a target that is there is a swap that got as far as its clean-up: nobody's only copy
        if (Directory.Exists(old) && Directory.Exists(target)) Directory.Delete(old, true);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var parked = false;
        if (Directory.Exists(target))
        {
            Directory.Move(target, old);
            parked = true;
        }

        try
        {
            Directory.Move(replacement, target);
        }
        catch
        {
            // A class file held open (antivirus, the game) must not cost the only copy
            if (parked && !Directory.Exists(target)) Directory.Move(old, target);
            throw;
        }

        if (parked)
        {
            try
            {
                Directory.Delete(old, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The new folder is in; the next run's recovery takes the leftover away
                Console.WriteLine($"Could not remove {old}: {ex.Message}");
            }
        }

        return true;
    }

    /// <summary>
    /// Undoes what a killed <see cref="SwapIn"/> left: the old folder parked with no target is put back, a parked
    /// folder next to a target that is there (the swap got as far as its clean-up) is deleted. Returns what it did,
    /// null when there was nothing to do.
    /// </summary>
    public static string? RecoverSwap(string target)
    {
        var old = target + OldSuffix;
        if (!Directory.Exists(old)) return null;

        if (!Directory.Exists(target))
        {
            Directory.Move(old, target);
            return $"put {Path.GetFileName(target)} back from {Path.GetFileName(old)}, which a stopped run left";
        }

        Directory.Delete(old, true);
        return $"removed {Path.GetFileName(old)}, which a stopped run left";
    }

    /// <summary>Marks a commit under way; the marker names the output and when</summary>
    public static void BeginCommit(string output)
    {
        File.WriteAllText(CommitMarker(output), $"{Path.GetFullPath(output)}{Environment.NewLine}{DateTime.UtcNow:O}{Environment.NewLine}");
    }

    /// <summary>The commit is over: the content is one run's again</summary>
    public static void EndCommit(string output)
    {
        File.Delete(CommitMarker(output));
    }

    /// <summary>Whether a run stopped while it moved its output in: the content is part that run's, part the one before</summary>
    public static bool CommitWasInterrupted(string output) => File.Exists(CommitMarker(output));

    /// <summary>
    /// Copies into a folder the files of another that it does not have yet, keeping their layout: what an older
    /// folder held that the new one must not lose. Returns how many were copied.
    /// </summary>
    public static int FillMissing(string folder, string from)
    {
        if (!Directory.Exists(from)) return 0;

        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(folder, Path.GetRelativePath(from, file));
            if (File.Exists(destination)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
            copied++;
        }

        return copied;
    }
}
