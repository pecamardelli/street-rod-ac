using System.IO;
using System.Runtime.InteropServices;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Audio;

/// <summary>
/// The handful of FMOD Studio calls it takes to play a car's engine: Assetto Corsa's own fmodstudio64.dll
/// (1.08), loaded from the AC install. The C API, not the C++ one, so plain P/Invoke will do: every signature here
/// is blittable, so the source-generated imports need no marshalling of their own. Names go in as NUL-terminated
/// UTF-8 bytes.
/// </summary>
internal static partial class FmodStudio
{
    private const string StudioDll = "fmodstudio64.dll";
    private const string CoreDll = "fmod64.dll";

    /// <summary>The header version the DLLs are built against: 1.08.12, the same in every AC install</summary>
    public const uint HeaderVersion = 0x00010812;

    public const int ResultOk = 0;

    // FMOD_STUDIO_INIT_SYNCHRONOUS_UPDATE: commands run in Update, on the caller's thread
    public const uint StudioInitSynchronousUpdate = 0x2;

    // FMOD_OUTPUTTYPE_NOSOUND_NRT: mixes one block per update, as fast as it is asked, to nowhere
    public const int OutputNoSoundNrt = 4;

    // FMOD_CHANNELCONTROL_DSP_HEAD: the end of a channel group's chain nearest the output
    public const int DspHead = -1;

    // FMOD_STUDIO_INIT_NORMAL, FMOD_INIT_NORMAL
    public const uint StudioInitNormal = 0;
    public const uint InitNormal = 0;

    // FMOD_STUDIO_LOAD_BANK_NORMAL
    public const uint LoadBankNormal = 0;

    // FMOD_STUDIO_STOP_MODE
    public const int StopAllowFadeout = 0;
    public const int StopImmediate = 1;

    // FMOD_STUDIO_LOADING_STATE
    public const int LoadingStateUnloading = 0;
    public const int LoadingStateUnloaded = 1;
    public const int LoadingStateLoading = 2;
    public const int LoadingStateLoaded = 3;
    public const int LoadingStateError = 4;

    /// <summary>
    /// How far an engine bank's parameters go when the bank does not say: the same for playing and for measuring,
    /// or the loudness would be measured over another range than the one heard
    /// </summary>
    public const float DefaultMaxRpm = 10000f;

    public const float DefaultMaxThrottle = 1f;

    /// <summary>The parameters every engine_ext event is driven by, as FMOD wants their names</summary>
    public static readonly byte[] RpmsName = Utf8("rpms");

    public static readonly byte[] ThrottleName = Utf8("throttle");

    private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("EngineAudio");

    private static readonly object LoadLock = new();
    private static bool _resolverSet;
    private static string? _folder;
    private static IntPtr _studioHandle;
    private static IntPtr _coreHandle;

