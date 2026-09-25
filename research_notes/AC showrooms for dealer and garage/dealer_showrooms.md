# AC showrooms (and showroom-like tracks) for car dealership / used-car lot / street scenes

Scope: scenes usable as a dealer backdrop for the AcTools "Dark" renderer in Street Rod AC. Researched 2026-09-23 via web search and page fetches; nothing downloaded. Several hosts (Patreon, nexusmods, assettoworld, assettocorsamods.io) returned HTTP 403 to the fetch tool, so details from those come from search snippets only and are marked as such.

Important distinction found during research: true `content/showroom` mods (a single KN5 + ui folder that CM Custom Showroom loads) are almost all photo studios or 360-degree panoramas. Nearly everything that actually looks like a dealer lot, parking lot, gas station or street is published as a **track** (content/tracks, often multi-KN5 via models.ini, CSP-dependent ext_config). Those would need converting or loading as a track scene, not dropping into content/showroom.

## Which dealership / car-lot / parking-lot / street showrooms exist for AC?

### Takeaway
There is no well-known, purpose-made "American used-car lot" or "new-car dealership" showroom for AC. The closest fits are Steel Racing's "Steel Showroom 02: The Dealer" (indoor dealership-style, map form, up to 6 cars, Patreon), a handful of drift/cruise **tracks** that contain parking or dealer lots (Canadian Parking Lot, Parking Garage, Driver 1999 parking lot, a Shell service-station scene), and panorama "city/street" showrooms (SR City Sky, 3 HDRI Showrooms, rataracing's city scenes). Everything else in the showroom category is studio/photo rooms.

### Cited Findings

