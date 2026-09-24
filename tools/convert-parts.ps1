# Makes the game's parts content out of the SLRR install: the packs named after what they hold, sorted into
# engines/rims/tyres/exhaust/suspension/brakes, fictional, modern and non-mechanical parts left out.
# See docs/systems/parts-system.md for what each rule is there for.
#
# The SLRR install and the folder of build notes are this machine's: pass -Slrr and -Notes, or set SRAC_SLRR and
# SRAC_SLRR_NOTES once. A folder that is not there stops the run (the notes' engine builds would silently go).
# The converter is built (Release) from the sources first, so a stale build never makes the content; -Converter runs
# a given exe instead. The exit code is the converter's: 0 clean, 1 a rule or folder it refused (or a commit that
# stopped part way, which it says), 2 parts or packs left out, or packs of unreadable rpks kept as they were (listed at
# the end of its output).
param(
    [string]$Slrr = $env:SRAC_SLRR,
    [string]$Output = (Join-Path $PSScriptRoot "..\Street Rod AC\Assets\Parts"),
    [string]$Notes = $env:SRAC_SLRR_NOTES,
    [string]$Converter = "",
    # Part id patterns to measure (slots against mesh bounds) instead of converting: the joint conventions of a pack
    [string]$Measure = ""
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    [Console]::Error.WriteLine("convert-parts: $message")
    exit 1
}

