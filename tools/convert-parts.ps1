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
    # The GM and Ford packs' Holleys are crude blocks; they are drawn with the Chrysler pack's models of the same
    # carburettors (Chrysler slot geometry comes along, so air cleaners sit where the model's air horn is)
    "engines/generic/Holley_4brl_carburator=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/hardcore_1050cfm_carb=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/Universal_4_barrel_carburetor=engines/generic/Carburetors_4BRL_street_HOLLEY"
    "engines/generic/Holley_2brl_carb=engines/generic/Carburetors_2BRL_HOLLEY"
    "engines/generic/Holley_2x4brl_carburator=engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY"
    "engines/generic/Universal_dual_4_barrel_carburetors*=engines/generic/Carburetors_2x4BRL_Dominator_HOLLEY"
    "engines/generic/Holley_3x2brl_carbs=engines/generic/Carburetors_3x2BRL_HOLLE_six_pack"
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
    "engines/generic/Carburetors_2x4BRL_*:10=carb:2x4"
    "engines/generic/Holley_2x4brl_carburator:10=carb:2x4"
    "engines/generic/Universal_dual_4_barrel_carburetors*:10=carb:2x4"
    "engines/gm/stock_2x4brl_carburator:10=carb:2x4"
    "engines/generic/Carburetors_3x2BRL_*:10=carb:3x2"
    "engines/generic/Holley_3x2brl_carbs:10=carb:3x2"
    # Air cleaners and scoops by the carburettors they sit on: one carburettor, or an inline pair or triple
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
    "engines/generic/Air_cleaner_Edelbrock_Oval_2x4BRL:11=air:inline"
    "engines/generic/Air_cleaner_KN_2x4BRL:11=air:inline"
    "engines/generic/Air_cleaner_Six_pack_HOLLEY:11=air:inline"
    "engines/generic/Air_scoop_Edelbrock_2x4BRL:11=air:inline"
    "engines/generic/Air_scoop_HOLLEY_2x4BR*:11=air:inline"
    "engines/generic/dualcarb_scoop:11=air:inline"
    "engines/generic/Summit_2x4_3x2_air_filter:12=air:inline"
    "engines/generic/Holley_2x4brl_filter:12=air:inline"
    "engines/generic/Summit_scoop_3:12=air:inline"
    "engines/generic/Summit_scoop_4:12=air:inline"
    "engines/generic/Summit_scoop_5:12=air:inline"
    "engines/generic/Universal_blower_scoop:12=air:inline"
    # Roots blowers on any blower manifold (the drive belt stays the blower's own)
    "engines/generic/Supercharger_Weiand:7=blower:roots"
    "engines/generic/Weiand_8_71_supercharger:8=blower:roots"
    "engines/generic/Weiand_pro_Street_supercharger:8=blower:roots"
    "engines/generic/Holley_Street_supercharger:8=blower:roots"
    "engines/generic/Holley_2x4_charger:8=blower:roots"
    "engines/generic/Universal_blower:8=blower:roots"
) -join ','

& $Converter $Slrr $Output --notes $Notes --replace engines/Mopar=engines/chrysler --rename $rename --drop $drop --merge $merge --model $model --fit $fit