**Indoor dealership-style**
- **Steel Showroom 02: The Dealer v1.1** — author Steel Racing (steel89ita); Patreon. A remake of one of Steel Racing's first CM showrooms "improved in geometries and textures, and now as a standard map" (i.e. a track, not a content/showroom). Supports up to 6 cars; start in Track mode with up to 5 opponents to show other cars (CSP walking-out mode used to move cars). Skins: 3 wood floors, 2 murals, 2 "relax area" colours; 27-colour light palette, all set in ext_config.ini. Free vs paid tier, file size, date and licence not retrievable (Patreon 403). — [Patreon post (search snippet)](https://www.patreon.com/posts/update-steel-02-79701050); [alt URL](https://www.patreon.com/steelracing/posts/update-steel-02-79701050)
- Steel Racing also has an earlier CM-type showroom "Steel Racing CM Showroom 02: Jagod" and "Steel Showroom 04 Special: The Customizable" (Patreon), plus "GT Showroom v1.1" distributed via the Steel Racing Discord. Looks not verified. — [Jagod](https://www.patreon.com/steelracing/posts/release-steel-cm-61970261); [Customizable](https://www.patreon.com/posts/62561972); [GT Showroom](https://www.patreon.com/posts/showroom-gt-v-1-83392886); GT Showroom mirrored as a track at [assettocorsamods.io](https://assettocorsamods.io/tracks/steel_racing_gt_showroom/) (unofficial mirror, 403 to fetch)
- **Steel Racing Showroom 05: PolyRoom** — steel89ita, overtake.gg, Jul 8 2024; described as "one of the most famous photo showrooms". Photo studio, not a dealer, but the best-known showroom overall. — [overtake.gg](https://www.overtake.gg/downloads/showroom-steel-racing-showroom-05-polyroom.70604/); [tag listing](https://www.overtake.gg/tags/showroom/)
- **Bburago Showroom (map)** — to4m5i, Patreon; includes 3DS Max file, FBX, textures and CSP extension file (source files included is notable for reuse, but licence unknown). Look unverified. — [Patreon (search snippet)](https://www.patreon.com/posts/bburago-showroom-84239263)
- **Asphalt 8 showroom** — nexusmods (mod 26); uses the 3D models and textures from Asphalt 8 at correct car scale; installs to content/showroom. Ripped game assets. — [Nexus (search snippet)](https://www.nexusmods.com/assettocorsa/mods/26)
- **OMEGA Concept Room / OM Art Gallery** — omegastracklab, Gumroad; Concept Room is a fictional high-quality showroom using many CSP lights, "very poor performance but good looking"; Art Gallery is free, needs CSP 0.1.79+. Modern gallery look, not a dealer. — [Concept Room](https://omegastracklab.gumroad.com/l/om_concept_room); [Art Gallery](https://omegastracklab.gumroad.com/l/om_art_gallery)
- Small-room track-type showrooms: **Tky Showroom** (free, 5 pit boxes, ~70 m x 70 m) and **FB Window Showroom** (free) — [Tky Showroom](https://assettocorsamods.io/tracks/tky_showroom/); [FB Window Showroom](https://www.assettoworld.com/track/fb-window-showroom) (both 403 to fetch; details from search snippets)

**Outdoor parking / dealer lots (all are tracks)**
- **Canadian Parking Lot v1.1** — juicter6000, overtake.gg, released Feb 6 2020 (updated Feb 7 2020), 144.5 MB, ~14.8k downloads. "Work in progress parking lot and dealer lot with original buildings"; Canadian strip-mall flavour (Tim Hortons, McDonald's, Walmart). Drift-oriented. Rating 3.57/5 (7 reviews); reviewers said graphics need work and "lots of things to do" remained. Closest thing found to a North American car lot. — [overtake.gg](https://www.overtake.gg/downloads/canadian-parking-lot.30798/)
- **Parking Garage v1.01** — CrazTheKing, overtake.gg, Jun 10 2023, 106.5 MB. 6-level garage with spiral exit, a front parking area and surrounding city; CSP GrassFX/LightingFX/RainFX. 3.25/5 (4 reviews); known issues: ramp collision (road and wall objects overlap), no parking line markings, repetitive wall textures. — [overtake.gg](https://www.overtake.gg/downloads/parking-garage.61281/)
- **Driver (1999) Parking Lot** — made from scratch; recreates the parking lot of the first mission of Driver (1999), a 1970s US setting; practice/drift mode only. Author/date/size not fetched. — [overtake.gg search snippet via tag page](https://www.overtake.gg/tags/parking/)
- Other parking structures (drift-oriented, Japanese flavour): Parking Deck Tokyo Drift Style (MarkHunter, Sep 2016, 8 levels, 20 pits); KC Drift Garage (CrazTheKing, Feb 2022, 62 pits); FZC_Underground (FreeZiic, Feb 2025, GTA San Andreas conversion — ripped). — [overtake.gg parking tag](https://www.overtake.gg/tags/parking/)

**Gas station / street**
- **J and G Designs Shell Service Station Scene** — track mod located in Manchester, UK (British Shell station, not American). Other details unavailable (403). — [assettocorsamods.io](https://assettocorsamods.io/tracks/j_and_g_designs_shell_service_station_scene/)
- **Street Showroom by Yronata** — track-type showroom located in Lviv, Ukraine (European street). — [Assetto World](https://www.assettoworld.com/track/street-showroom-by-yronata); [assettocorsamods.io](https://assettocorsamods.io/tracks/street_showroom/)
- **SR City Sky Showroom v1.1** — Paulaob, overtake.gg, Apr 12 2017, 139.2 MB, ~6.1k downloads, 5.0/5 (5 ratings). Real content/showroom with cityscape/sky backdrop plus 3 CM presets; v1.1 fixed shadows. — [overtake.gg](https://www.overtake.gg/downloads/sr-city-sky-showroom.14897/)
- **3 HDRI Showrooms** — focused_gaming, overtake.gg, Feb 1 2021: two street environments plus an industrial/container-yard scene (HDRI panoramas). — [overtake.gg showroom tag](https://www.overtake.gg/tags/showroom/)
- **rataracing city scenes** — "Showroom Ulmer Münster" (Jul 2021, cathedral square) and "ShowRoom The Cross Benidorm" (Jun 2021, coastal overlook). European landmarks. — [overtake.gg showroom tag](https://www.overtake.gg/tags/showroom/)
- **Southbank Skatepark** — rmi_wood, Apr 2021, photogrammetry-based urban showroom (London). — [overtake.gg showroom tag](https://www.overtake.gg/tags/showroom/)
- **Just Kauser's panoramas** on assettocorsamods.net: a Sleeping Dogs showroom (100 game screenshots stitched into a 360-degree texture) and a Chemnitz Karl-Marx-Monument street scene; shared via Google Drive, unpack to content/showroom. — [assettocorsamods.net thread](https://assettocorsamods.net/threads/custom-showroom-scene.1283/)

**Packs (contents not itemised on their pages)**
- **Showrooms and CM Presets Pack 1.0** — Paulaob, overtake.gg, Apr 6 2017, 249.5 MB, ~40.7k downloads, 4.58/5 (24). 9 showrooms + 14 CM presets. Complaints: "too overbright", performance slowdowns. Individual showroom names not listed on the page. — [overtake.gg](https://www.overtake.gg/downloads/showrooms-and-cm-presets-pack.14786/); mirror [modland](https://www.modland.net/assetto-corsa-mods/other/showrooms-and-cm-presets-pack.html)
- **Assetto Corsa Showroom Pack-1 (10 showroom)** — hdend, overtake.gg, Oct 16 2022, 50.2 MB, 1 review (4/5). Contents/origin not described. — [overtake.gg](https://www.overtake.gg/downloads/assetto-corsa-showroom-pack-1-10-showroom.55240/)
- A separate Patreon "Showroom pack" exists (author/contents unverified). — [Patreon](https://www.patreon.com/posts/showroom-pack-100309926)

**Garage/studio ones (in passing, covered elsewhere)**: Garage Showroom (2017), Showroom Basic Day Refurbished (Rogerson Roller, 2018, wooden garage doors), Retrolux Studio, GT3/GT4/GT5-inspired CM showrooms (DoughertyJames, 2023), ascobash studios (2016). — [overtake.gg showroom tag](https://www.overtake.gg/tags/showroom/); [assetto-corsa-showroom tag](https://www.overtake.gg/tags/assetto-corsa-showroom/); [ascobash](https://ascobash.wordpress.com/category/showrooms/)

### Inferences
- For an indoor new-car dealer, Steel Showroom 02: The Dealer is the only purpose-built candidate found, and its 6-car design matches a lineup need; but it is a CSP track with ext_config-driven lighting, so in the Dark renderer (no CSP) its lights/skins would likely not appear as intended.
- For a gritty used-car lot, Canadian Parking Lot is the only mod explicitly containing a "dealer lot"; it is dated (2020), WIP and heavy (144.5 MB), so it would be a reference or a source of a cropped scene rather than a drop-in.
- Panorama showrooms (SR City Sky, 3 HDRI, Just Kauser's) are cheap to render and are real content/showroom KN5s, but a panorama gives no ground-level space for several cars to sit in believably beyond a flat floor disc.

### Gaps
- No free/paid status, file size, date or licence for Steel Showroom 02 (Patreon blocked fetch).
- Names/looks of the showrooms inside Paulaob's 9-showroom pack and hdend's 10-showroom pack are not stated on their pages; it is possible one of them is a street/lot scene.
- No American 1960s-1990s themed showroom (diner, motel, gas station, used-car lot with bunting) was found on overtake.gg, assettocorsamods.net, nexus or Gumroad searches. acmods.net, assettocorsa.club, assettoland and Reddit were searched indirectly but returned nothing specific.
- YouTube/Reddit showcases of dealership scenes were not found in searches.

## Which are best-regarded?

### Takeaway
By download counts and ratings, Paulaob's Showrooms and CM Presets Pack (40.7k downloads, 4.58/5) and SR City Sky (5.0/5) lead among free CM showrooms; PolyRoom is the most-cited photo showroom. Lot/parking tracks rate poorly (3.25-3.57/5).

### Cited Findings
- Showrooms and CM Presets Pack: 40,747 downloads, 4.58/5 from 24 ratings; but some reviewers call it overbright and slow. — [overtake.gg](https://www.overtake.gg/downloads/showrooms-and-cm-presets-pack.14786/)
- SR City Sky Showroom: 5.0/5 from 5 ratings, 6,141 downloads. — [overtake.gg](https://www.overtake.gg/downloads/sr-city-sky-showroom.14897/)
- PolyRoom described as "one of the most famous photo showrooms". — [overtake.gg tag](https://www.overtake.gg/tags/showroom/)
- Canadian Parking Lot 3.57/5 (7); Parking Garage 3.25/5 (4) with collision complaints. — [Canadian Parking Lot](https://www.overtake.gg/downloads/canadian-parking-lot.30798/); [Parking Garage](https://www.overtake.gg/downloads/parking-garage.61281/)

### Inferences
- There is no community consensus "dealer showroom" favourite; the dealership look is a niche that mostly gets built as drift/cruise tracks.

### Gaps
- No Reddit/forum thread ranking dealer-type scenes was found.

## Any modelled on real dealerships or American used-car lots?

### Takeaway
None found modelled on a real dealership. The nearest are Canadian Parking Lot (North American strip mall + dealer lot, original buildings) and the Driver (1999) parking lot (a 1970s-US game setting recreated from scratch).

### Cited Findings
- Canadian Parking Lot: parking + dealer lot, original buildings, Tim Hortons/McDonald's/Walmart. — [overtake.gg](https://www.overtake.gg/downloads/canadian-parking-lot.30798/)
- Driver (1999) Parking Lot: made from scratch after the first mission of Driver. — [overtake.gg parking tag / search snippet](https://www.overtake.gg/tags/parking/)
- Real-world street scenes that do exist are European (Lviv street, Manchester Shell station, Ulm, Benidorm, Chemnitz, London Southbank). — [Assetto World](https://www.assettoworld.com/track/street-showroom-by-yronata); [assettocorsamods.io Shell](https://assettocorsamods.io/tracks/j_and_g_designs_shell_service_station_scene/); [overtake.gg showroom tag](https://www.overtake.gg/tags/showroom/); [assettocorsamods.net](https://assettocorsamods.net/threads/custom-showroom-scene.1283/)

### Inferences
- An American 1960s-1990s used-car lot would likely have to be built for the project (a small KN5: asphalt lot, chain-link fence, pennant strings, a sales shack, sign pole), or cut from a US-themed city track; assettocorsamods.net has a "How to make a showroom" thread as a starting point. — [How to make a showroom](https://assettocorsamods.net/threads/how-to-make-a-showroom.2981/)

### Gaps
- US city/cruise tracks (e.g. freeroam city maps) that may contain car lots were not surveyed; that could yield a scene to crop, licence permitting.

## Any that allow reuse in other projects?

### Takeaway
No candidate states a licence permitting redistribution or reuse. Several are built from ripped game assets (Asphalt 8, Sleeping Dogs, GTA San Andreas conversions, Gran Turismo-inspired), which rules out shipping them. Only the Bburago Showroom advertises source files (3DS Max/FBX), and its terms are unknown.

### Cited Findings
- Showrooms and CM Presets Pack: no credits or licence information on the page. — [overtake.gg](https://www.overtake.gg/downloads/showrooms-and-cm-presets-pack.14786/)
- Asphalt 8 showroom uses Asphalt 8's own models and textures. — [Nexus (search snippet)](https://www.nexusmods.com/assettocorsa/mods/26)
- Sleeping Dogs showroom is stitched from game screenshots. — [assettocorsamods.net](https://assettocorsamods.net/threads/custom-showroom-scene.1283/)
- FZC_Underground is a GTA San Andreas map conversion. — [overtake.gg parking tag](https://www.overtake.gg/tags/parking/)
- Bburago Showroom ships 3DS Max file, FBX, textures and CSP extension file. — [Patreon (search snippet)](https://www.patreon.com/posts/bburago-showroom-84239263)
- ascobash showroom pages give no licence notes. — [ascobash](https://ascobash.wordpress.com/category/showrooms/)

### Inferences
- Using any of these in a distributed game would require asking the author directly (Paulaob, focused_gaming, juicter6000, CrazTheKing, steel89ita are the plausible ones with original work). Loading a showroom the player already installed (as CM does) avoids redistribution entirely.
- Original work built from scratch in Blender (Parking Garage, Canadian Parking Lot's "original buildings", Driver lot "made from scratch") is the only category where a permission request could realistically succeed.

### Gaps
- overtake.gg resource pages fetched did not show any explicit permission/licence field; Patreon terms not retrievable.
