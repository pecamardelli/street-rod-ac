# Makes the game's parts content out of the SLRR install: the packs named after what they hold, sorted into
# engines/rims/tyres/exhaust/suspension/brakes, fictional, modern and non-mechanical parts left out.
# See docs/systems/parts-system.md for what each rule is there for.
param(
    [string]$Slrr = "D:\JUEGOS\Street Legal Racing - Redline",
    [string]$Output = (Join-Path $PSScriptRoot "..\Street Rod AC\Assets\Parts"),
    [string]$Notes = "D:\JUEGOS\SLRR\SCRIPTS",
    [string]$Converter = (Join-Path $PSScriptRoot "SlrrPartsConverter\bin\Release\net10.0\SlrrPartsConverter.exe")
)

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
    "engines/DEXTERV8s:Universal_blower=engines/generic"
    "engines/DEXTERV8s:Universal_blower_scoop=engines/generic"
    "engines/DEXTERV8s:Universal_4_barrel_carburetor=engines/generic"
    "engines/DEXTERV8s:Universal_dual_4_barrel_carburetors*=engines/generic"
    # Engine packs by brand
    "engines/Chrysler_V8_pak=engines/chrysler"
    "engines/GM_V8_pak=engines/gm"
    "engines/DEXTERV8s=engines/ford"
    "engines/fordi6_data=engines/ford_six"
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
    "engines/ford/Dodge_*"
    "engines/ford/*_Mopar_head_cover"
    "engines/ford/*Chevrolet_*"
    "engines/ford/Drag_*"
    "engines/chrysler/_Engine_block_360BP"
    "engines/gm/GMP_427_block"
    # Dexter's 2-bar "Super Blower" on the BDS mesh
    "engines/ford/Universal_blower_2"
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
    "engines/ford/Universal_round_air_filter=engines/generic/Air_cleaner_Edelbrock"
    "engines/ford/Universal_oval_air_filter=engines/generic/Air_cleaner_Edelbrock_Oval_2x4BRL"
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
    "engines/ford/Universal_adjustable_6_speed_AWD_transmission=engines/ford/VR4E_SUPER_LOCK_UP_Racing_Transmission"
    "engines/ford_six/Baiern_Devils_*_transmission=engines/ford_six/Baiern_Tourist_transmission"
) -join ','

$model = @(
    # The GM and Ford packs' carburettors are crude blocks; they are drawn with the Chrysler pack's models of the
    # same carburettors (Chrysler slot geometry comes along, so air cleaners sit where the model's air horn is).
    # GM's factory carburettors get the Carter AVS, a factory carburettor's looks rather than another Holley's
    "engines/generic/Holley_4brl_carburator=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/hardcore_1050cfm_carb=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/Universal_4_barrel_carburetor=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/Holley_2brl_carb=engines/generic/Carburetors_2BRL_HOLLEY"
    "engines/generic/Holley_2x2brl_carbs=engines/generic/Carburetors_2BRL_HOLLEY"
    "engines/generic/Holley_2x4brl_carburator=engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY"
    "engines/generic/Universal_dual_4_barrel_carburetors*=engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY"
    "engines/gm/stock_4brl_carburator=engines/chrysler/carter_4barrel_carb"
    "engines/gm/stock_2brl_carburator=engines/generic/Carburetors_2BRL_HOLLEY"
) -join ','

$single = @(
    # Models of a row of carburettors (or of air filters, one per carburettor) keep one; builds naming the part get
    # one per pad. Sets whose script values differ from the single carburettor of the pack keep their own part
    "engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY=2@0.22"
    "engines/generic/Carburetors_2x4BRL_King_Demon=2@0.22"
    "engines/generic/Carburetors_3x2BRL_Road_Demon_six_pack=3@0.122"
    "engines/chrysler/Carburetors_2x4BRL_crossram_*=2@0.20"
    "engines/generic/Holley_2x4brl_carburator=2@0.22"
    "engines/generic/Universal_dual_4_barrel_carburetors*=2@0.22"
    "engines/generic/Holley_2x2brl_carbs=2@0.18"
    "engines/generic/Holley_2x4brl_filter=2@0.22"
    "engines/gm/GTO65_Airbox=3@0.122"
) -join ','

