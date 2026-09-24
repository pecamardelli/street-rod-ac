using System.Globalization;
using Street_Rod_AC.Helpers;
using Street_Rod_AC.Parts.Scripting;

namespace Street_Rod_AC.Parts.Logic;

/// <summary>
/// Brings an assembly of parts to life the way the source game did: every part becomes an instance of its script
/// class, mounted on a stand-in car chassis, and the natives the scripts call (what is on this slot? how worn
/// am I?) are answered from the <see cref="InstalledPart"/> tree. The one big native, the engine simulation
/// behind DynoData.calcDyno, is <see cref="EngineDyno"/>.
/// </summary>
public sealed class PartScriptRuntime
{
    private const string ChassisClass = "java.game.parts.bodypart.Chassis";
    private const string DynoDataClass = "java.game.parts.DynoData";
    private const int ChassisEngineSlot = 1;

    private readonly ScriptVm _vm;
    private readonly Dictionary<InstalledPart, ScriptObject> _objects = new();
    private readonly Dictionary<ScriptObject, DynoResult> _dynoResults = new();

    public PartScriptRuntime(PartsCatalog catalog, InstalledPart root)
    {
        Root = root;

        var loader = catalog.Scripts;
        _vm = new ScriptVm(loader, new Host(this));

        try
        {
            Chassis = loader.Chain(ChassisClass) is { } chassisChain ? _vm.Instantiate(chassisChain, ScriptValue.Of(0)) : null;
        }
        catch (Exception ex)
        {
            Faults.Add($"the car's chassis script failed ({ex.GetType().Name}: {ex.Message})");
        }

        foreach (var part in root.SelfAndDescendants())
        {
            if (part.Definition.SourceScript == null) continue;

            // The script's path is pack.json content: it has to stay among the converted scripts
            var script = PathNames.TryCombineUnder(loader.Root, part.Definition.SourceScript, out var scriptFile) ? loader.Load(scriptFile) : null;
            if (script == null)
            {
                MissingScripts.Add(part);
                continue;
            }

            // A script that faults is a part that does nothing: the others go on without it, and the fault is told
            try
            {
                var instance = _vm.Instantiate(loader.Chain(script), ScriptValue.Of(0));
                instance.Tag = part;
                _objects[part] = instance;

                foreach (var (field, value) in part.Tuning) Tune(instance, field, value);
            }
            catch (Exception ex)
            {
                _objects.Remove(part);
                Fault(part, ex);
            }
        }
    }

    public InstalledPart Root { get; }

    /// <summary>Stand-in for the car; the scripts leave their results for the drivetrain on it</summary>
    public ScriptObject? Chassis { get; }

    /// <summary>Parts that have a script which is not among the converted classes; to the other parts they are not there</summary>
    public List<InstalledPart> MissingScripts { get; } = new();

    /// <summary>
    /// Parts whose script threw. One that threw while it was made or tuned is left out, as if its script were
    /// missing. One that threw later, in a call, stays in, half updated, for the other parts' scripts to go on
    /// asking: taking it out half way through would change what they see mid-run. Either way its fault is among
    /// <see cref="Faults"/>, which makes the engine's problem, so no figure it left behind reaches a race.
    /// </summary>
    public List<InstalledPart> FaultedScripts { get; } = new();

    /// <summary>What went wrong running the scripts, one line each, for the engine's problem and the log</summary>
    public List<string> Faults { get; } = new();

    /// <summary>A script ran out of steps: what it left behind is half done</summary>
    public bool BudgetExhausted => _vm.BudgetExhausted;

    /// <summary>Where the steps ran out (the entry method and the budget), when they did</summary>
    public string? BudgetExhaustedIn => _vm.BudgetExhaustedIn;

    /// <summary>Most steps one script entry took here: how close these parts come to the budget</summary>
    public int PeakSteps => _vm.PeakSteps;

    public ScriptObject? ObjectOf(InstalledPart part) => _objects.GetValueOrDefault(part);

    /// <summary>Calls a method of a part's script; unknown when the part has no script or the call faults</summary>
    public ScriptValue Call(InstalledPart part, string method, params ScriptValue[] arguments)
    {
        if (!_objects.TryGetValue(part, out var instance)) return ScriptValue.Unknown;

        try
        {
            return _vm.Call(instance, method, arguments);
        }
        catch (Exception ex)
        {
            Fault(part, ex);
            return ScriptValue.Unknown;
        }
    }

