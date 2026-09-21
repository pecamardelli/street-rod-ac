using System.IO;
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

        var loader = new ScriptClassLoader(Path.Combine(catalog.Root, PartScripts.Folder));
        _vm = new ScriptVm(loader, new Host(this));

        Chassis = loader.Chain(ChassisClass) is { } chassisChain ? _vm.Instantiate(chassisChain, ScriptValue.Of(0)) : null;

        foreach (var part in root.SelfAndDescendants())
        {
            var script = part.Definition.SourceScript == null ? null : loader.Load(Path.Combine(loader.Root, part.Definition.SourceScript));
            if (script == null) continue;

            var instance = _vm.Instantiate(loader.Chain(script), ScriptValue.Of(0));
            instance.Tag = part;
            _objects[part] = instance;

            foreach (var (field, value) in part.Tuning) Tune(instance, field, value);
        }
    }

    public InstalledPart Root { get; }

    /// <summary>Stand-in for the car; the scripts leave their results for the drivetrain on it</summary>
    public ScriptObject? Chassis { get; }

    public ScriptObject? ObjectOf(InstalledPart part) => _objects.GetValueOrDefault(part);

    public ScriptValue Call(InstalledPart part, string method, params ScriptValue[] arguments) =>
        _objects.TryGetValue(part, out var instance) ? _vm.Call(instance, method, arguments) : ScriptValue.Unknown;

    /// <summary>The car's own update: finds the engine, lets it collect its numbers from its parts and runs the dyno</summary>
    public void UpdateCar()
    {
        if (Chassis != null) _vm.Call(Chassis, "updatevariables");
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
                 && int.TryParse(field[(bracket + 1)..].TrimEnd(']'), out var index))
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
                    return part.Children.TryGetValue(number, out var child) ? ScriptValue.Of(_runtime.ObjectOf(child)) : ScriptValue.Null;

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
