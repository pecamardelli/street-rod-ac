using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Street_Rod_AC.Audio;

/// <summary>
/// The two DSP plugins every Assetto Corsa car bank is built against, "FMOD Gain" and "FMOD Distance Filter".
/// They are the examples of the FMOD 1.08 plugin SDK, compiled into acs.exe and registered by it before it loads a
/// bank, so outside the game a car bank refuses to load (FMOD_ERR_PLUGIN_MISSING) until they are registered here.
///
/// A bank knows a plugin by its name and its parameters by index, so both are laid out exactly as acs.exe has them
/// (read off its plugin descriptions: SDK 108, version 1.0, the same callbacks set). The gain does what it says. The
/// distance filter narrows a band-pass as the sound moves off; a preview is heard from right beside the car, where
/// the example passes everything, so here it always does.
/// </summary>
internal static unsafe class FmodPlugins
{
    private const uint PluginSdkVersion = 108;
    private const uint PluginVersion = 0x00010000;

    private const int ParamFloat = 0;
    private const int ParamBool = 2;
    private const int ParamData = 3;

    // FMOD_DSP_PARAMETER_DATA_TYPE_3DATTRIBUTES
    private const int Data3DAttributes = -2;

    // Two FMOD_3D_ATTRIBUTES (relative, absolute) of four vectors each
    private const int Attributes3DSize = 2 * 4 * 3 * sizeof(float);

    private const int ValueStringLength = 32;

    // FMOD_DSP_STATE: the instance pointer, then the plugin's own data
    private const int PluginDataOffset = 8;