# Windows PowerShell 5.1 passes a native argument that ends in a backslash as "...\", which the converter reads as an
# escaped quote: that argument swallows the ones after it. Folders set in the environment often end in one
function Trim-Folder([string]$folder) {
    if (-not $folder) { return $folder }
    $trimmed = $folder.TrimEnd('\', '/')
    # A drive root keeps its separator ("D:" alone is that drive's current folder), with a dot after it
    if ($trimmed -match '^[A-Za-z]:$') { return "$trimmed\." }
    return $trimmed
}
$Slrr = Trim-Folder $Slrr
$Output = Trim-Folder $Output
$Notes = Trim-Folder $Notes

if (-not $Slrr) { Fail "no SLRR folder: pass -Slrr <folder> or set SRAC_SLRR" }
# Not Join-Path: on a drive that is not there it throws before Fail can say what is wrong
if (-not (Test-Path -LiteralPath "$Slrr\parts" -PathType Container)) { Fail "$Slrr is not an SLRR install (no 'parts' folder)" }
if (-not $Notes) { Fail "no build notes folder: pass -Notes <folder> or set SRAC_SLRR_NOTES" }
if (-not (Test-Path -LiteralPath $Notes -PathType Container)) { Fail "the build notes folder is not there: $Notes" }

if (-not $Converter) {
    dotnet build (Join-Path $PSScriptRoot "SlrrPartsConverter\SlrrPartsConverter.csproj") -c Release -v q -nologo
    if ($LASTEXITCODE -ne 0) { Fail "the converter does not build" }
    $Converter = Join-Path $PSScriptRoot "SlrrPartsConverter\bin\Release\net10.0\SlrrPartsConverter.exe"
}
if (-not (Test-Path -LiteralPath $Converter -PathType Leaf)) { Fail "no converter at $Converter" }

# Packs a later release took the place of: the old one stays installed (car scripts and build notes name it) and
# every part of it resolves to its twin in the new one. Chrysler V8 Pack 4.5 for the MagnumForce Mopar pack; the
# user's own Ford six (ford_l6, with the meshes they modelled) for its predecessor, the Ford 221 pack (fordi6_data)
$replace = @(
    "engines/Mopar=engines/chrysler"
    "engines/fordi6_data=engines/ford_six"
    # The user's Ford V8s (260 to 460, ford_v8s: the Mopar pack's meshes with Ford scripts and textures) for Dexter's
    "engines/DEXTERV8s=engines/ford"
) -join ','

# Pairs of the Ford six replacement the matcher gets wrong: the old pack was a DOHC reskin, the new one is the pushrod
# engine it is, so its camshafts, bearing bridge and drive belt have no look-alikes; the Sprint and racing parts have
# theirs but the matcher settles for the stock ones
$twin = @(
    "engines/fordi6_data/Ford_188_221_intake_camshaft=engines/ford_six/ford_188_camshaft"
    "engines/fordi6_data/Ford_221_SP_intake_camshaft=engines/ford_six/ford_221_SP_camshaft"
    "engines/fordi6_data/Oreste_Berta_intake_camshaft=engines/ford_six/ford_racing_camshaft_3"
    "engines/fordi6_data/Ford_188_221_exhaust_camshaft=-"
    "engines/fordi6_data/Ford_221_SP_exhaust_camshaft=-"
    "engines/fordi6_data/Oreste_Berta_exhaust_camshaft=-"
    "engines/fordi6_data/Ford_188_221_SP_camshaft_bearing_bridge=-"
    "engines/fordi6_data/Ford_188_221_camshaft_drive_belt=engines/ford_six/ford_stock_timing_chain"
    "engines/fordi6_data/Ford_221_SP_cylinder_head=engines/ford_six/sprint_cylinder_head"
    "engines/fordi6_data/Oreste_Berta_cylinder_head=engines/ford_six/ford_racing_cylinder_head"
    "engines/fordi6_data/Ford_221_SP_intake_manifold=engines/ford_six/sprint_intake_manifold"
    "engines/fordi6_data/Ford_188_221_dual_intake_manifold=engines/ford_six/ford_triple_intake_manifold"
    "engines/fordi6_data/Ford_221_SP_dual_intake_manifold=engines/ford_six/ford_triple_intake_manifold"
    "engines/fordi6_data/Ford_221_SP_exhaust_header=engines/ford_six/sprint_exhaust_header"
    "engines/fordi6_data/Canossilen_exhaust_header=engines/ford_six/sprint_exhaust_header"
    "engines/fordi6_data/Holley_Argelite_2300=engines/ford_six/carb_holley_2300"
    # Dexter's Fords: one camshaft and generic dress parts for three engines, drawn on nothing the Ford V8 pack
    # shares, so the matcher pairs them by little; the 302/351 heads are stock heads, not the racing ones
    "engines/DEXTERV8s/Ford_302_351_L_head=engines/ford/Ford_302_left_cylinder_head"
    "engines/DEXTERV8s/Ford_302_351_R_head=engines/ford/Ford_302_right_cylinder_head"
    "engines/DEXTERV8s/L_Ford_head_cover=engines/ford/Ford_351_390_left_cylinder_head_cover"
    "engines/DEXTERV8s/R_Ford_head_cover=engines/ford/Ford_351_390_right_cylinder_head_cover"
    "engines/DEXTERV8s/Ford_302_351_429_camshaft=engines/ford/Ford_302_camshaft"
    # (the three racing camshafts of a family share one cfg: RPM boost, high torque _20B4/_29B4, medium _50B4/_39B4)
    "engines/DEXTERV8s/Comp_11_336_4_Camshaft=engines/ford/Ford_racing_camshaft_small_block_1"
    "engines/DEXTERV8s/Comp_11_754_14_Camshaft=engines/ford/Ford_racing_camshaft_small_block_1"
    "engines/DEXTERV8s/Comp_12_214_4_Camshaft=engines/ford/Ford_racing_camshaft_small_block_1_20B4"
    "engines/DEXTERV8s/Comp_12_262_4_Camshaft=engines/ford/Ford_racing_camshaft_small_block_1_50B4"
    "engines/DEXTERV8s/Comp_20_223_3_Camshaft=engines/ford/Ford_racing_camshaft_small_block_1_20B4"
    "engines/DEXTERV8s/Comp_21_242_4_Camshaft=engines/ford/Ford_racing_camshaft_small_block_1_50B4"
    "engines/DEXTERV8s/Comp_21_243_4_Camshaft=engines/ford/Ford_racing_camshaft_small_block_1_50B4"
    "engines/DEXTERV8s/Performer_camshaft=engines/ford/Ford_racing_camshaft_small_block_1_20B4"
    "engines/DEXTERV8s/Racing_camshaft=engines/ford/Ford_racing_camshaft_small_block_1"
    "engines/DEXTERV8s/Probsty_Hemi_Camshaft=-"
    "engines/DEXTERV8s/Universal_alternator=engines/ford/Ford_stock_alternator"
    "engines/DEXTERV8s/Universal_alternator_drive_belt=engines/ford/Ford_stock_alt_drive_belt"
    "engines/DEXTERV8s/Universal_timing_chain_cover=engines/ford/Ford_stock_timing_cover"
    "engines/DEXTERV8s/Universal_oilfilter=engines/ford/Motorcraft_oil_filter"
    "engines/DEXTERV8s/Universal_crankshaft_bearing_bridge=-"
    "engines/DEXTERV8s/Universal_wires=-"
    "engines/DEXTERV8s/Universal_N2O_system=-"
    "engines/DEXTERV8s/Universal_blower=engines/generic/Edelbrock_supercharger_small_block"
    "engines/DEXTERV8s/Universal_blower_belt=engines/generic/Edelbrock_supercharger_drive_belt_small_block"
    "engines/DEXTERV8s/Universal_blower_pulley=-"
    "engines/DEXTERV8s/Universal_dual_4_barrel_carburetors=engines/generic/Holley_2x4brl_carburetors"
    "engines/DEXTERV8s/Universal_round_air_filter=engines/ford/Holley_4brl_air_filter"
    "engines/DEXTERV8s/Universal_oval_air_filter=engines/ford/Edelbrock_2x4brl_air_filter"
    "engines/DEXTERV8s/Ford_4_barrel_intake_manifold=engines/ford/Ford_302_4brl_intake_manifold"
    "engines/DEXTERV8s/Ford_dual_4_barrel_intake_manifold=engines/ford/Trickflow_2x4brl_intake_manifold_big_block"
    "engines/DEXTERV8s/Ford_blower_intake_manifold=engines/ford/Ford_supercharger_manifold_big_block"
    "engines/DEXTERV8s/Ford_intake_manifold_converter=-"
) -join ','

$rename = @(
    # Universal-fit induction parts (aftermarket carburettors, air cleaners, scoops, roots blowers) out of the
    # engine packs into one generic pack; factory carburettors and air cleaners stay with their brand
    "engines/Chrysler_V8_pak:Carburetors_4BRL_street_HOLLEY=engines/generic"
    "engines/Chrysler_V8_pak:Carburetors_4BRL_Edelbrock=engines/generic"
    "engines/Chrysler_V8_pak:Carburetors_4BRL_Demon=engines/generic"
    "engines/Chrysler_V8_pak:Carburetors_2BRL_HOLLEY=engines/generic"
    "engines/Chrysler_V8_pak:Carburetors_2x4BRL_Dominator_HOLLEY=engines/generic"
    "engines/Chrysler_V8_pak:Carburetors_2x4BRL_Edelbrock=engines/generic"
    "engines/Chrysler_V8_pak:Carburetors_2x4BRL_King_Demon=engines/generic"
    "engines/Chrysler_V8_pak:Carburetors_3x2BRL_HOLLE_six_pack=engines/generic"
    "engines/Chrysler_V8_pak:Carburetors_3x2BRL_Road_Demon_six_pack=engines/generic"
    "engines/Chrysler_V8_pak:Air_cleaner_Edelbrock*=engines/generic"
    "engines/Chrysler_V8_pak:Air_cleaner_HOLLEY=engines/generic"
    "engines/Chrysler_V8_pak:Air_cleaner_KN_2x4BRL=engines/generic"
    "engines/Chrysler_V8_pak:Air_cleaner_Six_pack_HOLLEY=engines/generic"
    "engines/Chrysler_V8_pak:Air_scoop_*=engines/generic"
    "engines/Chrysler_V8_pak:air_scoop=engines/generic"
    "engines/Chrysler_V8_pak:dualcarb_scoop=engines/generic"
    "engines/Chrysler_V8_pak:velocity_stack=engines/generic"
    "engines/Chrysler_V8_pak:Supercharger_Weiand=engines/generic"
    "engines/GM_V8_pak:hardcore_1050cfm_carb=engines/generic"
    "engines/GM_V8_pak:Holley_2brl_carb=engines/generic"
    "engines/GM_V8_pak:Holley_4brl_carburator=engines/generic"
    "engines/GM_V8_pak:Holley_2x4brl_carburator=engines/generic"
    "engines/GM_V8_pak:Holley_3x2brl_carbs=engines/generic"
    "engines/GM_V8_pak:Holley_2x2brl_carbs=engines/generic"
    "engines/GM_V8_pak:KN_filter=engines/generic"
    "engines/GM_V8_pak:MOROSO_air_filter=engines/generic"
    "engines/GM_V8_pak:Summit_air_filter=engines/generic"
    "engines/GM_V8_pak:Summit_2x4_3x2_air_filter=engines/generic"
    "engines/GM_V8_pak:Summit_scoop_*=engines/generic"
    "engines/GM_V8_pak:Holley_2x4brl_filter=engines/generic"
    "engines/GM_V8_pak:Custom_horns=engines/generic"
    "engines/GM_V8_pak:Weiand_8_71_supercharger=engines/generic"
    "engines/GM_V8_pak:Weiand_pro_Street_supercharger=engines/generic"
    "engines/GM_V8_pak:Holley_Street_supercharger=engines/generic"
    "engines/GM_V8_pak:Holley_2x4_charger=engines/generic"
    # The Ford V8 pack's aftermarket carburettors and its Edelbrock blower; its air filters stay Ford (decals)
    "engines/ford_v8s:Holley_street_carburetor=engines/generic"
    "engines/ford_v8s:Holley_street_4brl_carburetor=engines/generic"
    "engines/ford_v8s:Edelbrock_4brl_carburetor*=engines/generic"
    "engines/ford_v8s:Holley_2x4brl_carburetors=engines/generic"
    "engines/ford_v8s:Edelbrock_2x4brl_carburetors*=engines/generic"
    "engines/ford_v8s:Edelbrock_supercharger_*=engines/generic"
    # Engine packs by brand
    "engines/Chrysler_V8_pak=engines/chrysler"
    "engines/GM_V8_pak=engines/gm"
    "engines/ford_v8s=engines/ford"
    "engines/ford_l6=engines/ford_six"
    # Rims and tyres by brand; Goodyear's rpk holds both
    "mopar_wheels=rims/mopar"
    "mopar_tires=tyres/mopar"
    "falcon_rim=rims/falcon"
    "hudson_rim=rims/hudson"
    "rodas_fad=rims/opala"
    "tyre_Camaro69=tyres/camaro69"
    "Goodyear_Eagle:Tyre=tyres/goodyear_eagle"
    "Goodyear_Eagle=rims/goodyear_eagle"
    "mufflers=exhaust/mufflers"
    # The base game's parts.rpk: body parts out (dropped below), running gear and the roots next to what needs them
    "stock:ExtraLights=body/stock"
    "stock:FrontSeat=body/stock"
    "stock:bodypart=body/stock"
    "stock:BodyPart*=body/stock"
    "stock:Suspension=suspension/stock"
    "stock:Suspensions=suspension/stock"
    "stock:Spring=suspension/stock"
    "stock:ShockAbsorber=suspension/stock"
    "stock:Swaybar=suspension/stock"
    "stock:DiscBrake=brakes/stock"
    "stock:brake=brakes/stock"
    "stock:Brake_0245=brakes/stock"
    "stock:Wheel=rims/stock"
    "stock:Tyre=tyres/stock"
    "stock:ExhaustTip=exhaust/stock"
    "stock:ExhaustPipe=exhaust/stock"
    "stock:rgearpart=suspension/stock"
    "stock:RGearPart*=suspension/stock"
    "stock=engines/stock"
) -join ','

$drop = @(
    # Fictional and modern engines
    "engines/Baiern_Emer/*"
    "engines/Einvagen_Duhen_Ishima_Focer/*"
    "engines/GMC_Nissan_Honda_Hyundai_Opel/*"
    "engines/Buick_LC2/*"
    "engines/MC_Prime/*"
    "engines/MC_Prime_SuperDuty/*"
    "engines/chrysler/_Engine_block_360BP"
    "engines/gm/GMP_427_block"
    # Dexter's Dodge, Chevrolet and drag engines and his 2-bar blower: the pack is replaced, but its parts are paired
    # with the Ford V8s, and these must not be (a Duster with a Ford 292 is no Duster)
    "engines/DEXTERV8s/Dodge_*"
    "engines/DEXTERV8s/*_Mopar_head_cover"
    "engines/DEXTERV8s/*Chevrolet_*"
    "engines/DEXTERV8s/Drag_*"
    "engines/DEXTERV8s/Universal_blower_2"
    # The Ford V8 pack's scriptless dress-up and nitrous bits (they would pair with anything)
    "engines/ford/dress_1_st_pump"
    "engines/ford/nos_*"
    "engines/ford/Edelbrock_dual_timing_gears"
    # The Ford six pack still carries the Baiern and Emer parts it was grown from, and a nitrous rail without a script
    "engines/ford_six/Baiern_*"
    "engines/ford_six/Emer_*"
    "engines/ford_six/SL_Tuners_*"
    # Modern tuner wheels, and everything that is body or interior
    "wheels/*"
    "interior/*"
    "wings/*"
    "body/*"
) -join ','

$merge = @(
    # The same part twice with the same script values: one stays. Carburettors of different packs are NOT merged
    # even when they are the same product: every pack tunes its carburettors' mixture and fuel flow its own way,
    # and the engines were rated with them (they borrow the better model instead, see $model)
    "engines/chrysler/Carburetors_4BRL_Holley=engines/generic/Carburetors_4BRL_street_HOLLEY"
    # Every carburettor part is one carburettor: a set whose single exists with the same script values becomes that
    # many of the single (builds get one per pad); sets with values of their own are sliced instead, see $single
    "engines/generic/Carburetors_2x4BRL_Edelbrock=engines/generic/Carburetors_4BRL_Edelbrock*2"
    "engines/generic/Carburetors_3x2BRL_HOLLE_six_pack=engines/generic/Carburetors_2BRL_HOLLEY*3"
    "engines/gm/stock_2x4brl_carburator=engines/gm/stock_4brl_carburator*2"
    "engines/generic/Holley_3x2brl_carbs=engines/generic/Holley_2brl_carb*3"
    # Transmissions from after the 1960s: builds and saves get the period box of the same engine instead
    "engines/chrysler/Transmission_TKO*=engines/chrysler/Transmission_A833_4spd"
    "engines/gm/Tremec_*=engines/gm/RG_427_4spd"
    "engines/gm/TCI_427_4spd_tranny=engines/gm/RG_427_4spd"
    "engines/gm/RG_427_5spd_tranny=engines/gm/RG_427_4spd"
    "engines/gm/GM_427_4spd_tranny=engines/gm/GM_427_3spd_tranny"
    "engines/gm/RG_4spd_plus_tranny_*=engines/gm/RG_4spd_plus_tranny"
    "engines/gm/V8_4spd_tranny_GM=engines/gm/V8_3spd_tranny"
    "engines/gm/GM_500_4spd_gearbox=engines/gm/GM_500_3spd_gearbox"
    "engines/ford/Tranny_tremec_*=engines/ford/Tranny_borg_warner_super_t10_4spd"
    # The Ford pack's Edelbrock blower twice, once per block family, with the same values: one fits every blower
    # manifold by its fitting
    "engines/generic/Edelbrock_supercharger_big_block=engines/generic/Edelbrock_supercharger_small_block"
    "engines/generic/Edelbrock_supercharger_drive_belt_big_block=engines/generic/Edelbrock_supercharger_drive_belt_small_block"
) -join ','

$model = @(
    # The GM and Ford packs' carburettors are crude blocks; they are drawn with the Chrysler pack's models of the
    # same carburettors (Chrysler slot geometry comes along, so air cleaners sit where the model's air horn is).
    # GM's factory carburettors get the Carter AVS, a factory carburettor's looks rather than another Holley's
    "engines/generic/Holley_4brl_carburator=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/hardcore_1050cfm_carb=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/Holley_2brl_carb=engines/generic/Carburetors_2BRL_HOLLEY"
    "engines/generic/Holley_2x2brl_carbs=engines/generic/Carburetors_2BRL_HOLLEY"
    "engines/generic/Holley_2x4brl_carburator=engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY"
    "engines/gm/stock_4brl_carburator=engines/chrysler/carter_4barrel_carb"
    "engines/gm/stock_2brl_carburator=engines/generic/Carburetors_2BRL_HOLLEY"
    # The Ford V8 pack's carburettors are the 2010 Mopar pack's models (one of them the crude early block)
    "engines/generic/Holley_street_carburetor=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/Holley_street_4brl_carburetor=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/Edelbrock_4brl_carburetor*=engines/generic/Carburetors_4BRL_Edelbrock"
    "engines/generic/Holley_2x4brl_carburetors=engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY"
) -join ','

$single = @(
    # Models of a row of carburettors (or of air filters, one per carburettor) keep one; builds naming the part get
    # one per pad. Sets whose script values differ from the single carburettor of the pack keep their own part
    "engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY=2@0.22"
    "engines/generic/Carburetors_2x4BRL_King_Demon=2@0.22"
    "engines/generic/Carburetors_3x2BRL_Road_Demon_six_pack=3@0.122"
    "engines/chrysler/Carburetors_2x4BRL_crossram_*=2@0.20"
    "engines/generic/Holley_2x4brl_carburator=2@0.22"
    "engines/generic/Holley_2x2brl_carbs=2@0.18"
    "engines/generic/Holley_2x4brl_filter=2@0.22"
    "engines/gm/GTO65_Airbox=3@0.122"
    "engines/generic/Holley_2x4brl_carburetors=2@0.22"
    "engines/generic/Edelbrock_2x4brl_carburetors*=2@0.22"
) -join ','

$name = @(
    "engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY=HOLLEY Dominator 4BRL Carburetor"
    "engines/generic/Carburetors_2x4BRL_King_Demon=King Demon 4BRL Carburetor"
    "engines/generic/Carburetors_3x2BRL_Road_Demon_six_pack=Road Demon 2BRL Carburetor"
    "engines/chrysler/Carburetors_2x4BRL_crossram_Edelbrock=Edelbrock Crossram 4BRL Carburetor"
    "engines/chrysler/Carburetors_2x4BRL_crossram_HOLLEY*=HOLLEY Crossram 4BRL Carburetor"
    "engines/generic/Holley_2x4brl_carburator=Holley 750 CFM Dominator carburetor"
    "engines/generic/Holley_2x2brl_carbs=Holley Classic series 2-barrel carb (350 cfm)"
    "engines/generic/Holley_2x4brl_filter=Holley round air filter"
    "engines/gm/GTO65_Airbox=Pontiac GTO 389 Tri-Power air filter"
    "engines/generic/Holley_2x4brl_carburetors=Holley street four-barrel carburetor (dual-quad jetting)"
    "engines/generic/Edelbrock_2x4brl_carburetors=Edelbrock Performer 500 CFM four-barrel carburetor (dual-quad jetting)"
    "engines/generic/Edelbrock_2x4brl_carburetors_2=Edelbrock Marine 600 CFM four-barrel carburetor (dual-quad jetting)"
    "engines/generic/Edelbrock_supercharger_small_block=Edelbrock 7.0 psi roots type supercharger"
    "engines/generic/Edelbrock_supercharger_drive_belt_small_block=Edelbrock roots type supercharger drive belt"
) -join ','

# A pad that took a set of carburettors becomes one pad per carburettor, with a slot over the row for the air
# cleaner that spans them. The air slot sits where the set's own air horn was (offset from the pad, after $shift):
# Chrysler four-barrel sets (-0.043, 0.108, -0.040), Six Packs (0.003, 0.073, -0.006), crossram sets
# (-0.043, 0.146, -0.040); every pack's pads use the Chrysler offsets, GM's ovals are shifted to match (see $shift)
$pads = @(
    "engines/chrysler/Intake_manifold_small_2x4brl_*:7=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/chrysler/Intake_manifold_big_2x4brl_*:7=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/chrysler/dualquad_intake:7=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/chrysler/Supercharger_Edelbrock_2:9=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/generic/Supercharger_Weiand:9=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/chrysler/Intake_manifold_small_SIXPACK:7=carb:2bbl*3@0.122@0.003/0.073/-0.006@air:3x2"
    "engines/chrysler/Intake_manifold_big_SIXPACK_*:7=carb:2bbl*3@0.122@0.003/0.073/-0.006@air:3x2"
    # Hemi crossrams take their own carburettors (whose fuel figures are the set's) and any four-barrel; their pads
    # come down to the flange with the rest (see $shift), so the air slot is 6.2 cm further up from the pad
    "engines/chrysler/Intake_manifold_HEMI_Crossram*:7=carb:crossram+carb:4bbl*2@0.20@-0.043/0.208/-0.040@air:crossram"
    "engines/chrysler/Intake_manifold_big_Crossram:7=carb:crossram+carb:4bbl*2@0.15@-0.043/0.208/-0.040@air:crossram"
    "engines/gm/Edelbrock_Dual_Quad_intake_manifold*:7=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/generic/Weiand_8_71_supercharger:9=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/generic/Holley_2x4_charger:9=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/gm/GM_427_2x4brl_intake:7=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/gm/TrickFlow_427_Dual_Tunnel_Ram:7=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/gm/Weiand_500_DualRam:7=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/gm/Weiand_500_charger:9=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/gm/Edelbrock_3x2brl_V8_intake:7=carb:2bbl*3@0.122@0.003/0.073/-0.006@air:3x2"
    "engines/gm/GTO65_intake:7=carb:2bbl*3@0.122@0.003/0.073/-0.006@air:3x2"
    "engines/gm/Vette_C3_intake:7=carb:2bbl*3@0.122@0.003/0.073/-0.006@air:3x2"
    "engines/generic/Holley_Street_supercharger:9=carb:2bbl*2@0.18@0/0.1/0"
    "engines/ford/Trickflow_2x4brl_intake_manifold_*:7=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
) -join ','

$fit = @(
    # Carburettor bases by flange: they go on every manifold pad (or blower top) that takes the flange
    "engines/generic/Carburetors_2BRL_HOLLEY:10=carb:2bbl"
    "engines/generic/Holley_2brl_carb:10=carb:2bbl"
    "engines/gm/stock_2brl_carburator:10=carb:2bbl"
    "engines/generic/Carburetors_4BRL_*:10=carb:4bbl"
    "engines/generic/Holley_4brl_carburator:10=carb:4bbl"
    "engines/generic/hardcore_1050cfm_carb:10=carb:4bbl"
    "engines/chrysler/carter_4barrel_carb:12=carb:4bbl"
    "engines/chrysler/Fireful0_engine_works_carb:12=carb:4bbl"
    "engines/gm/stock_4brl_carburator:10=carb:4bbl"
    # The single carburettors sliced out of sets (the ids still say 2x4/3x2)
    "engines/generic/Carburetors_2x4BRL_*:10=carb:4bbl"
    "engines/generic/Holley_2x4brl_carburator:10=carb:4bbl"
    "engines/generic/Carburetors_3x2BRL_*:10=carb:2bbl"
    "engines/generic/Holley_2x2brl_carbs:10=carb:2bbl"
    "engines/chrysler/Carburetors_2x4BRL_crossram_*:10=carb:crossram"
    # The Ford V8 pack's (2010 Mopar numbering: carburettors mount by 7)
    "engines/generic/Holley_street_carburetor:7=carb:4bbl"
    "engines/generic/Holley_street_4brl_carburetor:7=carb:4bbl"
    "engines/generic/Edelbrock_4brl_carburetor*:7=carb:4bbl"
    "engines/generic/Holley_2x4brl_carburetors:7=carb:4bbl"
    "engines/generic/Edelbrock_2x4brl_carburetors*:7=carb:4bbl"
    # Air cleaners and scoops by what they sit on: one carburettor's horn, or the slot over a row of them
    "engines/generic/Air_cleaner_Edelbrock:11=air:single"
    "engines/generic/Air_cleaner_Edelbrock_Signature_Series:11=air:single"
    "engines/generic/Air_cleaner_Edelbrock_racing:11=air:single"
    "engines/generic/Air_cleaner_HOLLEY:11=air:single"
    "engines/generic/Air_scoop_Edelbrock:11=air:single"
    "engines/generic/Air_scoop_HOLLEY:11=air:single"
    "engines/generic/air_scoop:11=air:single"
    "engines/generic/velocity_stack:11=air:single"
    "engines/generic/KN_filter:12=air:single"
    "engines/generic/MOROSO_air_filter:12=air:single"
    "engines/generic/Summit_air_filter:12=air:single"
    "engines/generic/Summit_scoop_1:12=air:single"
    "engines/generic/Summit_scoop_2:12=air:single"
    "engines/generic/Custom_horns:12=air:single"
    "engines/generic/Holley_2x4brl_filter:12=air:single"
    "engines/generic/Air_cleaner_Edelbrock_Oval_2x4BRL:11=air:2x4"
    "engines/generic/Air_cleaner_KN_2x4BRL:11=air:2x4"
    "engines/generic/Air_cleaner_Six_pack_HOLLEY:11=air:3x2"
    "engines/generic/Air_scoop_Edelbrock_2x4BRL:11=air:2x4"
    "engines/generic/Air_scoop_HOLLEY_2x4BR*:11=air:2x4"
    "engines/generic/dualcarb_scoop:11=air:2x4"
    "engines/generic/Summit_2x4_3x2_air_filter:12=air:2x4+air:3x2"
    "engines/generic/Summit_scoop_3:12=air:2x4"
    "engines/generic/Summit_scoop_4:12=air:2x4"
    "engines/generic/Summit_scoop_5:12=air:2x4"
    # The Ford pack's air filters keep their Ford decals and stay Ford parts, but sit on any carburettor
    "engines/ford/Ford_*_4brl_air_filter*:11=air:single"
    "engines/ford/Edelbrock_4brl_air_filter:11=air:single"
    "engines/ford/Holley_4brl_air_filter:11=air:single"
    "engines/ford/Edelbrock_racing_air_cleaner:11=air:single"
    "engines/ford/Motorcraft_4brl_blower:11=air:single"
    "engines/ford/Edelbrock_2x4brl_air_filter:11=air:2x4"
    "engines/ford/KN_2x4brl_air_filter:11=air:2x4"
    "engines/ford/Motorcraft_2x4brl_blower:11=air:2x4"
    # The Chrysler 2-bbl's air horn only knows Chrysler's factory cleaners: it takes any single cleaner all the same
    "engines/generic/Carburetors_2BRL_HOLLEY:11=takes:air:single"
    # The carburettors sliced out of sets are one carburettor each: their horns, which only the set's cleaner named
    # (it sits over the row now), take a single cleaner like any other carburettor's; the game also reads the
    # cleaner over the row through a horn that takes air (PartScriptRuntime). Same for GM's lone 1050 cfm Dominator
    "engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY:11=takes:air:single"
    "engines/generic/Carburetors_2x4BRL_King_Demon:11=takes:air:single"
    "engines/generic/Carburetors_3x2BRL_Road_Demon_six_pack:11=takes:air:single"
    "engines/generic/hardcore_1050cfm_carb:11=takes:air:single"
    "engines/chrysler/Carburetors_2x4BRL_crossram_*:11=takes:air:single"
    "engines/generic/Holley_2x4brl_carburetors:11=takes:air:single"
    "engines/generic/Edelbrock_2x4brl_carburetors*:11=takes:air:single"
    # Roots blowers on any blower manifold (the drive belt stays the blower's own)
    "engines/generic/Supercharger_Weiand:7=blower:roots"
    "engines/generic/Weiand_8_71_supercharger:8=blower:roots"
    "engines/generic/Weiand_pro_Street_supercharger:8=blower:roots"
    "engines/generic/Holley_Street_supercharger:8=blower:roots"
    "engines/generic/Holley_2x4_charger:8=blower:roots"
    "engines/generic/Edelbrock_supercharger_small_block:7=blower:roots"
) -join ','

$shift = @(
    # Packs place the carburettor joint by different conventions, which cancels within a pack and shows where a
    # carburettor of one pack meets a pad of another. Measured on the meshes: the Chrysler pack (and Dexter's Ford
    # pads) put the carburettor slot 6.5 cm above the carburettor's base and the pad 6 cm above the flange; GM puts
    # both at the flange. Both sides of the Chrysler/Ford joint come down to the flange (nothing moves within the
    # pack). The borrowed Chrysler models carry Chrysler slot geometry, so they come down too; the crossram
    # carburettors and their manifolds come down with them, since the crossram pads take any four-barrel ($pads)
    "engines/generic/Carburetors_*:10=0/-0.062/0"
    "engines/chrysler/Carburetors_2x4BRL_crossram_*:10=0/-0.062/0"
    "engines/chrysler/Intake_manifold_HEMI_Crossram*:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_big_Crossram:7=0/-0.062/0"
    "engines/generic/Holley_2brl_carb:10=0/-0.062/0"
    "engines/generic/Holley_4brl_carburator:10=0/-0.062/0"
    "engines/generic/hardcore_1050cfm_carb:10=0/-0.062/0"
    "engines/generic/Holley_2x4brl_carburator:10=0/-0.062/0"
    "engines/generic/Holley_2x2brl_carbs:10=0/-0.062/0"
    "engines/gm/stock_4brl_carburator:10=0/-0.062/0"
    "engines/gm/stock_2brl_carburator:10=0/-0.062/0"
    "engines/chrysler/E_F_I_Systems_Hilborn:10=0/-0.062/0"
    "engines/chrysler/carter_4barrel_carb:12=0/-0.062/0"
    "engines/chrysler/Fireful0_engine_works_carb:12=0/-0.062/0"
    "engines/chrysler/Intake_manifold_small_2brl_*:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_small_4brl_*:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_small_SIXPACK:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_small_2x4brl_*:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_big_4brl_B:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_big_4brl_O:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_big_4brl_Edelbrock*:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_big_SIXPACK_*:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_big_2x4brl_*:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_HEMI_4brl:7=0/-0.062/0"
    "engines/chrysler/Intake_manifold_HEMI_HOLLEY:7=0/-0.062/0"
    "engines/chrysler/dualquad_intake:7=0/-0.062/0"
    "engines/chrysler/Supercharger_Edelbrock*:9=0/-0.062/0"
    "engines/chrysler/Supercharger_Paxton_Kit:9=0/-0.062/0"
    "engines/generic/Supercharger_Weiand:9=0/-0.062/0"
    # The Ford V8 pack is a 2010 Mopar clone by cfg too (measured with -Measure: its carburettors' slot 7 sits at the
    # carburettor's centre, its pads 2 cm over the manifold top, its Edelbrock blower's pad 9 like Chrysler's): the
    # same way down to the flange
    "engines/generic/Holley_street_carburetor:7=0/-0.062/0"
    "engines/generic/Holley_street_4brl_carburetor:7=0/-0.062/0"
    "engines/generic/Edelbrock_4brl_carburetor*:7=0/-0.062/0"
    "engines/generic/Holley_2x4brl_carburetors:7=0/-0.062/0"
    # (the Edelbrock sets keep their own model, whose base is 7.2 cm below the slot)
    "engines/generic/Edelbrock_2x4brl_carburetors*:7=0/-0.072/0"
    "engines/ford/Ford_*_4brl_intake_manifold:7=0/-0.062/0"
    "engines/ford/Ford_racing_4brl_intake_manifold_*:7=0/-0.062/0"
    "engines/ford/Ford_351_boss_4brl_intake_manifold:7=0/-0.062/0"
    "engines/ford/Cobra_Jet_429_4brl_intake_manifold:7=0/-0.062/0"
    "engines/ford/Holley_4brl_intake_manifold_small_block:7=0/-0.062/0"
    "engines/ford/Trickflow_2x4brl_intake_manifold_*:7=0/-0.062/0"
    "engines/generic/Edelbrock_supercharger_small_block:9=0/-0.062/0"
    # Its air filters mostly carry the Chrysler offsets on the shared meshes already (within 1.5 cm); three sit at
    # their mesh centre where the Chrysler part on the same mesh does not
    "engines/ford/KN_2x4brl_air_filter:11=-0.052/0.015/-0.045"
    "engines/ford/Edelbrock_2x4brl_air_filter:11=-0.050/-0.004/-0.082"
    "engines/ford/Motorcraft_2x4brl_blower:11=-0.050/0.010/-0.045"
    # GM's stock single carburettors were modelled 18 cm ahead of their origin and its single-carburettor pads sit
    # 18 cm back to match; the carburettors draw centred Chrysler models now, so the pads move forward to where
    # GM's own carburettors sat
    "engines/gm/GM_small_block_4brl_intake_manifold:7=0/0/0.18"
    "engines/gm/GM_427_4brl_intake:7=0/0/0.18"
    "engines/gm/Holley_4brl_intake_manifold:7=0/0/0.18"
    "engines/gm/Holley_strip_manifold:7=0/0/0.18"
    "engines/gm/Stock_4brl_intake_manifold:7=0/0/0.18"
    "engines/gm/GM_small_block_2brl_intake_manifold:7=0/0/0.18"
    "engines/gm/V8_2brl_intake_manifold:7=0/0/0.18"
    # Single-carburettor pads that stand in for GM's stock pad, and the '63 fuelie's rail that sits on one of them
    "engines/gm/GM_500_4brl_intake:7=0/0/0.18"
    "engines/gm/Motown_220cc_intake:7=0/0/0.18"
    "engines/gm/Vette_63_intake:7=0/0/0.18"
    "engines/gm/Vette_63_fuel_rail:10=0/0/0.18"
    "engines/generic/Weiand_pro_Street_supercharger:9=0/0/0.18"
    # GM's ovals were modelled for GM's set air horn, 5 cm right, 3 cm lower and 9 cm ahead of the Chrysler one the
    # shared air slots use
    "engines/generic/Summit_2x4_3x2_air_filter:12=-0.053/0.026/-0.09"
    "engines/gm/Vette_C3_Airbox:12=-0.053/0.026/-0.09"
    # The Holley Street blower's carburettor pad sat at the blower's origin, its 2x2 set 21 cm above it; the pad
    # goes up to the blower top where the single carburettors' bases are
    "engines/generic/Holley_Street_supercharger:9=0/0.212/0.163"
) -join ','

# Slots nudged into place in the garage (F5 in the workbench) are kept in tools\slot_shifts.json for good. The game
# writes them next to the content it runs on, which is a build folder: those files are folded in and taken away
# One --absorb per file: a path may hold a comma. None at all when there are none (an empty argument is dropped on the
# way to the converter, which would then read --absorb as the pack filter)
$absorbArgs = @(Get-ChildItem (Join-Path $PSScriptRoot "..\Street Rod AC\bin") -Recurse -Filter slot_shifts.json -ErrorAction SilentlyContinue |
    ForEach-Object { "--absorb"; $_.FullName })
$measureArgs = if ($Measure) { @("--measure", $Measure) } else { @() }

# The content in the repo is what saves were made with: ids it had that this run does not produce any more stay
# answered (a mod release that renamed its files, aliases of earlier runs), whatever folder this run writes to
$previous = Join-Path $PSScriptRoot "..\Street Rod AC\Assets\Parts"

& $Converter $Slrr $Output --notes $Notes --replace $replace --twin $twin --rename $rename --drop $drop --merge $merge --model $model --single $single --name $name --pads $pads --fit $fit --shift $shift --shifts (Join-Path $PSScriptRoot "slot_shifts.json") --previous $previous @absorbArgs @measureArgs
exit $LASTEXITCODE
