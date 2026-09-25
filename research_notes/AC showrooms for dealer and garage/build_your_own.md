# Building or sourcing our own AC showroom (garage interior, car dealership) from generic assets

Scope: what an AC showroom is technically, how the AcTools "Dark" renderer (CM Custom Showroom, which Street Rod AC hosts in-process via `GarageRenderer` in `Street Rod AC/Controls/DealerLotViewport3D.cs`) treats it, how to author one, where to get permissively licensed assets, and whether a panorama-only showroom is a cheap option. Researched 2026-09-23. No files were downloaded.

## 1. What an AC showroom folder is, and how the Dark renderer lights it

### Takeaway
A showroom is just `content/showroom/<id>/` holding `<id>.kn5` (a track-style kn5: static meshes, materials, embedded textures) plus `ui/ui_showroom.json`, `preview.jpg`, and optionally `track.wav`/`<id>.bank`. In the Dark renderer the showroom is drawn as a normal static node that receives and casts shadows; the scene's light/ambient/reflection comes from renderer properties (light colour/brightness, AmbientUp/AmbientDown, CubemapAmbient, reflection cubemap), not from anything baked into the showroom. A showroom where every material is "self-lit" (ksDiffuse = 0, ksAmbient >= 1, the panorama trick) turns shadows off.

