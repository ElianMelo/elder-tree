# Art Direction — PSX Dark Fantasy

Distilled from the 91 images in `Assets/References` plus web research on PS1-era rendering.
**Every new model is checked against this page before it is built and after.**

## 1. What the references say

| Group | Images (contact-sheet index) | What we take |
|---|---|---|
| Night worlds | 0–3, 14, 27–30, 34–35, 40, 44, 63, 71–72, 75–77 | Saturated *deep* indigo/violet skies, a moon, black silhouettes of towers and dead trees. Value stays low; colour lives in the sky and in small glows. |
| Toxic / arcane glow | 6, 11, 18, 22, 24, 31–32, 68, 82 | Small, intense light sources (sickly green, ember red/amber) that pop against near-black. Glow is an *accent*, never the base colour. |
| Creatures & figures | 9, 13, 37, 39, 50–52, 60–61, 69, 74 | Goblins, liches, knights: chunky silhouettes, every surface carries a busy hand-painted / photo-sourced texture (scales, chainmail, bark, grime). |
| **PSX characters (fidelity target)** | **88 (knight), 89 (skeleton)**, 90 | ~300–1,500 triangles, faceted silhouette, *all* detail is in the texture: baked shading, AO, grime, dents. Small textures stretched over big faces, visible pixels, no specular sheen. |
| Forest / bark | 46, 47, 52, 56, 66, 81, 87 | Mossy, wet bark; crushed blacks between trunks; green-teal fog. |
| Elder tree (user brief, 4 images) | — | Treant: trunk torso, face carved in bark (heavy brow, deep sockets, long nose, root/moss beard), crown of antler-like branches, long gnarled arms with twig fingers, root feet. One has burning amber eyes. |

## 2. Rules

**Geometry**
- Characters 1,000–2,500 tris; props 50–600. Tubes are 3–8 sided. No subdivision surfaces.
- Silhouette over surface: spend polygons on the outline (branches, roots, brow, nose), never on smoothness.
- Overlapping/intersecting parts are fine (the PS1 way) — no need for welded topology.
- Alpha-cutout cards (moss beard, leaf clumps, hanging roots) instead of modelled strands.

**Texture**
- One atlas per character, **256 × 256** (hero) / 128 (props). Point (nearest) filtering, no mipmaps, no compression smear.
- *High detail at low resolution*: every texel carries information — grain, cracks, lichen spots, grime. Never flat colour fills.
- Shading is painted in: albedo = colour × AO × soft top-light. No normal maps, no specular.
- Palette quantised to 15-bit (5 bits/channel) with 4 × 4 ordered (Bayer) dither — the PS1 look.
- Texel density is uniform across the character (UV parts are scaled by real-world size).

**Colour**
- Base values stay dark and desaturated: bark `#2B2320`–`#5A4A3C`, moss `#2E3A22`–`#4C5A30`, lichen `#6E7466` (sparingly).
- No bright, candy, or "storybook fantasy" saturation on surfaces. Saturation is reserved for glows and the sky.
- Accent glow: dim ember amber `#C8781E` (eyes, cracks), in line with `Palette.Ember / Lantern`.

**Material (Unity URP)**
- URP/Lit, smoothness ≤ 0.08, metallic 0, no normal map. Emission only for the accent glow.
- Cutout cards: alpha clip 0.5, double sided.

## 3. Rendering notes (from research)
- PS1 textures were 64–256 px, stretched over big surfaces; nearest filtering, no mips ([3daistudio PS1 guide](https://www.3daistudio.com/blog/ps1-graphics-how-to-make-ps1-style-3d-models)).
- Characters were a few hundred to ~1,000 triangles ([same](https://www.3daistudio.com/blog/ps1-graphics-how-to-make-ps1-style-3d-models)).
- The full look also needs low internal resolution, vertex snapping and affine warping in a shader ([godot-psx-style-demo](https://github.com/MenacingMecha/godot-psx-style-demo)); vertex colours fake lighting; textures posterised before import.
- PSX soulslike precedent: *Bloodborne PSX* and *Lunacid* ([PC Gamer](https://pcgamer.com/bloodborne-psx-is-a-lo-fi-demake-of-the-fromsoftware-slasher), [RPGFan on Lunacid](https://www.rpgfan.com/review/lunacid/)) — chunky pixels, rigid animation, dark fantasy spaces.

## 4. How the game implements it

| Layer | Where | What |
|---|---|---|
| Shader | `Assets/TungSahur/Art/Shaders/PSXLit.shader` | URP Simple Lit (Lambert, no specular) + clip-space **vertex snapping** + **15-bit ordered dither** on output. Every lit material uses it through `TungSahurBuilder.Lit()`. |
| Globals | `Scripts/Runtime/FX/PSXGlobals.cs` (on the `PostProcess` object) | Snap grid (default 320×180, aspect-matched) and dither strength (0.45). Set the grid to 0 to turn the wobble off. |
| Resolution | URP `PC_RPAsset` via `BuildScene()` | Render scale **0.4** with **point** upscaling, MSAA off, hard 1K shadows. UI Toolkit stays native-res and crisp. `TungSahurBuilder.PsxRenderScale` tunes it. |
| Textures | `TextureForge.SavePsx` | 64–256 px, 15-bit Bayer-dithered, point filtered, uncompressed. Tiling world textures keep nearest-mips so the ground does not boil. Ground, planks, stone, burlap, bark, pine needles, dark wood, leather, rusted iron, club. |
| Props | `Tools/Blender/build_props.py` (`PSX = True`) | No bevels, ≤8-sided cylinders, 10×6 spheres, faceted shading. A gnarled dead tree and a dark pine replace the round storybook trees. Props are 48–790 tris; the dummy, which acts as an enemy, is 1,120. |
| Weapons | `build_psx_weapons.py` (club), `build_bow.py` (`PSX = True`) | Iron-banded spiked cudgel (325 tris), bow 572, arrow 284, quiver 1,360 (was 8.4k / 1.4k / 6.3k). Dark wood, black horn, dried-blood and bone fletching, leather quiver. |
| Light & mood | `Palette.cs`, `BuildScene()` | Deep indigo/violet sky, **purple mist lighter than the surfaces** (silhouettes read against it), cold moon key, dimmer amber lanterns, sickly green will-o'-wisps, violet-crushed grade with heavier film grain. |

Tuning notes: ambient and fog drive readability at night. If the scene turns into black mush, raise `Palette.Fog` / ambient before touching exposure, because exposure amplifies the dither steps. The lamp poles catch their own lantern and read as torches; that is intentional.
