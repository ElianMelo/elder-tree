# Handoff — Tung Tung Tung Sahur soulslike slice

## 0000. Animation: Human Mega Animations Pack is the source of truth

All player motion now comes from **Kevin Iglesias / Human Mega Animations Pack** (`Assets/Kevin Iglesias/Human Animations/Animations/Male`). Don't mix in other packs: the ExplosiveLLC jump/fall/land were the last outside clips and are gone.

**Rig retargeted onto the pack's HumanM skeleton** (`Tools/Blender/build_elder_tree.py`, 2,264 tris, 51 bones):
- Joint heights and segment ratios follow `HumanM_Model` × 0.95: hips 0.93, spine 1.01, chest 1.20, neck 1.455, head 1.525, shoulders raised from 1.25 to **1.38** (they used to sit inside the trunk, so pack poses with raised arms buried them). Shoulders stay wider than HumanM (0.30 vs 0.19) so the arms clear the trunk.
- Same chain as the pack: **no UpperChest** (shoulders parent to Chest).
- **5-finger hands, 30 finger bones**, laid out like HumanM's hand. The twig fingers are modelled straight; the pack's clips curl them (fist on the club, closed bow hand). The old pre-curled right fist is gone.
- Unity import (`ConfigureCharacterImport`): fingers are mapped (Blender bone = HumanTrait name without spaces), **default muscle limits** like the pack's own avatar (the old damped spine/neck ranges muted the torso twist), and the skeleton description is rebuilt from the file each time (a stale one invalidates the avatar).
- Check tool: `PreviewRenderer.RenderCompareSheet(file, clip)` draws the pack's HumanM (top) and the Elder Tree (bottom) on the same frames; `RenderBoneCloseup` zooms on one bone. Shot: `Docs/Screenshots/17_pack_retarget_compare.png`.

**Clip map** (`TungSahurBuilder.Animation.cs`):

| State | Pack clip |
|---|---|
| Locomotion | CombatIdle1H01 + Walk01 / Run01 (8 directions) + Sprint01 |
| JumpStart / Fall / Land | Jump01 - Begin / Fall01 / Jump01 - Land |
| Light 1-4, DodgeAttack | Attack1H02/04/05/03/01_R |
| Heavy 1-2, RunAttack, JumpAttack, Skill | Attack2H02/01/03/02/04 |
| Dodge, Backstep | Dodge01 (the pack has no backstep) |
| HitLight / HitHeavy / Stagger / Death | CombatDamage01 / 02 / Stun01 / CombatDeath01 |
| **KnockdownFall → Ground → StandUp** (new: Slam hits) | Knockdown01 - Fall / Ground / StandUp |
| Guard layer | Parry1H01_R - Loop / Hit |
| **BowDraw** (new) | BowShot01 - Load, scrubbed by the gameplay draw (`BowDrawTime`) |
| **BowHold** (new) | BowShot01_Down / level / _Up - Hold, blended by aim pitch (-50 / -5 / +40 deg, measured) |
| **BowRelease** (new) | BowShot01 - Release |
| **BowLegs layer** (new, legs-only mask) | Walk01 (8 directions) around a BowShot01 - Hold centre, steered in the hips frame |

**Bow now plays the pack's archer clips.** These clips keep the root facing the target and turn the hips about 85° inside the animation, so `stanceYaw` dropped from 85° to 6° (the clips aim 6° left of the root). The BowRig IK anchor (0.21, 1.38, 0) and reach/draw (0.56 / 0.68) sit where BowShot01 - Hold puts the hands, so IK only trims the pack pose onto the aim line. Shot: `Docs/Screenshots/18_pack_archer_stance.png`.

Verified: animator and prefabs rebuilt, attack hit windows still contain each swing's peak (`AnalyzeSwing`), and play mode was checked with the autopilot (idle, run, light, heavy, bow draw / full / strafe / release / renock, jump, slam knockdown). Console: 0 errors, 0 warnings.