### Cited Findings
- CM's `ShowroomObject` treats a showroom as the files `{id}.kn5`, `preview.jpg`, `track.wav`, `ui/ui_showroom.json` (the pack routine adds exactly these), with an optional `{id}.bank` sound bank; from the JSON it parses the usual UI fields (name, author, version, url come through the generic AC-object JSON) and explicitly `year` (falling back to guessing the year from the name/id) — [actools ShowroomObject.cs](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/ShowroomObject.cs)
- Install location is `assettocorsa/content/showroom/<id>/` — [assettocorsamods.net: Custom Showroom Scene](https://assettocorsamods.net/threads/custom-showroom-scene.1283/)
- `DarkKn5ObjectRenderer` takes the showroom kn5 path as a constructor argument; with a showroom loaded `BackgroundBrightness` defaults to 2 instead of 1 and the UI colour is forced white — [DarkKn5ObjectRenderer.cs](https://raw.githubusercontent.com/gro-ove/actools/master/AcTools.Render/Kn5SpecificForwardDark/DarkKn5ObjectRenderer.cs)
- The showroom node is drawn in the shadow pass (`ShowroomNode?.Draw(holder, camera, SpecialRenderMode.Shadow)`) and in the Simple / SimpleTransparent passes, i.e. like any other static geometry; no special mesh or material names are looked up — same source
- The renderer inspects showroom materials with `ksDiffuse.ValueA == 0f && ksAmbient.ValueA >= 1f`; when the showroom's materials are all like that (fully self-illuminated, as panorama showrooms are) shadows are disabled — same source
- The flat-mirror floor option (`FlatMirror`, `FlatMirrorBlurred`, `FlatMirrorReflectiveness`) is skipped whenever a showroom node exists, so a showroom must supply its own floor — same source
- Ambient is a two-colour hemisphere: shader gets `AmbientDown * AmbientBrightness` and `(AmbientUp - AmbientDown) * AmbientBrightness`; there is also `CubemapAmbient` (ambient from per-car light probes), `LightColor`/`LightBrightness` (the single sun/key light), `BackgroundColor`/`BackgroundBrightness`, `MaterialsReflectiveness`, `CustomReflectionBrightness`, and `UseCustomReflectionCubemap` (a fixed cubemap centred at origin instead of a dynamic one at the car) — same source (property list as extracted by fetch; exact defaults not verified)
- The existing Street Rod AC lot viewport already sets `EnableShadows`, `UsePcss`, `UseSslr`, `UseAo`, `UseBloom` on `GarageRenderer` and notes `CubemapAmbient = 0` as the first knob if a full lot is slow — local code `Street Rod AC/Controls/DealerLotViewport3D.cs` lines 374-400
- Lights added in CM Custom Showroom are scene lights of the showroom session only (not stored in the kn5) — [OverTake.gg: Content Manager Lights](https://www.overtake.gg/threads/content-manager-lights.188508/)
- CM stores look settings as presets in `%LOCALAPPDATA%\AcTools Content Manager\Presets\Custom Showroom` (and `...\Custom Previews` for preview generation); community showrooms often ship a matching preset — [OverTake.gg: simple black showroom](https://www.overtake.gg/threads/just-a-simple-black-showroom-including-reflection-for-cm.184015/); [acstuff.club: Custom Showroom presets](https://acstuff.club/f/d/23-content-manager-custom-showroom-presets/16)

### Inferences
- For our own showroom the kn5 only needs geometry, materials and textures; lighting mood is set in code (light direction/colour, AmbientUp/Down, background) per scene. We should pair each showroom with a small preset/struct of renderer values (like CM presets) rather than trying to bake lights into the kn5.
- Because the Dark renderer does real-time shadowing of the showroom, a modelled room (floor, walls) gets car contact shadows for free; a pure panorama sphere does not (shadows off) unless it has a separate diffuse-lit floor mesh (see section 4).
- `ui_showroom.json` is only for CM's listing; our game reads the kn5 path directly, so `ui/`/`preview.jpg` are optional for us but cheap to include for CM compatibility.

### Gaps
- Default values of AmbientUp/AmbientDown/light for showroom mode were not verified line-by-line; the fetch tool summarised the file. Check the source directly before tuning.
- Whether the dynamic reflection cubemap includes the showroom geometry could not be confirmed from the summary (the fetch was ambiguous); worth a quick test (chrome part reflecting walls vs. background colour).
- No official Kunos documentation of `ui_showroom.json` fields was found.

## 2. Authoring workflow: Blender/3ds Max -> FBX -> ksEditor -> kn5, and direct exporters

### Takeaway
The standard route is: model/assemble in Blender or Max, export FBX (scale 0.01 from Max-style units or metres correctly set), load in the AC SDK's ksEditor (`assettocorsa/sdk/editor`), assign shaders (ksPerPixel, ksPerPixelMultiMap, etc.) and textures per material, then "Save kn5 as Track". Blender add-ons can write kn5 directly (moppius tools, track-oriented; LKOLA add-on, car-oriented, MIT). Since a showroom is a static track-style kn5, the track exporters fit.

### Cited Findings
- Showroom recipe from Steel Racing's customizable showroom: open ksEditor, import the FBX (sources in `assettocorsa\sdk\editor`), customise shader details and import textures, then **Save KN5 as Track** and overwrite the showroom kn5 — [Steel Racing Patreon: Steel Showroom 04 "The Customizable"](https://www.patreon.com/steelracing/posts/release-steel-04-62561972)
- FBX should be exported at scale 0.01 with a texture folder containing every texture; in ksEditor's Materials tab check each material has its texture and pick the shader (ksPerPixel, ksPerPixelMultiMap) and channels — [Assetto Corsa Conversion guide: Export the car as kn5](https://cimmerian.gitbook.io/assetto-corsa-conversion/the-basics/export-the-car-as-kn5)
- newksEditor, a community replacement editor — [GitHub 619motorsports/newksEditor](https://github.com/619motorsports/newksEditor)
- moppius/blender-assetto-corsa-tools: File -> Export -> Assetto Corsa (.kn5), kn5 format v5, mesh objects and image textures exported, per-material/object settings via JSON, multiple materials per object; no skinned meshes; built for track scenes; derived from Thomas Hagnhofer's original exporter — [GitHub moppius/blender-assetto-corsa-tools](https://github.com/moppius/blender-assetto-corsa-tools/blob/main/README.md); [Hagn's Site: Blender Kn5 Exporter](https://site.hagn.io/assettocorsa/blender-kn5-exporter)
- LKOLA/blender-kn5-addon: import and export kn5 inside Blender without ksEditor, MIT license, documents ksStandard/ksLight/ksTree shaders, README targets car models and states Blender 5.1.2+/Python 3.11+ (as read from README); textures referenced by path — [GitHub LKOLA/blender-kn5-addon](https://github.com/LKOLA/blender-kn5-addon/blob/main/README.md)
- leBluem/io_import_accsv: imports kn5 and has experimental export to track kn5 — [GitHub leBluem/io_import_accsv](https://github.com/leBluem/io_import_accsv)
- A Blender add-on for import/export of .kn5/.ai/.csv/.ini exists on OverTake — [OverTake.gg Blender addon](https://www.overtake.gg/downloads/blender-addon-import-export-kn5-ai-csv-ini-files.35230/updates)
- Panorama-retexture route used by a community author: take ~100 screenshots, stitch a 360 panorama with Autostitch, and replace the texture of an existing showroom in 3DSimED — [assettocorsamods.net: Custom Showroom Scene](https://assettocorsamods.net/threads/custom-showroom-scene.1283/)

### Inferences
- Street Rod AC already has its own kn5 writer in the parts converter (see memory "Parts conversion pipeline"), so the most self-contained path is: Blender -> FBX/glTF -> our converter tool -> showroom kn5, reusing that writer with track-style (non-skinned) nodes. ksEditor stays the fallback for shader tuning.
- Keep shader set to what the Dark renderer implements well: ksPerPixel (diffuse), ksPerPixelMultiMap (spec/reflection mask), ksPerPixelNM (normal maps), plus self-lit materials (ksAmbient=1, ksDiffuse=0) for signs, light panels and panorama backdrops.

### Gaps
- No up-to-date (2025-2026) video tutorial specifically for showrooms was verified; most guides are for tracks/cars. ksEditor track tutorials (overtake.gg, YouTube) apply by analogy.
- LKOLA's Blender version claim was not independently checked.

## 3. Permissively licensed assets: garages, workshops, dealerships, parking, HDRIs

### Takeaway
Poly Haven is the safest source: every HDRI is CC0 and there are several directly on-theme (Autoshop 01, Auto Service, Garage, Abandoned Garage, Parking Garage, Skylit Garage, Empty Workshop, Industrial/Mechanics Workshop, Empty Warehouse, Warehouse Loading Dock, Hangar). Sketchfab has CC-BY dealership/garage models but quality and polycount vary wildly (from 132 triangles to 7 million). BlenderKit's Royalty-Free licence and Fab's Standard licence are usable but have resale/extraction clauses that matter because kn5 files are trivially extractable.

### Cited Findings
Licences:
- Poly Haven: all assets CC0, any purpose including commercial, no attribution required — [Poly Haven License](https://polyhaven.com/license)
- BlenderKit: assets are Royalty Free or CC0; RF allows commercial use without attribution but forbids re-selling the model, and games may be sold "if these can't be extracted by the users in an easy way" — [BlenderKit Licensing FAQ](https://www.blenderkit.com/docs/licenses/licensing-faq/); [BlenderKit Licenses](https://www.blendkit.com/docs/licenses/)
- Fab/Quixel: Megascans were free under Fab's Standard License (usable in all engines and tools) until end of 2024; assets claimed then remain usable forever; from 2025 most Megascans are paid individually — [CG Channel](https://www.cgchannel.com/2024/10/epic-games-has-made-megascans-free-to-all-but-only-until-the-end-of-2024/); [Fab Transition FAQs](https://support.fab.com/s/article/Fab-Transition-FAQs?language=en_US)
- Sketchfab CC0-tagged models and a curated CC0 collection exist — [Sketchfab tag cc0](https://sketchfab.com/tags/cc0); [Thomas Flynn CC0 collection](https://sketchfab.com/nebulousflynn/collections/cc0-9e9b8c5442ab4b59ba16b6fa5e43b8da); OpenGameArt CC0 low-poly packs — [OpenGameArt CC0 Assets 3D Low Poly](https://opengameart.org/content/cc0-assets-3d-low-poly)

Poly Haven HDRIs (all CC0; slugs from the Poly Haven API indoor listing, [api.polyhaven.com](https://api.polyhaven.com/assets?type=hdris&categories=indoor)):
- **Autoshop 01** — industrial garage, even low-contrast fluorescent + skylight, tags garage/warehouse/car/workshop, 17K, by Oliksiy Yakovlyev — [polyhaven.com/a/autoshop_01](https://polyhaven.com/a/autoshop_01)
- **Auto Service** — concrete auto workshop, warm ceiling lights + daylight through open door, 16K, includes backplates, by Sergej Majboroda — [polyhaven.com/a/auto_service](https://polyhaven.com/a/auto_service)
- **Garage** — interior garage, fluorescent, concrete, shelves — [polyhaven.com/a/garage](https://polyhaven.com/a/garage)
- **Parking Garage** — underground car park, 16K unclipped, lamps + openings, concrete highlights — [polyhaven.com/a/parking_garage](https://polyhaven.com/a/parking_garage)
- **Skylit Garage** — parking garage category, pillars, backplates — [polyhaven.com/a/skylit_garage](https://polyhaven.com/a/skylit_garage)
- Outdoor lot candidates: **Beach Parking**, **Park Parking**, **Future Parking** — [beach_parking](https://polyhaven.com/a/beach_parking), [park_parking](https://polyhaven.com/a/park_parking), [future_parking](https://polyhaven.com/a/future_parking)
- Further on-theme slugs listed by the API (pages not individually opened): abandoned_garage (30000x15000), abandoned_workshop, abandoned_workshop_02, empty_workshop, empty_warehouse_01, burnt_warehouse, boiler_room, carpentry_shop_01/02, hangar_01, warehouse_loading_dock (24576x12288), parking_garage_01, industrial_workshop, mechanics_workshop, plus neutral photo studios (brown_photostudio_01..07, ferndale_studio_01..08, cyclorama_hard_light) for a "new-car dealer" white-room look — [Poly Haven API](https://api.polyhaven.com/assets?type=hdris&categories=indoor)

Sketchfab models (CC Attribution 4.0 unless noted; free download):
- "Garage- Dealership Showroom" by MattDoesBlender — CC BY 4.0, **7 million triangles**, published June 2025 — [Sketchfab f03456b](https://sketchfab.com/3d-models/garage-dealership-showroom-f03456bd287d4d13966157dd02d95c75)
- "Car Dealership" by ImperialBlue3D — CC BY 4.0, 132 triangles, PS1-style low poly — [Sketchfab bb07901](https://sketchfab.com/3d-models/car-dealership-bb07901c912a43e1a5c2e9f6c74ee4c6)
- "Car showroom free" by Scooma Dev — CC BY per search listing — [Sketchfab fb80a44](https://sketchfab.com/3d-models/car-showroom-free-fb80a442e1ab41d680bbfab89217a89e)
- "Car dealership" by iliayurchenkov — CC BY per search listing — [Sketchfab 8b73872](https://sketchfab.com/3d-models/car-dealership-8b7387239be7470f8d583c449d4388fb)
- "Car-Showroom 1" / "Car-Showroom 2" by Polsaris — [Sketchfab 40b0ae0](https://sketchfab.com/3d-models/car-showroom-1-40b0ae06eb8343e5bacf34e04fcfff73), [Sketchfab e7a3497](https://sketchfab.com/3d-models/car-showroom-2-e7a3497e8a7c487e906b2d8814b018f0) (licence not verified)
- "Garage Interior" by dhimasdc9 — CC BY per search listing, ~116.6k triangles — [Sketchfab 7e58e5e](https://sketchfab.com/3d-models/garage-interior-7e58e5e59a2e4ea8a7652b2b9413056d)
- "Garage Room" by hamza69; "Low poly car repair" by zubenko78 — CC BY per search listing — [Sketchfab 623fb10](https://sketchfab.com/3d-models/garage-room-623fb10f6cb74d5993c67e53f8119bc9), [Sketchfab 93588da](https://sketchfab.com/3d-models/low-poly-car-repair-93588daf070a45ea9fd6e7fcfc6f0e1c)
- Browse: [Sketchfab tag car-showroom](https://sketchfab.com/tags/car-showroom), [tag dealership](https://sketchfab.com/tags/dealership); CGTrader free filter — [CGTrader car showroom](https://www.cgtrader.com/3d-models/car-showroom)

### Inferences
- CC0 (Poly Haven, CC0-tagged Sketchfab, Kenney/OpenGameArt) is the zero-friction choice; CC-BY is fine if we ship an attribution/credits file. BlenderKit RF and Fab Standard assets are riskier here because any kn5 can be unpacked with CM, which arguably makes them "easily extractable" and close to redistribution.
- For a hot-rod garage, Poly Haven's Autoshop 01 / Auto Service / Garage / Empty Workshop are the best mood matches; Parking Garage / Beach Parking / Park Parking suit a used-car lot; photo-studio HDRIs suit a new-car dealer.
- Sketchfab licence labels should be re-checked on each model page at download time (the licence shown can be changed by the author; several above were only confirmed via search snippets).

### Gaps
- Poly Haven 3D models (props like shelves, tool chests, barrels) were not enumerated.
- Kenney, TurboSquid-free, and Quixel/Fab specific garage kits were not individually checked (TurboSquid/CGTrader "free" use royalty-free licences with redistribution limits, not CC0 — not verified in this session).

## 4. Panorama/HDRI-only showrooms as the cheap option

### Takeaway
Yes. Community "HDRI showrooms" are a half sphere (dome) UV-mapped with an equirectangular panorama and a self-lit material; they cost almost nothing to render. Downsides in the Dark renderer: all-self-lit showrooms disable shadows, and cars float over a painted floor with no contact shadow or parallax. A hybrid (HDRI dome + a real diffuse floor disc/plane, maybe a few walls) keeps shadows and still takes an afternoon.

### Cited Findings
- HDRI showrooms consist of "a sphere cut in half with a 360º panoramic HDRI texture placed on it"; they ship HDR and LDR variants; free HDRIs came from Poly Haven and HDRMaps — [Steam Workshop: More HDRI Showrooms](https://steamcommunity.com/sharedfiles/filedetails/?id=2786839495); [OverTake.gg: 3 HDRI Showrooms](https://www.overtake.gg/downloads/3-hdri-showrooms.39390/)
- To make one from a 3D scene: set up materials and lighting, render an equirectangular environment map, UV-map it onto a half sphere used as the showroom — same Steam Workshop source
- A user reported one set of HDRI showrooms looking overexposed/washed out — [OverTake.gg thread](https://www.overtake.gg/threads/3-hdri-showrooms.197570/)
- Alternative: stitch a panorama from in-game screenshots (Autostitch) and retexture an existing showroom's dome — [assettocorsamods.net](https://assettocorsamods.net/threads/custom-showroom-scene.1283/)
- Dark renderer turns off shadows when showroom materials are ksDiffuse=0 / ksAmbient>=1 (the self-lit panorama setup) — [DarkKn5ObjectRenderer.cs](https://raw.githubusercontent.com/gro-ove/actools/master/AcTools.Render/Kn5SpecificForwardDark/DarkKn5ObjectRenderer.cs)

### Inferences
- The kn5 stores LDR textures (DDS/PNG), so an HDRI must be tone-mapped to 8-bit for the dome; the "overexposed" complaint suggests tuning exposure and setting the renderer's light/ambient to match the panorama (pick AmbientUp/Down colours sampled from the HDRI's ceiling/floor).
- Good cheap recipe for us: (a) dome radius ~ 30-50 m, self-lit, LDR 8192x4096 (or 4096x2048) tone-mapped from a Poly Haven HDRI; (b) a separate floor mesh with normal diffuse shading and a floor texture cropped/projected from the panorama's lower hemisphere or a CC0 concrete texture, so shadows stay on; (c) camera kept near the capture point, since panoramas break when the camera moves far (parallax).
- Poly Haven "backplates" (Auto Service ships them) could serve as flat backdrops for fixed dealer-lot camera angles.
- Our `ShowroomSpec` (floor extent, wall radius) can be authored exactly for such a dome, since we know the dome/floor radius at build time.

### Gaps
- Exact dome sizes, texture resolutions and shader settings used by the community HDRI showrooms are not documented in their threads.

## 5. Performance and size norms

### Takeaway
There is no published Kunos budget for showrooms. Evidence points to keeping them lean: community complaints of CM showroom lag trace back to integrated-GPU selection and heavy CSP-light showrooms, and the models available range up to millions of triangles that must be decimated. A sensible target for our in-process renderer (which also renders up to ~12 cars per lot) is well under 1M triangles, few materials/draw calls, and 2K-4K textures (panorama up to 8K).

### Cited Findings
- CM Custom Showroom lag reports (down to ~1 FPS) are often because CM runs on the integrated GPU; fix is forcing the dedicated GPU — [OverTake.gg: CM Showroom is laggy](https://www.overtake.gg/threads/cm-showroom-is-laggy-af.175571/)
- High-quality showrooms with many CSP lights can perform very poorly (the Dark renderer ignores CSP anyway) — [AC Supply performance guide](https://www.acsupply.cx/guides/performance-optimization)
- Kunos pipeline doc stresses optimising draw calls and materials and using LODs (car guidance: high-res LOD used for showroom view) — [AC Pipeline Rev2.0 (Scribd)](https://www.scribd.com/document/501206878/AC-Pipeline-PUB-Rev2-0)
- Community polycount discussion thread — [assettocorsamods.net: Polycounts?](https://assettocorsamods.net/threads/polycounts.944/)
- Street Rod AC's own comment notes some existing showrooms are "a few hundred MB" and take seconds to load — local code `Street Rod AC/Controls/DealerLotViewport3D.cs` line ~360
- Example of the range on Sketchfab: 7M-triangle dealership vs. 132-triangle low-poly one — [Sketchfab f03456b](https://sketchfab.com/3d-models/garage-dealership-showroom-f03456bd287d4d13966157dd02d95c75); [Sketchfab bb07901](https://sketchfab.com/3d-models/car-dealership-bb07901c912a43e1a5c2e9f6c74ee4c6)

### Inferences
- A self-built showroom of tens of MB (DXT-compressed 2K textures, one 8K dome) would load much faster than the few-hundred-MB community ones we currently use, which matters because the lot rebuilds the scene when the showroom changes.
- Prefer merged meshes / texture atlases: each material is a draw call per pass, and the Dark renderer draws the showroom in shadow, main and transparent passes plus possibly reflection probes.

### Gaps
- No authoritative polycount/texture budget specific to AC showrooms was found; the numbers above are recommendations, not sourced norms.
