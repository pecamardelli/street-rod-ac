using System.Diagnostics;
using System.IO;

namespace Street_Rod_AC.Services
{
    /// <summary>
    /// Whether the game is running, whoever started it. The install may only be put back while nothing of AC has
    /// it open: a restore under a running game deletes the copy and the unpacked data it is reading, and cannot
    /// move a sound bank FMOD holds. The process the launcher started is not enough to go by: Steam can relaunch
    /// acs.exe under a new process, and the app may have been restarted while a race it launched still runs.
    /// </summary>
    public static class AcProcesses
    {
        /// <summary>The race executable, as a process name</summary>
        public const string Race = "acs";

        /// <summary>The showroom executable, as a process name</summary>
        public const string Showroom = "acShowroom";

        /// <summary>A process name for an executable file name ("acs.exe" → "acs")</summary>
        public static string NameOf(string executable) => Path.GetFileNameWithoutExtension(executable);

        /// <summary>True when any process of the given name runs; never throws</summary>
        public static bool IsRunning(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes) process.Dispose();
                return processes.Length > 0;
            }
            catch (Exception)
            {
                // Not knowing is treated as running: the cost is a restore left for later, never one under the game
                return true;
            }
        }

        /// <summary>True when the race or the showroom runs</summary>
        public static bool AnyRunning() => IsRunning(Race) || IsRunning(Showroom);

        /// <summary>
        /// Whether the race or the showroom runs: null when the processes cannot be listed. For callers to whom
        /// "cannot tell" is not the same as "running" (a wait that must not go on forever on an error that keeps
        /// coming back).
        /// </summary>
        public static bool? QueryAnyRunning()
        {
            var race = Query(Race);
            if (race == true) return true;
            var showroom = Query(Showroom);
            if (showroom == true) return true;
            return race == null || showroom == null ? null : false;
        }

        private static bool? Query(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes) process.Dispose();
                return processes.Length > 0;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Kills every process of that name with its children; returns how many were asked to go. Never throws.</summary>
        public static int KillAll(string processName)
        {
            var killed = 0;
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (Exception)
            {
                return 0;
            }

            foreach (var process in processes)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    killed++;
                }
                catch (Exception)
                {
                    // Already gone, or not ours to kill: the caller waits for what is left
                }
                finally
                {
                    process.Dispose();
                }
            }

            return killed;
        }
    }
}