**Not used yet, good next picks from the pack:** Turn01 (turn in place), UnsheatheBack/SheatheBack (bow ↔ club swap; the 0.2 s raise is too short for them today), BowDamage/BowDeath (hits currently drop the bow first), CombatDeath02-06 variants, Idle breaks out of combat.

## 000. Boss: Bombardiro Crocodilo

**Start it:** pause menu → **Summon Bombardiro**. The training dummies leave the field, and the boss flies in low over the arena, roars, and climbs into its orbit. **Reset Training** ends the fight.

**Debug: F1** opens a boss roster listing every boss in `BossEncounter.bosses` (the primary `boss` is added automatically). ↑/↓ + Enter or 1-9 spawns / despawns one, 0 despawns all; the game pauses while it's open. Several bosses can be up at once: the bar follows the next living one, and victory waits until all are down. To add a boss, put its `BombardiroBoss` in the list.

| Move | What it does | Counterplay |
|---|---|---|
| Carpet bombing | Plans a run across your position (best of 16 headings, then a Hermite curve onto the line) and lays 7 bombs (10 in phase 2). A red ring appears on each impact point first and fills as its bomb falls. | Leave the corridor sideways. |
| Precision strike | 3 to 4 single bombs aimed where you are heading. | Keep moving or roll. |
| Living bombs | Low pass that drops 3 to 4 walking croc-bombs. They chase you, plant their feet within 1.7 m, blink red twice (beep, beep), then blow. | Kill them first (45 HP): they only pop, and the pop damages the boss if it's near. Or roll out after the second blink. |
| Ground charge | Lands across the arena from you, roars and turns to face you, then runs you over in a straight line (Slam, launches you). Twice in phase 2. | Sprint or roll sideways. The outer wings pass overhead. |
| Grounded window | Sits on the field for 6.5 s and snaps its jaws at anything in front (bite with a 0.55 s telegraph). Flanks and tail are safe. | Melee window. |
| Stance break | Arrows and clubs wear its poise down (150). Broken in the air, it crash-lands; broken on the ground, it slumps nose-first. Either way you get a 3.4 s critical window (×2.6). | Shoot it in the air, then club it. |
| Phase 2 (≤50%) | Smoking outer engines, 18% faster, more bombs, double charge. | |

Death: **YOU DIED**, fade, respawn at the spawn point; the boss is dismissed and the dummies return. Victory: crash, chain explosions, **GREAT ENEMY FELLED**; the wreck stays until the next summon or reset.

**Player damage (new, the player could not be hurt before):** `PlayerController.Damage.cs`. Roll i-frames dodge everything; a guard facing the blow absorbs 72% for stamina (guard break when stamina runs out); flinch by impact weight (Slam knocks you off your roots); 0.4 s grace after a hit so one blow can't multi-hit. `TakeHit(DamageInfo)` is the API for any future enemy.

**Bow vs flying targets:** while locked on and aiming, the crosshair now stays stuck to the target, and look input becomes a lead offset (±14°, `CameraDirector.maxAimLead`). Before this change it snapped only for 0.3 s when the bow was raised.

| Piece | Where |
|---|---|
| Model (2,355 tris; croc-head bomber, separate jaw and 4 three-blade props), aerial bomb (192), living bomb (540) | `Tools/Blender/build_bombardiro.py`: run headless with `-- <abs out dir>`, then copy the 3 FBX to `Art/Models/Boss` |
| AI: flight (banked steering, Hermite paths), moves, stance, phases | `Enemies/Bombardiro/BombardiroBoss.cs` (all tuning is on the prefab) |
| Bombs, markers, walkers, blasts | `AerialBomb.cs`, `LivingBomb.cs`, `Blast.cs` |
| Fight flow (summon, dummies, death and victory) | `BossEncounter.cs` (on GameSystems) |
| HUD: bottom boss bar with chip and damage number, YOU DIED, screen fade, menu entry | `UI/HUD.Boss.cs`, `TungHUD.uss` |
| Textures (riveted rusty aluminium, croc scutes, teeth, flesh, marker), materials, FX (explosion with light flash, bomb pop, smoke), prefabs | `Editor/TungSahurBuilder.Boss.cs` → `BuildBoss()` (part of `BuildAll()`) |
| SFX: explosion, bomb whistle, beep, croc roar, jaw snap, metal hit, player hurt, skid, YOU DIED, GREAT ENEMY FELLED, a seamless four-engine drone loop | `SfxForge` (Bombardiro section) |
| Autopilot commands | `key F1` (tap any keyboard key), `boss`, `bossmove Carpet/Strike/Living/Charge`, `bosshp <dmg> [poise]`, `lockboss`, `god 0/1` |