    [StructLayout(LayoutKind.Explicit, Size = 88)]
    private struct ParameterDesc
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(4)] public fixed byte Name[16];
        [FieldOffset(20)] public fixed byte Label[16];
        [FieldOffset(40)] public IntPtr Description;
        [FieldOffset(48)] public float Minimum;
        [FieldOffset(48)] public int BoolDefault;
        [FieldOffset(48)] public int DataType;
        [FieldOffset(52)] public float Maximum;
        [FieldOffset(56)] public float Default;
    }

    [StructLayout(LayoutKind.Explicit, Size = 216)]
    private struct DspDescription
    {
        [FieldOffset(0)] public uint PluginSdkVersion;
        [FieldOffset(4)] public fixed byte Name[32];
        [FieldOffset(36)] public uint Version;
        [FieldOffset(40)] public int NumInputBuffers;
        [FieldOffset(44)] public int NumOutputBuffers;
        [FieldOffset(48)] public IntPtr Create;
        [FieldOffset(56)] public IntPtr Release;
        [FieldOffset(64)] public IntPtr Reset;
        [FieldOffset(72)] public IntPtr Read;
        [FieldOffset(96)] public int NumParameters;
        [FieldOffset(104)] public IntPtr ParameterDescs;
        [FieldOffset(112)] public IntPtr SetFloat;
        [FieldOffset(128)] public IntPtr SetBool;
        [FieldOffset(136)] public IntPtr SetData;
        [FieldOffset(144)] public IntPtr GetFloat;
        [FieldOffset(160)] public IntPtr GetBool;
        [FieldOffset(168)] public IntPtr GetData;
    }

    // Per instance: the gain's target and where it has got to, the invert flag; the filter's two numbers and its
    // 3D attributes, which are only kept to be handed back
    [StructLayout(LayoutKind.Sequential)]
    private struct GainState
    {
        public float GainDb;
        public float Current;
        public int Invert;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterState
    {
        public float MaxDistance;
        public float Frequency;
        public fixed byte Attributes[Attributes3DSize];
    }

    private static DspDescription* _gain;
    private static DspDescription* _filter;
    private static DspDescription* _meter;

    // What the meter has heard since it was last reset: the sum of squares and the number of samples in it
    private static double _meterSquares;
    private static long _meterSamples;

    /// <summary>Registers both plugins with a Studio system; before any car bank is loaded</summary>
    public static void Register(IntPtr studioSystem)
    {
        _gain = _gain != null ? _gain : CreateGain();
        _filter = _filter != null ? _filter : CreateFilter();
        FmodStudio.Check(FmodStudio.FMOD_Studio_System_RegisterPlugin(studioSystem, (IntPtr)_gain), "register FMOD Gain");
        FmodStudio.Check(FmodStudio.FMOD_Studio_System_RegisterPlugin(studioSystem, (IntPtr)_filter), "register FMOD Distance Filter");
    }

    /// <summary>
    /// A DSP that passes the sound through and sums up how much of it there is, for measuring how loud something
    /// plays. One measurement at a time: the sums are the meter's, not the instance's.
    /// </summary>
    public static IntPtr CreateMeter(IntPtr coreSystem)
    {
        if (_meter == null)
        {
            var d = NewDescription("Street Rod Meter", null, 0);
            d->Read = (IntPtr)(delegate* unmanaged<IntPtr, float*, float*, uint, int, int*, int>)&MeterRead;
            _meter = d;
        }

        FmodStudio.Check(FmodStudio.FMOD_System_CreateDSP(coreSystem, (IntPtr)_meter, out var dsp), "create meter");
        return dsp;
    }

    public static void ResetMeter()
    {
        _meterSquares = 0;
        _meterSamples = 0;
    }

    /// <summary>A new sound: nothing of the last one left in the filters</summary>
    public static void ClearMeter()
    {
        ResetMeter();
        Array.Clear(MeterState);
    }

    /// <summary>K-weighted RMS of what went through since the reset, in dB full scale; null when nothing did</summary>
    public static double? MeterRmsDb() =>
        _meterSamples == 0 ? null : 10 * Math.Log10(Math.Max(_meterSquares / _meterSamples, 1e-12));

    // The descriptions live as long as the process: FMOD keeps pointers into them

    private static DspDescription* CreateGain()
    {
        var parameters = (ParameterDesc*)NativeMemory.AllocZeroed(2, (nuint)sizeof(ParameterDesc));
        FloatParameter(&parameters[0], "Gain", "dB", "Gain in dB. -80 to 10. Default = 0", -80f, 10f, 0f);
        BoolParameter(&parameters[1], "Invert", "", "Invert signal. Default = off", false);

        var d = NewDescription("FMOD Gain", parameters, 2);
        d->Create = (IntPtr)(delegate* unmanaged<IntPtr, int>)&GainCreate;
        d->Release = (IntPtr)(delegate* unmanaged<IntPtr, int>)&Release;
        d->Reset = (IntPtr)(delegate* unmanaged<IntPtr, int>)&GainReset;
        d->Read = (IntPtr)(delegate* unmanaged<IntPtr, float*, float*, uint, int, int*, int>)&GainRead;
        d->SetFloat = (IntPtr)(delegate* unmanaged<IntPtr, int, float, int>)&GainSetFloat;
        d->SetBool = (IntPtr)(delegate* unmanaged<IntPtr, int, int, int>)&GainSetBool;
        d->GetFloat = (IntPtr)(delegate* unmanaged<IntPtr, int, float*, byte*, int>)&GainGetFloat;
        d->GetBool = (IntPtr)(delegate* unmanaged<IntPtr, int, int*, byte*, int>)&GainGetBool;
        return d;
    }

    private static DspDescription* CreateFilter()
    {
        var parameters = (ParameterDesc*)NativeMemory.AllocZeroed(3, (nuint)sizeof(ParameterDesc));
        FloatParameter(&parameters[0], "Max Dist", "", "Distance at which bandpass stops narrowing. 0 to 1000000000. Default = 100", 0f, 1e9f, 100f);
        FloatParameter(&parameters[1], "Frequency", "Hz", "Bandpass target frequency. 100 to 10,000Hz. Default = 2000Hz", 100f, 10000f, 2000f);
        Text(parameters[2].Name, 16, "3D Attributes");
        parameters[2].Type = ParamData;
        parameters[2].Description = Utf8("");
        parameters[2].DataType = Data3DAttributes;

        var d = NewDescription("FMOD Distance Filter", parameters, 3);
        d->Create = (IntPtr)(delegate* unmanaged<IntPtr, int>)&FilterCreate;
        d->Release = (IntPtr)(delegate* unmanaged<IntPtr, int>)&Release;
        d->Reset = (IntPtr)(delegate* unmanaged<IntPtr, int>)&NoOp;
        d->Read = (IntPtr)(delegate* unmanaged<IntPtr, float*, float*, uint, int, int*, int>)&PassRead;
        d->SetFloat = (IntPtr)(delegate* unmanaged<IntPtr, int, float, int>)&FilterSetFloat;
        d->SetData = (IntPtr)(delegate* unmanaged<IntPtr, int, void*, uint, int>)&FilterSetData;
        d->GetFloat = (IntPtr)(delegate* unmanaged<IntPtr, int, float*, byte*, int>)&FilterGetFloat;
        d->GetData = (IntPtr)(delegate* unmanaged<IntPtr, int, void**, uint*, byte*, int>)&FilterGetData;
        return d;
    }

    private static DspDescription* NewDescription(string name, ParameterDesc* parameters, int count)
    {
        var d = (DspDescription*)NativeMemory.AllocZeroed((nuint)sizeof(DspDescription));
        d->PluginSdkVersion = PluginSdkVersion;
        Text(d->Name, 32, name);
        d->Version = PluginVersion;
        d->NumInputBuffers = 1;
        d->NumOutputBuffers = 1;
        d->NumParameters = count;

        if (count == 0) return d;

        // FMOD wants an array of pointers to the descriptions, not the descriptions themselves
        var pointers = (ParameterDesc**)NativeMemory.Alloc((nuint)count, (nuint)sizeof(IntPtr));
        for (var i = 0; i < count; i++) pointers[i] = &parameters[i];
        d->ParameterDescs = (IntPtr)pointers;
        return d;
    }

    private static void FloatParameter(ParameterDesc* p, string name, string label, string description, float min, float max, float value)
    {
        p->Type = ParamFloat;
        Text(p->Name, 16, name);
        Text(p->Label, 16, label);
        p->Description = Utf8(description);
        p->Minimum = min;
        p->Maximum = max;
        p->Default = value;
    }

    private static void BoolParameter(ParameterDesc* p, string name, string label, string description, bool value)
    {
        p->Type = ParamBool;
        Text(p->Name, 16, name);
        Text(p->Label, 16, label);
        p->Description = Utf8(description);
        p->BoolDefault = value ? 1 : 0;
    }

    private static void Text(byte* target, int size, string text)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        var n = Math.Min(bytes.Length, size - 1);
        for (var i = 0; i < n; i++) target[i] = bytes[i];
        target[n] = 0;
    }

    private static IntPtr Utf8(string text) => Marshal.StringToCoTaskMemUTF8(text);

    private static T* StateOf<T>(IntPtr dspState) where T : unmanaged => *(T**)((byte*)dspState + PluginDataOffset);

    private static void WriteValue(byte* target, string text)
    {
        if (target != null) Text(target, ValueStringLength, text);
    }

    #region Callbacks

    private const int Ok = FmodStudio.ResultOk;

    // FMOD_ERR_INVALID_PARAM
    private const int InvalidParam = 31;

    // FMOD_ERR_MEMORY, FMOD_ERR_INTERNAL
    private const int OutOfMemory = 38;
    private const int Internal = 28;

    // An exception must never leave an [UnmanagedCallersOnly] method: the process is failed on the spot. The ones
    // that allocate or build strings catch everything and answer FMOD with an error code instead; the Read callbacks
    // on the mixer thread do neither and stay as lean as they are.

    [UnmanagedCallersOnly]
    private static int GainCreate(IntPtr dspState)
    {
        try
        {
            var state = (GainState*)NativeMemory.AllocZeroed((nuint)sizeof(GainState));
            state->Current = 1f;
            *(void**)((byte*)dspState + PluginDataOffset) = state;
            return Ok;
        }
        catch (OutOfMemoryException)
        {
            return OutOfMemory;
        }
        catch
        {
            return Internal;
        }
    }

    [UnmanagedCallersOnly]
    private static int FilterCreate(IntPtr dspState)
    {
        try
        {
            var state = (FilterState*)NativeMemory.AllocZeroed((nuint)sizeof(FilterState));
            state->MaxDistance = 100f;
            state->Frequency = 2000f;
            *(void**)((byte*)dspState + PluginDataOffset) = state;
            return Ok;
        }
        catch (OutOfMemoryException)
        {
            return OutOfMemory;
        }
        catch
        {
            return Internal;
        }
    }

    [UnmanagedCallersOnly]
    private static int Release(IntPtr dspState)
    {
        var slot = (void**)((byte*)dspState + PluginDataOffset);
        NativeMemory.Free(*slot);
        *slot = null;
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static int NoOp(IntPtr dspState) => Ok;

    [UnmanagedCallersOnly]
    private static int GainReset(IntPtr dspState)
    {
        var state = StateOf<GainState>(dspState);
        state->Current = TargetOf(state);
        return Ok;
    }

    private static float TargetOf(GainState* state)
    {
        var linear = state->GainDb <= -80f ? 0f : MathF.Pow(10f, state->GainDb / 20f);
        return state->Invert != 0 ? -linear : linear;
    }

    /// <summary>Gain, ramped across the buffer to where it is going so a change does not click</summary>
    [UnmanagedCallersOnly]
    private static int GainRead(IntPtr dspState, float* input, float* output, uint length, int inChannels, int* outChannels)
    {
        var state = StateOf<GainState>(dspState);
        var target = TargetOf(state);
        var current = state->Current;
        var step = length == 0 ? 0f : (target - current) / length;

        for (uint frame = 0; frame < length; frame++)
        {
            current += step;
            var offset = frame * (uint)inChannels;
            for (var channel = 0; channel < inChannels; channel++) output[offset + channel] = input[offset + channel] * current;
        }

        state->Current = target;
        return Ok;
    }

    // K-weighting (ITU-R BS.1770) at 48 kHz: a high shelf for the head, then a high pass, per channel. Loudness as
    // ears have it: an engine is mostly low end, which plain RMS counts for more than it sounds
    private const int MeterChannels = 8;
    private static readonly double[] ShelfB = [1.53512485958697, -2.69169618940638, 1.19839281085285];
    private static readonly double[] ShelfA = [1, -1.69065929318241, 0.73248077421585];
    private static readonly double[] PassB = [1.0, -2.0, 1.0];
    private static readonly double[] PassA = [1, -1.99004745483398, 0.99007225036621];
    private static readonly double[] MeterState = new double[MeterChannels * 8];

    [UnmanagedCallersOnly]
    private static int MeterRead(IntPtr dspState, float* input, float* output, uint length, int inChannels, int* outChannels)
    {
        Unsafe.CopyBlock(output, input, length * (uint)inChannels * sizeof(float));

        var squares = 0.0;
        var channels = Math.Min(inChannels, MeterChannels);
        for (var c = 0; c < channels; c++)
        {
            // Two biquads, direct form I: x1 x2 y1 y2 each
            var o = c * 8;
            for (uint i = 0; i < length; i++)
            {
                double x = input[i * (uint)inChannels + (uint)c];
                var y = ShelfB[0] * x + ShelfB[1] * MeterState[o] + ShelfB[2] * MeterState[o + 1] - ShelfA[1] * MeterState[o + 2] - ShelfA[2] * MeterState[o + 3];
                MeterState[o + 1] = MeterState[o];
                MeterState[o] = x;
                MeterState[o + 3] = MeterState[o + 2];
                MeterState[o + 2] = y;

                var z = PassB[0] * y + PassB[1] * MeterState[o + 4] + PassB[2] * MeterState[o + 5] - PassA[1] * MeterState[o + 6] - PassA[2] * MeterState[o + 7];
                MeterState[o + 5] = MeterState[o + 4];
                MeterState[o + 4] = y;
                MeterState[o + 7] = MeterState[o + 6];
                MeterState[o + 6] = z;

                squares += z * z;
            }
        }

        _meterSquares += squares;
        _meterSamples += length * (uint)channels;
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static int PassRead(IntPtr dspState, float* input, float* output, uint length, int inChannels, int* outChannels)
    {
        Unsafe.CopyBlock(output, input, length * (uint)inChannels * sizeof(float));
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static int GainSetFloat(IntPtr dspState, int index, float value)
    {
        if (index != 0) return InvalidParam;
        StateOf<GainState>(dspState)->GainDb = value;
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static int GainSetBool(IntPtr dspState, int index, int value)
    {
        if (index != 1) return InvalidParam;
        StateOf<GainState>(dspState)->Invert = value;
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static int GainGetFloat(IntPtr dspState, int index, float* value, byte* valueString)
    {
        if (index != 0) return InvalidParam;
        try
        {
            var gain = StateOf<GainState>(dspState)->GainDb;
            if (value != null) *value = gain;
            WriteValue(valueString, string.Create(CultureInfo.InvariantCulture, $"{gain:0.0} dB"));
            return Ok;
        }
        catch
        {
            return Internal;
        }
    }

    [UnmanagedCallersOnly]
    private static int GainGetBool(IntPtr dspState, int index, int* value, byte* valueString)
    {
        if (index != 1) return InvalidParam;
        try
        {
            var invert = StateOf<GainState>(dspState)->Invert;
            if (value != null) *value = invert;
            WriteValue(valueString, invert != 0 ? "Inverted" : "Off");
            return Ok;
        }
        catch
        {
            return Internal;
        }
    }

    [UnmanagedCallersOnly]
    private static int FilterSetFloat(IntPtr dspState, int index, float value)
    {
        var state = StateOf<FilterState>(dspState);
        switch (index)
        {
            case 0: state->MaxDistance = value; return Ok;
            case 1: state->Frequency = value; return Ok;
            default: return InvalidParam;
        }
    }

    [UnmanagedCallersOnly]
    private static int FilterGetFloat(IntPtr dspState, int index, float* value, byte* valueString)
    {
        try
        {
            var state = StateOf<FilterState>(dspState);
            float result;
            switch (index)
            {
                case 0: result = state->MaxDistance; WriteValue(valueString, string.Create(CultureInfo.InvariantCulture, $"{result:0.0}")); break;
                case 1: result = state->Frequency; WriteValue(valueString, string.Create(CultureInfo.InvariantCulture, $"{result:0.0} Hz")); break;
                default: return InvalidParam;
            }

            if (value != null) *value = result;
            return Ok;
        }
        catch
        {
            return Internal;
        }
    }

    [UnmanagedCallersOnly]
    private static int FilterSetData(IntPtr dspState, int index, void* data, uint length)
    {
        if (index != 2) return InvalidParam;
        var state = StateOf<FilterState>(dspState);
        Unsafe.CopyBlock(state->Attributes, data, Math.Min(length, (uint)Attributes3DSize));
        return Ok;
    }

    [UnmanagedCallersOnly]
    private static int FilterGetData(IntPtr dspState, int index, void** data, uint* length, byte* valueString)
    {
        if (index != 2) return InvalidParam;
        try
        {
            var state = StateOf<FilterState>(dspState);
            if (data != null) *data = state->Attributes;
            if (length != null) *length = Attributes3DSize;
            WriteValue(valueString, "");
            return Ok;
        }
        catch
        {
            return Internal;
        }
    }

    #endregion
}
