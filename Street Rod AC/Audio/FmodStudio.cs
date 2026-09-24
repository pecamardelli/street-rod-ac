using System.IO;
using System.Runtime.InteropServices;

namespace Street_Rod_AC.Audio;

/// <summary>
/// The handful of FMOD Studio calls it takes to play a car's engine: Assetto Corsa's own fmodstudio64.dll
/// (1.08), loaded from the AC install. The C API, not the C++ one, so plain P/Invoke will do.
/// </summary>
internal static class FmodStudio
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

    private static readonly object LoadLock = new();
    private static bool _resolverSet;
    private static string? _folder;

    /// <summary>
    /// Points the imports at the AC install. The core DLL goes in first by its full path: the studio DLL links
    /// to it by name, and Windows would not look for it next to the studio DLL.
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
            var handle = NativeLibrary.Load(studio);
            if (!_resolverSet)
            {
                NativeLibrary.SetDllImportResolver(typeof(FmodStudio).Assembly, (name, _, _) =>
                    name == StudioDll ? handle : name == CoreDll ? coreHandle : IntPtr.Zero);
                _resolverSet = true;
            }

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

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_System_Create(out IntPtr system, uint headerVersion);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_System_Initialize(IntPtr system, int maxChannels, uint studioFlags, uint flags, IntPtr extraDriverData);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_System_Release(IntPtr system);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_System_RegisterPlugin(IntPtr system, IntPtr description);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_System_Update(IntPtr system);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_System_LoadBankFile(IntPtr system, byte[] fileNameUtf8, uint flags, out IntPtr bank);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_System_GetEventByID(IntPtr system, ref Guid id, out IntPtr description);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_Bank_Unload(IntPtr bank);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventDescription_CreateInstance(IntPtr description, out IntPtr instance);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventDescription_LoadSampleData(IntPtr description);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventDescription_GetSampleLoadingState(IntPtr description, out int state);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventDescription_GetParameterCount(IntPtr description, out int count);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventDescription_GetParameterByIndex(IntPtr description, int index, out ParameterDescription parameter);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventInstance_Start(IntPtr instance);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventInstance_Stop(IntPtr instance, int mode);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventInstance_Release(IntPtr instance);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventInstance_SetVolume(IntPtr instance, float volume);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_EventInstance_SetParameterValue(IntPtr instance, byte[] nameUtf8, float value);

    [DllImport(StudioDll)]
    public static extern int FMOD_Studio_System_GetLowLevelSystem(IntPtr system, out IntPtr coreSystem);

    [DllImport(CoreDll)]
    public static extern int FMOD_System_SetOutput(IntPtr system, int output);

    [DllImport(CoreDll)]
    public static extern int FMOD_System_CreateDSP(IntPtr system, IntPtr description, out IntPtr dsp);

    [DllImport(CoreDll)]
    public static extern int FMOD_System_GetMasterChannelGroup(IntPtr system, out IntPtr channelGroup);

    [DllImport(CoreDll)]
    public static extern int FMOD_ChannelGroup_AddDSP(IntPtr channelGroup, int index, IntPtr dsp);

    public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text + "\0");

    public static void Check(int result, string what)
    {
        if (result != ResultOk) throw new FmodException(what, result);
    }
}

public sealed class FmodException(string what, int code) : Exception($"FMOD {what} failed: error {code}")
{
    public int Code { get; } = code;
}