$name = @(
    "engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY=HOLLEY Dominator 4BRL Carburetor"
    "engines/generic/Carburetors_2x4BRL_King_Demon=King Demon 4BRL Carburetor"
    "engines/generic/Carburetors_3x2BRL_Road_Demon_six_pack=Road Demon 2BRL Carburetor"
    "engines/chrysler/Carburetors_2x4BRL_crossram_Edelbrock=Edelbrock Crossram 4BRL Carburetor"
    "engines/chrysler/Carburetors_2x4BRL_crossram_HOLLEY*=HOLLEY Crossram 4BRL Carburetor"
    "engines/generic/Holley_2x4brl_carburator=Holley 750 CFM Dominator carburetor"
    "engines/generic/Universal_dual_4_barrel_carburetors=Holley 1150 Four Barrel Race Carburetor"
    "engines/generic/Universal_dual_4_barrel_carburetors_2=Drag Race Methanol Carburetor"
    "engines/generic/Holley_2x2brl_carbs=Holley Classic series 2-barrel carb (350 cfm)"
    "engines/generic/Holley_2x4brl_filter=Holley round air filter"
    "engines/gm/GTO65_Airbox=Pontiac GTO 389 Tri-Power air filter"
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
    # Hemi crossrams take their own carburettors (whose fuel figures are the set's) and any four-barrel
    "engines/chrysler/Intake_manifold_HEMI_Crossram*:7=carb:crossram+carb:4bbl*2@0.20@-0.043/0.146/-0.040@air:crossram"
    "engines/chrysler/Intake_manifold_big_Crossram:7=carb:crossram+carb:4bbl*2@0.15@-0.043/0.146/-0.040@air:crossram"
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
    "engines/ford/Ford_dual_4_barrel_intake_manifold:7=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
    "engines/generic/Universal_blower:9=carb:4bbl*2@0.22@-0.043/0.108/-0.040@air:2x4"
) -join ','

$fit = @(
    # Carburettor bases by flange: they go on every manifold pad (or blower top) that takes the flange
    "engines/generic/Carburetors_2BRL_HOLLEY:10=carb:2bbl"
    "engines/generic/Holley_2brl_carb:10=carb:2bbl"
    "engines/gm/stock_2brl_carburator:10=carb:2bbl"
    "engines/generic/Carburetors_4BRL_*:10=carb:4bbl"
    "engines/generic/Holley_4brl_carburator:10=carb:4bbl"
    "engines/generic/hardcore_1050cfm_carb:10=carb:4bbl"
    "engines/generic/Universal_4_barrel_carburetor:10=carb:4bbl"
    "engines/chrysler/carter_4barrel_carb:12=carb:4bbl"
    "engines/chrysler/Fireful0_engine_works_carb:12=carb:4bbl"
    "engines/gm/stock_4brl_carburator:10=carb:4bbl"
    # The single carburettors sliced out of sets (the ids still say 2x4/3x2)
    "engines/generic/Carburetors_2x4BRL_*:10=carb:4bbl"
    "engines/generic/Holley_2x4brl_carburator:10=carb:4bbl"
    "engines/generic/Universal_dual_4_barrel_carburetors*:10=carb:4bbl"
    "engines/generic/Carburetors_3x2BRL_*:10=carb:2bbl"
    "engines/generic/Holley_2x2brl_carbs:10=carb:2bbl"
    "engines/chrysler/Carburetors_2x4BRL_crossram_*:10=carb:crossram"
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
    "engines/generic/Universal_blower_scoop:12=air:2x4"
    # The Chrysler 2-bbl's air horn only knows Chrysler's factory cleaners: it takes any single cleaner all the same
    "engines/generic/Carburetors_2BRL_HOLLEY:11=takes:air:single"
    # Roots blowers on any blower manifold (the drive belt stays the blower's own)
    "engines/generic/Supercharger_Weiand:7=blower:roots"
    "engines/generic/Weiand_8_71_supercharger:8=blower:roots"
    "engines/generic/Weiand_pro_Street_supercharger:8=blower:roots"
    "engines/generic/Holley_Street_supercharger:8=blower:roots"
    "engines/generic/Holley_2x4_charger:8=blower:roots"
    "engines/generic/Universal_blower:8=blower:roots"
) -join ','

$shift = @(
    # Packs place the carburettor joint by different conventions, which cancels within a pack and shows where a
    # carburettor of one pack meets a pad of another. Measured on the meshes: the Chrysler pack (and Dexter's Ford
    # pads) put the carburettor slot 6.5 cm above the carburettor's base and the pad 6 cm above the flange; GM puts
    # both at the flange. Both sides of the Chrysler/Ford joint come down to the flange (nothing moves within the
    # pack). The borrowed Chrysler models carry Chrysler slot geometry, so they come down too; the crossram
    # carburettors and their Hemi manifolds fit only each other and stay
    "engines/generic/Carburetors_*:10=0/-0.062/0"
    "engines/generic/Holley_2brl_carb:10=0/-0.062/0"
    "engines/generic/Holley_4brl_carburator:10=0/-0.062/0"
    "engines/generic/hardcore_1050cfm_carb:10=0/-0.062/0"
    "engines/generic/Holley_2x4brl_carburator:10=0/-0.062/0"
    "engines/generic/Universal_4_barrel_carburetor:10=0/-0.062/0"
    "engines/generic/Universal_dual_4_barrel_carburetors*:10=0/-0.062/0"
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
    "engines/ford/Ford_4_barrel_intake_manifold:7=0/-0.062/0"
    "engines/ford/Ford_dual_4_barrel_intake_manifold:7=0/-0.062/0"
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

& $Converter $Slrr $Output --notes $Notes --replace engines/Mopar=engines/chrysler --rename $rename --drop $drop --merge $merge --model $model --single $single --name $name --pads $pads --fit $fit --shift $shift
