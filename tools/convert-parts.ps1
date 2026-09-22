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
    # Modern tuner wheels, and everything that is body or interior
    "wheels/*"
    "interior/*"
    "wings/*"
    "body/*"
) -join ','

& $Converter $Slrr $Output --notes $Notes --replace engines/Mopar=engines/chrysler --rename $rename --drop $drop