    /// <summary>
    /// Points the imports at the AC install. The core DLL goes in first by its full path: the studio DLL links
    /// to it by name, and Windows would not look for it next to the studio DLL.
    ///
    /// Only a load that worked is kept, so a folder without FMOD can be tried again once the path is put right. Once
    /// loaded the DLLs stay for the process (every AC install ships the same 1.08): a later change of the AC folder
    /// only changes where the banks come from.
    /// </summary>
    public static void Load(string folder)
    {
        lock (LoadLock)
        {
            if (_folder != null) return;

            var core = Path.Combine(folder, CoreDll);
            var studio = Path.Combine(folder, StudioDll);
            if (!File.Exists(core) || !File.Exists(studio))
                throw new FileNotFoundException($"FMOD is not in the Assetto Corsa folder: {folder}");

            var coreHandle = NativeLibrary.Load(core);
            IntPtr handle;
            try
            {
                handle = NativeLibrary.Load(studio);
            }
            catch
            {
                // Let go of the core again: every retry would otherwise add a reference to it
                NativeLibrary.Free(coreHandle);
                throw;
            }

            if (!_resolverSet)
            {
                NativeLibrary.SetDllImportResolver(typeof(FmodStudio).Assembly, (name, _, _) =>
                    name == StudioDll ? handle : name == CoreDll ? coreHandle : IntPtr.Zero);
                _resolverSet = true;
            }

            _studioHandle = handle;
            _coreHandle = coreHandle;
            _folder = folder;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ParameterDescription
    {
        public IntPtr Name;
        public int Index;
        public float Minimum;
        public float Maximum;
        public float DefaultValue;
        public int Type;
    }

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_Create(out IntPtr system, uint headerVersion);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_Initialize(IntPtr system, int maxChannels, uint studioFlags, uint flags, IntPtr extraDriverData);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_Release(IntPtr system);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_RegisterPlugin(IntPtr system, IntPtr description);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_Update(IntPtr system);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_LoadBankFile(IntPtr system, byte[] fileNameUtf8, uint flags, out IntPtr bank);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_GetEventByID(IntPtr system, ref Guid id, out IntPtr description);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_Bank_Unload(IntPtr bank);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_CreateInstance(IntPtr description, out IntPtr instance);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_LoadSampleData(IntPtr description);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_GetSampleLoadingState(IntPtr description, out int state);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_GetParameterCount(IntPtr description, out int count);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventDescription_GetParameterByIndex(IntPtr description, int index, out ParameterDescription parameter);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_Start(IntPtr instance);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_Stop(IntPtr instance, int mode);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_Release(IntPtr instance);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_SetVolume(IntPtr instance, float volume);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_SetParameterValue(IntPtr instance, byte[] nameUtf8, float value);

    /// <summary>A vector in FMOD's space: x right, y up, z ahead of the listener</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector
    {
        public float X;
        public float Y;
        public float Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Attributes3D
    {
        public Vector Position;
        public Vector Velocity;
        public Vector Forward;
        public Vector Up;
    }

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_EventInstance_Set3DAttributes(IntPtr instance, ref Attributes3D attributes);

    [LibraryImport(StudioDll)]
    public static partial int FMOD_Studio_System_GetLowLevelSystem(IntPtr system, out IntPtr coreSystem);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_SetOutput(IntPtr system, int output);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_CreateDSP(IntPtr system, IntPtr description, out IntPtr dsp);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_System_GetMasterChannelGroup(IntPtr system, out IntPtr channelGroup);

    [LibraryImport(CoreDll)]
    public static partial int FMOD_ChannelGroup_AddDSP(IntPtr channelGroup, int index, IntPtr dsp);

    #region Calls 1.08 may not have

    // Looked up by name rather than imported: an import that is not there only fails when it is called, and each
    // of these has a way round it

    private static bool TryGetExport(IntPtr library, string name, out IntPtr function)
    {
        function = IntPtr.Zero;
        return library != IntPtr.Zero && NativeLibrary.TryGetExport(library, name, out function);
    }

    /// <summary>Runs every command queued so far, and waits for it; null when the DLL does not have it</summary>
    public static unsafe int? TryFlushCommands(IntPtr system) =>
        TryGetExport(_studioHandle, "FMOD_Studio_System_FlushCommands", out var function)
            ? ((delegate* unmanaged<IntPtr, int>)function)(system)
            : null;

    /// <summary>Where a bank is in loading or unloading; null when the DLL cannot say</summary>
    public static unsafe int? TryGetBankLoadingState(IntPtr bank, out int state)
    {
        state = LoadingStateError;
        if (!TryGetExport(_studioHandle, "FMOD_Studio_Bank_GetLoadingState", out var function)) return null;

        int value = LoadingStateError;
        var result = ((delegate* unmanaged<IntPtr, int*, int>)function)(bank, &value);
        state = value;
        return result;
    }

    /// <summary>Stops the mixer and its output thread for as long as nothing is to be heard; null when the DLL does not have it</summary>
    public static unsafe int? TryMixerSuspend(IntPtr coreSystem) =>
        TryGetExport(_coreHandle, "FMOD_System_MixerSuspend", out var function)
            ? ((delegate* unmanaged<IntPtr, int>)function)(coreSystem)
            : null;

    public static unsafe int? TryMixerResume(IntPtr coreSystem) =>
        TryGetExport(_coreHandle, "FMOD_System_MixerResume", out var function)
            ? ((delegate* unmanaged<IntPtr, int>)function)(coreSystem)
            : null;

    #endregion

    public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text + "\0");

    /// <summary>For setting up: a call that fails means the thing cannot be done at all</summary>
    public static void Check(int result, string what)
    {
        if (result != ResultOk) throw new FmodException(what, result);
    }

    // Failures already reported, by what failed: the first time, then at most once every few seconds with a count
    private static readonly TimeSpan WarnInterval = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<string, (DateTime Last, int Suppressed)> Warned = new();

    /// <summary>
    /// For playing: a call that fails is logged, and whatever it was for goes on without it. Many of these run
    /// every frame, so the same failure is logged once and then at most every few seconds, with how many went unsaid.
    /// </summary>
    /// <returns>True when the call went through</returns>
    public static bool Warn(int result, string what)
    {
        if (result == ResultOk) return true;

        var now = DateTime.UtcNow;
        int suppressed;
        lock (Warned)
        {
            if (Warned.TryGetValue(what, out var known) && now - known.Last < WarnInterval)
            {
                Warned[what] = (known.Last, known.Suppressed + 1);
                return false;
            }

            suppressed = known.Suppressed;
            Warned[what] = (now, 0);
        }

        if (suppressed > 0) Logger.Warning("FMOD {What} failed: error {Code} ({Suppressed} more since the last report)", what, result, suppressed);
        else Logger.Warning("FMOD {What} failed: error {Code}", what, result);
        return false;
    }

    /// <summary>The highest value a parameter of an event goes to; null when the event has no such parameter</summary>
    public static float? ParameterMaximum(IntPtr description, string name)
    {
        if (FMOD_Studio_EventDescription_GetParameterCount(description, out var count) != ResultOk) return null;
        for (var i = 0; i < count; i++)
        {
            if (FMOD_Studio_EventDescription_GetParameterByIndex(description, i, out var parameter) != ResultOk) continue;
            if (string.Equals(Marshal.PtrToStringUTF8(parameter.Name), name, StringComparison.OrdinalIgnoreCase))
                return parameter.Maximum;
        }

        return null;
    }
}

public sealed class FmodException(string what, int code) : Exception($"FMOD {what} failed: error {code}")
{
    public int Code { get; } = code;
}