Verified in play mode with the autopilot: every move, hits landing on the player, melee and arrows damaging the boss, stance break → crash/stagger → criticals, phase 2, death with the banner, and player death → respawn. Console: 0 errors, 0 warnings. Shots: `Docs/Screenshots/16_bombardiro_fight.png`.

**Known limits:** the arena props (lamps, crates, hut) don't block the boss; it taxis through them, and a living bomb can land on a lamp top. The plane has no skeleton (procedural jaw and props only). There is no boss music yet; the engine drone carries the mood. `TextureForge.BuildIron` (and a few older textures) use `Mathf.SmoothStep` as if it were a threshold, but it interpolates between its first two arguments. The boss textures use a proper smoothstep; the older ones were left alone.

## 00. Character v3: Elder Tree (PSX dark fantasy)

- **Art direction** now lives in `Docs/ART_DIRECTION.md` (from the 91 images in `Assets/References`, the 4 treant images and PS1 research). Check every new model against it.
- `Tools/Blender/build_elder_tree.py` builds the player as a low-poly treant: **2,222 tris**. 12-sided fluted trunk with a carved face (angry V brow, deep ember-lit sockets, long drooping nose), moss-strand beard cards, 8-branch antler crown with sparse dead-leaf cards, shoulder burls and spurs, root skirt over the thighs, gnarled 6-sided arms with twig fingers (now finger-rigged, see §0000), and root-toe feet.
- Skeleton: superseded by the pack retarget (§0000). It used to be the old log's 22-bone humanoid.
- **Textures**: `T_ElderBark.png` (256², point filtered, no mips, uncompressed) is painted per texel: bark fibres, fissures, plates, cross cracks, knots, lichen, moss on up-facing surfaces, ground grime, ember glow around the eyes. All of it is multiplied by baked AO and a soft top light, then quantised to 15-bit with a 4×4 Bayer dither. `T_ElderMoss.png` (256², alpha cutout) holds the moss strands, leaves and hanging roots.
- **Materials** (`BuildElderMaterials()`): `M_ElderBark` (smoothness 0.04, no specular or reflections), `M_ElderMoss` (alpha clip, double sided), `M_ElderEye` (dim amber emission).
- `TungSahurBuilder.ModelPath` → `ElderTree.fbx`. The old log is kept at `TungModelPath`.
- Rebuild: `blender -b --factory-startup --python Tools/Blender/build_elder_tree.py -- Tools/Blender/export --bake Assets/TungSahur/Art/Textures`, copy `ElderTree.fbx` into `Assets/TungSahur/Art/Models`, then `TungSahurBuilder.BuildAll()`. The bake takes about 5 s. Live look-dev: run the builder with `TS_EXPORT=False`, then `preview_elder_tree.py` (`ET_Cam*` cameras).
- Shots: `Docs/Screenshots/11_elder_tree_ingame.png`, `12_elder_tree_blender.png`, `13_elder_tree_atlases.png`. Play mode console: 0 errors, 0 warnings.
- **World pass (PSX dark fantasy)**: every lit material now uses `TungSahur/PSXLit` (vertex snap + 15-bit dither), the game renders at 0.4 scale with point upscaling, the world textures are 64–256 px dithered and point filtered, and the props, trees, club, bow, arrow and quiver were rebuilt low-poly. The sky, fog, lights and grade are now indigo/violet. Details in `Docs/ART_DIRECTION.md` §4, shots in `Docs/Screenshots/14_psx_world_sheet.png` and `15_psx_world_full.png`. Play mode console: 0 errors, 0 warnings.
- Rebuild the world: `blender -b --factory-startup --python Tools/Blender/build_props.py -- Assets/TungSahur/Art/Models/Props`, `... build_psx_weapons.py -- Tools/Blender/export` (copy `ElderClub.fbx` to Models), `... build_bow.py -- Tools/Blender/export --icon Assets/TungSahur/UI/Icon_Bow.png` (copy the three FBX), then `TungSahurBuilder.BuildAll()`.
- Unused after the pass (safe to delete when you review): the `T_*_Normal.png` world normal maps, `T_TungClub_*`, and the old `TungSahur_Club.fbx` (kept at `TungClubPath`).