    /// <summary>The car's own update: finds the engine, lets it collect its numbers from its parts and runs the dyno</summary>
    public void UpdateCar()
    {
        if (Chassis == null) return;

        try
        {
            _vm.Call(Chassis, "updatevariables");
        }
        catch (Exception ex)
        {
            Faults.Add($"updating the car failed ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private void Fault(InstalledPart part, Exception ex)
    {
        if (!FaultedScripts.Contains(part)) FaultedScripts.Add(part);
        Faults.Add($"the script of {part.Definition.Id} failed ({ex.GetType().Name}: {ex.Message})");
    }

    /// <summary>Result of the last dyno run on the DynoData object of a block</summary>
    public DynoResult? DynoResultOf(ScriptObject dynoData) => _dynoResults.GetValueOrDefault(dynoData);

    private static void Tune(ScriptObject instance, string field, double value)
    {
        var bracket = field.IndexOf('[');
        if (bracket < 0)
        {
            var isInteger = instance.Fields.GetValueOrDefault(field) is ScriptNumber { IsInteger: true };
            instance.Fields[field] = new ScriptNumber(value, isInteger);
        }
        else if (instance.Fields.GetValueOrDefault(field[..bracket]) is ScriptArray array
                 && int.TryParse(field[(bracket + 1)..].TrimEnd(']'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            array.Items[index] = new ScriptNumber(value, false);
        }
    }

    private sealed class Host : ScriptHost
    {
        private readonly PartScriptRuntime _runtime;

        public Host(PartScriptRuntime runtime)
        {
            _runtime = runtime;
        }

        public override ScriptValue? CallNative(ScriptVm vm, ScriptObject self, string method, ScriptValue[] arguments, bool uncertain)
        {
            if (self.Is(DynoDataClass)) return Dyno(self, method, arguments);

            var part = self.Tag as InstalledPart;
            var isChassis = ReferenceEquals(self, _runtime.Chassis);
            var number = arguments.Length > 0 && arguments[0] is ScriptNumber first ? (int)first.Amount : 0;

            switch (method)
            {
                case "partOnSlot":
                    if (isChassis) return number == ChassisEngineSlot ? ScriptValue.Of(_runtime.ObjectOf(_runtime.Root)) : ScriptValue.Null;
                    if (part == null) return ScriptValue.Null;

                    // The slot a part hangs by leads back to what it hangs on
                    if (number == part.OwnSlot && part.Parent != null) return ScriptValue.Of(_runtime.ObjectOf(part.Parent));
                    if (part.Children.TryGetValue(number, out var child)) return ScriptValue.Of(_runtime.ObjectOf(child));

                    // A carburettor in a row with nothing on its own air horn breathes through the cleaner over the
                    // row; only through the horn, a nitrous slot or the like stays empty
                    var horn = part.Definition.Slots.FirstOrDefault(s => s.Id == number);
                    if (horn is { TakesAir: true } && part.Parent != null && part.ParentSlot != PartSlot.SharedAirSlot
                        && part.Parent.Children.TryGetValue(PartSlot.SharedAirSlot, out var shared))
                        return ScriptValue.Of(_runtime.ObjectOf(shared));

                    return ScriptValue.Null;

                case "slotIDOnSlot":
                    if (part == null) return ScriptValue.Of(0);
                    if (number == part.OwnSlot && part.Parent != null) return ScriptValue.Of(part.ParentSlot);
                    return ScriptValue.Of(part.Children.TryGetValue(number, out var mounted) ? mounted.OwnSlot : 0);

                case "getSlots":
                    return ScriptValue.Of(isChassis ? 1 : part?.Definition.Slots.Count ?? 0);

                case "getSlotID":
                    if (isChassis) return ScriptValue.Of(number == 0 ? ChassisEngineSlot : 0);
                    if (part == null) return ScriptValue.Of(0);
                    if (number < 0) return ScriptValue.Of(part.OwnSlot);
                    return ScriptValue.Of(number < part.Definition.Slots.Count ? part.Definition.Slots[number].Id : 0);

                case "getWear":
                    return ScriptValue.Of(part?.Wear ?? 1.0);

                case "getTear":
                    return ScriptValue.Of(part?.Tear ?? 1.0);

                case "getCarRef":
                    return ScriptValue.Of(_runtime.Chassis);

                case "getCar":
                    return ScriptValue.Of(_runtime.Chassis == null ? 0 : 1);

                case "getWheels":
                    return ScriptValue.Of(4);

                case "getInfo" or "getWheelID":
                    return ScriptValue.Of(0);

                // The car's wheels, sounds and scene are not there; scripts check for null before using them
                case "getWheel" or "getSfxTable" or "getParent":
                    return ScriptValue.Null;

                // Things scripts do to the game that mean nothing here
                case "setWear" or "setTear" or "setMaxWear" or "disableSlot" or "setSlotPos" or "setSlotDamage" or "command"
                    or "queueEvent" or "forceUpdate" or "setCooling" or "setSfxExhaustMinVol" or "setHornSFX" or "setNitroSFX"
                    or "setBuck" or "setSteerWheel" or "setSteerWheelRadius" or "clearEventMask" or "removeAllTimers"
                    or "unregisterCallbacks":
                    return ScriptValue.Null;

                default:
                    return null;
            }
        }

        private ScriptValue? Dyno(ScriptObject data, string method, ScriptValue[] arguments)
        {
            switch (method)
            {
                case "newNative" or "deleteNative":
                    return ScriptValue.Null;

                case "calcDyno":
                {
                    var result = EngineDyno.Run(DynoInputs.From(data));
                    _runtime._dynoResults[data] = result;

                    // The scripts read these back for their checks and the tuning menus
                    data.Fields["Displacement"] = ScriptValue.Of(result.Displacement);
                    data.Fields["Compression"] = ScriptValue.Of(result.Compression);
                    data.Fields["maxTorque"] = ScriptValue.Of(result.MaxTorque);
                    data.Fields["maxHP"] = ScriptValue.Of(result.MaxPowerHp);
                    data.Fields["RPM_maxTorque"] = ScriptValue.Of(result.MaxTorqueRpm);
                    data.Fields["RPM_maxHP"] = ScriptValue.Of(result.MaxPowerRpm);
                    data.Fields["torque"] = ScriptValue.Of(result.MaxTorque);
                    data.Fields["torque2"] = ScriptValue.Of(result.MaxTorque);
                    return ScriptValue.Of(result.MaxTorque);
                }

                case "getTorque" when arguments.Length >= 1 && arguments[0] is ScriptNumber rpm:
                    return ScriptValue.Of(_runtime._dynoResults.GetValueOrDefault(data)?.TorqueAt(rpm.Amount) ?? 0.0);

                case "getHP" when arguments.Length >= 1 && arguments[0] is ScriptNumber rpm:
                    return ScriptValue.Of(_runtime._dynoResults.GetValueOrDefault(data)?.PowerHpAt(rpm.Amount) ?? 0.0);

                default:
                    return null;
            }
        }
    }
}