## 0. Character v2 (reference-matched model)

- `Tools/Blender/build_tung_sahur.py` now sculpts the face as a height field on a dense lathe (carved brows, socketed wide-open eyes with lids, bulbous nose, apple cheeks, smirking grin, nasolabial folds), chunkier log (r 0.27), thicker legs, big feet, gripping right fist, plain-wood bat.
- Textures are **baked** (`--bake <Assets/TungSahur/Art/Textures>`): `T_TungBody_Albedo/Normal` (4K, albedo x local AO, seam-free cylindrical face UVs), `T_TungClub_*`, and a generated `T_TungEye_Sculpt` (dark-brown iris, black pupil). `BuildArt()` uses them when present, else falls back to the procedural tileables.
- Look-dev in live Blender via MCP: run the builder with `TS_EXPORT=False`, then `Tools/Blender/preview_tung_sahur.py` (pose, warm lamp lights, `CamBody/CamFace/CamFeet/CamHand`).
- Rebuild: `blender -b --factory-startup --python Tools/Blender/build_tung_sahur.py -- Tools/Blender/export --bake Assets/TungSahur/Art/Textures`, copy the two FBX into `Assets/TungSahur/Art/Models`, then `TungSahurBuilder.BuildAll()`.
- Comparison: `Docs/Screenshots/10_reference_vs_model.png`.

## 1. What was built

| Request | Status | Notes |
|---|---|---|
| 3D model of Tung Tung Tung Sahur from the image, in Blender | ✅ | Built by a Python script run in **headless Blender 5.3** (see §3). Log body, carved brows/nose/smile, bulging eyes with heavy lids, thin limbs, fists, bare feet with toes, and a separate baseball-bat club. Source `.blend` + previews in `Tools/Blender/export/`. |
| Unity Humanoid rig | ✅ | 22-bone armature named to Unity's HumanTrait names, explicit humanoid mapping, computed skin weights. Avatar imports **valid / humanoid**. Spine/neck muscle ranges are narrowed so the log stays rigid instead of bending like rubber. |
| Retarget the imported animation packs | ✅ | Kevin Iglesias (locomotion 8-way, 1H/2H attacks, dodge, hits, guard), ExplosiveLLC 2-Handed Warrior (jump/fall/land). Vendor import settings untouched (a looping copy of *Fall* lives in our folder). |
| Attacks that sell the club's movement, strength, weight | ✅ | Clips chosen by contact-sheet review; hit windows **measured from the club-tip speed curve** of each clip. Per-phase playback speed (slow wind-up → fast snap → heavy follow-through), lunges, hit-stop, impulse shake, FOV punch, rumble, wood-knock audio, splinters. |
| Cinemachine third-person camera | ✅ | Cinemachine 3: OrbitalFollow + RotationComposer + Deoccluder + ImpulseListener, driven by `CameraDirector`. |
| Light + heavy attacks | ✅ | 4-hit light chain, 2-hit heavy chain with **hold-to-charge**, running attack, roll attack, jump (plunging) attack, FP skill "Tung Tung Slam" with ground shockwave. |
| Strafe, movement, run, jump | ✅ | Walk/run/sprint, 8-way lock-on strafing, jump with momentum, coyote time, hard-landing recovery, roll (a literal log tumble) / backstep with i-frames, guard (upper-body layer). |
| Full controller support, souls schema | ✅ | Elden Ring button layout on gamepad and KBM, buffered inputs, dodge-on-release / hold-to-sprint, device-adaptive hints, rumble, menus fully navigable with a pad. |
| Dummy: damage, lock-on, strafe | ✅ | 3 straw dummies: spring wobble, hit flash, squash, poise/stance break → stagger window → **critical** (×2.6), regeneration, "FELLED" topple and rise. |
| Polish / UI | ✅ | Night art direction (palette in `Palette.cs`), procedural sky, lanterns with flicker, fireflies, post stack, UI Toolkit HUD (bars with chip damage, equipment cross, reticle, enemy bar with accumulating damage, title card, felled banner, pause/settings). |

Verified in Play mode with an automated virtual-gamepad playtest (`Dev/AutoPilot.cs`): combo chaining, charged heavy (114 dmg), skill slam, roll, sprint, jump attack, lock-on, guard, stance break → critical, felled banner, pause/settings navigation. **Console: 0 errors, 0 warnings.**

## 2. Elden Ring research → how it maps to the code

| ER mechanic | Reference value | Here |
|---|---|---|
| Roll i-frames (medium load) | 13 i-frames + 8 recovery @30fps ≈ 0.43 s + 0.27 s ([wiki.gg](https://eldenring.wiki.gg/wiki/Invincibility_Frames), [Equip Load](https://eldenring.wiki.gg/wiki/Equipment_Load)) | `rollIFrames = (0.03, 0.46)`, `rollDuration = 0.66` |
| Roll vs backstep | Direction held → roll, none → backstep | `TryDodgeDirect()` |
| Stamina regen | ~45/s base, delayed after spending ([Fextralife Stamina](https://eldenring.wiki.fextralife.com/Stamina)) | `staminaRegen = 45`, `staminaRegenDelay = 0.55`, slower while guarding |
| Act with any stamina left | Actions start if stamina > 0, may go negative | `PlayerStats.TrySpendStamina` |
| Dodge on release, hold to dash | Same button | `PlayerInputReader` tap-vs-hold |
| Input buffering / animation commitment | Inputs queue during an attack, executed at the combo window; recovery is committed except via dodge after a cancel point ([forum discussion](https://forum.norestforthewicked.com/t/input-buffering-combat-queueing-feels-too-unresponsive-in-dynamic-situations/16313)) | `Queued` in `PlayerController.Combat` with `comboOpen` / `cancelOpen` per attack |
| Charged heavies, stance (poise) damage | Charged heavies & jump attacks deal the most stance damage; stance break → critical ([Stance](https://eldenring.wiki.gg/wiki/Stance), [Windows Central](https://www.windowscentral.com/elden-ring-melee-combat-guide)) | `poiseDamage`, `chargeDamageMultiplier`, dummy `staggerWindow` + critical |
| Hit-stop | Must freeze attacker *and* target or it reads as a hiccup ([UE5 game-feel notes](https://uhiyama-lab.com/en/notes/ue/game-feel-hit-feedback/), [PC Gamer on MH Wilds](https://www.pcgamer.com/games/rpg/capcom-if-youre-listening-monster-hunter-wilds-needs-its-hitstop-back)) | Global `TimeDirector.HitStop` (whole world freezes), longer for heavies/charged/criticals |
| Early tracking | You can steer the first frames of a swing; lock-on snaps facing | `trackUntil`, `trackTurnSpeed` |
| Lock-on camera | Behind the player, aim drifts toward target, switch by stick flick | `CameraDirector` lock branch + shoulder offset, `LockOnSystem.Switch` |
| Enemy HP bar | Floating bar + accumulating damage number during a combo | `HUD.Panels.cs` enemy bars |

Exact frame data varies by source and patch (the wikis disagree on light vs medium i-frames), so values are starting points, all exposed on the prefab / `TungMoveset.asset`.

## 3. Deviations & things you should know

- **No Blender MCP server was connected to this session**, and the open Blender instance had no MCP socket. I ran Blender **headless from its Steam install** (`E:\Steam\steamapps\common\Blender\blender.exe`) with Python scripts instead. The scripts are MCP-compatible if you want to drive them that way later.
- **Installed `com.unity.pipeline`** (Unity's CLI bridge) so I could drive your open editor; it's in `Packages/manifest.json`. Remove it if you don't want it.
- **Fixed a compile error in a vendor file**: `ExplosiveLLC/.../SuperCharacterController.cs` had `[SerializeField]` on a struct (CS0592), which kept the project in a broken compile state. Changed to `[System.Serializable]`.
- URP `PC_RPAsset`: MSAA 4×, shadow distance 55, 4K main shadow map, 2K additional shadows. Tag Manager: layers **Player (8), Hurtbox (9), Enemy (10)**. Build settings: `Arena.unity` is the only scene.
- Unity auto-created `Assets/UI Toolkit/UnityThemes` (default runtime theme). `QualitySettings.asset` diff is Unity's own serialization upgrade.
- **Nothing is committed** — all changes are in the working tree for you to review.
- `Dev/AutoPilot.cs` (virtual gamepad test driver) and `WeaponHitbox.DebugLog` are dev tools; they're inert unless invoked.

## 4. Known limitations

1. **No true roll animation in the packs.** The roll is the Kevin Iglesias *Dodge* pose plus a procedural 360° forward tumble of the whole log (on-brand, but a dedicated roll clip would be cleaner).
2. **No fingers/grip pose** — Tung's hands are sculpted fists; the club is parented to the right hand. Two-handed heavies show the left hand near, not on, the handle.
3. Arms can clip the log during wide swings (a log is wider than a human torso).
4. Hut sign uses a legacy `TextMesh`; fine at distance, soft up close.
5. The dummies don't attack, so guard/hit-reaction states exist but only matter once an enemy fights back.
6. Audio is fully synthesized — good for feel, but placeholder next to real foley.

## 5. Questions for you

1. **Combat identity:** should the club feel *heavier* (Great Hammer: slower, more stance damage, fewer hits) or *snappier* (current: Hammer class)? This decides the light-combo speed curve.
2. **Enemies next?** Do you want a sparring enemy that attacks back (parry/guard/dodge become meaningful), or a boss (a giant *kentongan* drum? a rival "Ballerina Cappuccina"-style brainrot character)?
3. **Art direction:** keep the moonlit stylized-PBR look, or go toward the reference's warmer, photoreal-ish lamp lighting?
4. **Roll:** keep the comedic full log-tumble, or a grounded side-roll? Can I pull a roll animation from Mixamo or another pack?
5. **Character proportions:** happy with the model? Changes like a thicker log, longer legs or bigger eyes are one-number tweaks in `build_tung_sahur.py`.
6. **Keyboard layout:** ER-faithful (Shift+LMB heavy, RMB guard) or the common action layout (LMB light / RMB heavy)?
7. **Audio:** keep synthesized SFX, or should I wire a slot for real recorded "tung tung tung" kentongan hits and a voice bark?
8. **Target platform & performance budget** (PC only? Steam Deck? consoles)? That decides shadow/post quality tiers.
9. Do you want the **Blender MCP** set up so future model edits happen live in your Blender session?

## 6. Suggestions (ranked)

1. **An enemy that fights back**: reuse the humanoid pipeline with the DoubleL *Enemy_Attack* / *Hit* clips; add a player hurtbox, guard/parry and posture, and the existing `Hit` state comes alive.
2. **Riposte / critical animation**: a dedicated finisher ("TUNG!" overhead with a camera cut) instead of a damage multiplier.
3. **Weapon arts and two-handing (Y+RB)** switching to the 2H idle and a 2H moveset, so the club gets an ER-style stance toggle.
4. **Sahur-themed objective**: wake the village before dawn (hit kentongan drums in order, a timer to sunrise, lighting goes from night to dawn), a natural "first level".
5. **Flask of Crimson Tears** equivalent ("Kolak bowl"?) on X with a drink animation and HP UI.
6. **Real roll and hit-reaction clips** plus finger bones for proper grips.
7. **Audio pass**: kentongan samples, footsteps per surface, a reactive drum music layer that intensifies when locked on.
8. **Performance pass**: baked lighting for the static yard, GPU-instanced trees, LODs.
9. **Save/settings persistence** is already in PlayerPrefs; next: key rebinding UI via the Input System rebinding API.
10. Commit with **Git LFS** for `.fbx/.png/.wav/.blend` before the repo grows.

## 7. Bow & arrow

**Controls:** hold **LT / RMB** to raise and draw, **RT / LMB** (or RB) to loose. Arrows are infinite. Firing before the minimum draw (30%) is buffered and looses as soon as the string is far enough back. A full draw chimes, turns the reticle gold, and hits harder (marked as *charged*). Remapped to make room: **Guard is Left Alt** on keyboard (still LB), **Skill is Y** on pad (still Shift+RMB).

| Piece | Where |
|---|---|
| Models (recurve bow with `Draw` shape key and string/arrow-rest empties, bamboo arrow, quiver) and the HUD icon | `Tools/Blender/build_bow.py` (live via MCP or headless; `--icon` renders `UI/Icon_Bow.png`) |
| Gameplay: Aim state, raise → draw → release → re-nock, buffered fire, stamina per shot, ballistic solve onto the crosshair point | `PlayerController.Bow.cs` (all tuning on the Player prefab) |
| Presentation: side-on stance, humanoid IK puts both hands on the aim line, bow swings from the back slot to the fist (club goes to the back), blend-shape limbs measured from the string hand, 3-point string with release buzz, reach to the quiver | `BowRig.cs` (on the model, IK pass on base layer) |
| Projectile: raycast-stepped flight, damage via hurtboxes but sticks only where it meets the visible body (ellipsoid test), rides along with the dummy, wobble, oldest-first recycling (40) | `Combat/ArrowProjectile.cs`, `Prefabs/Arrow.prefab` |
| Camera: shoulder offset, zoom (radius + FOV, tighter at full draw), finer look, pad aim friction on targets, closed-loop snap onto the lock target when raising | `CameraDirector` (Bow aim section) |
| HUD: crosshair whose ticks close with the draw, gold pulse at full draw, warm dot over targets, hit marker; bow slot + ∞ in the equipment cross; lock reticle fades while aiming | `UI/HUD.Bow.cs`, `TungHUD.uss` |
| SFX: draw creak, release twang, nock click, arrow thunk, full-draw chime | `SfxForge` bow section |
| Build | `TungSahurBuilder.Bow.cs` (`BuildBowAssets`, `AttachBow`), included in `BuildAll()` |

Verified in play mode with the autopilot (raise, draw, full-draw hit, rapid fire, aim from a run, dodge out of aim, lower). Console: 0 errors.

Also removed the ExplosiveLLC `Editor/SetupInputLayers.cs` popup ("Warrior Animation Mecaim…"), which reopened after every asset import.

**Known limits / next ideas:** no dedicated upper-body bow clip (the pose is IK-driven; the DoubleL `Bow_Attack_*` clips don't suit Tung's log body); no arrow pickup or ammo types yet (infinite by design); the arena is small, so a far archery target would show the arc better.
