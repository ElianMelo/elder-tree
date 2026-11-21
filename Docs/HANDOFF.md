# Handoff — Elder Tree soulslike slice

## 00000000000000000. Druid Mask (Druid reward) + a lighter, leafier Druid

**Boss look (your call: light wood, more and lighter leaves, dark leaves as shadow).** This deliberately breaks ART_DIRECTION's "dark bark" rule for the Druid set only.
- **Wood:** `T_DruidBark` is now pale, weathered ash/honey wood with brown furrows instead of black ones, a little moss and lichen. `T_DruidRoot` is a shade warmer.
- **Leaves:** **184 clumps** (was 101), about 1.4× bigger. Each clump's back card, lying against the bark, is a **dark leaf** (the shadow layer). The two fanned cards are **light leaves**, except on undersides and in about 14% of clumps, which stay dark all through. That gives the cover depth. `T_DruidLeaf` is one 2×2 atlas: the bottom row holds light greens, the top row dark greens.
- **Budget:** 3,664 tris (1,104 of them leaf cards). The root feet are left bare on purpose: they're the weak point.
- Shot: `Docs/Screenshots/26_druid_boss_light_wood.png`.

**Reward:** felling the Druid drops the **Druid Mask** (`DruidBoss.rewardMask`, set by `PlaceDruidSet`). The first pickup plays the usual cinematic in forest greens, and the received card lists the controls. Afterwards it lives in **slot 3 / D-pad ↓**.

| Druid form | What it does |
|---|---|
| Body | `DruidForm.fbx` uses the same skeleton as the Elder Tree, so all pack clips work. It's a small cousin of the boss: pale wood, the drooping Patapim nose, a scowling brow, green eyes, an antler crown, twig fingers, root feet, a root belt with glowing seed charms and a seed necklace. **124 leaf clumps** cover about 80% of it like a robe: mantle, chest, back, a skirt to the knees, arms, crown and hood. The leaves never stop moving (`FX/LeafSway.cs`). Each clump sways on its own and stirs harder when the druid walks, casts or throws. Its tint drifts lighter and darker with the sway, so the cover shimmers between the two greens. 2,416 body tris plus 744 in leaf cards. |
| Staff | A gnarled pale-wood staff, held upright in the right fist (grip calibrated on the 1H idle). Three branches twist into a crook that cradles a glowing seed. Spells leave from its `Focus`. |
| **Magic** (replaces melee): **Vine Seeker** | **RB:** Elden Ring rules. The druid slows to a walk (1.1 m/s) while the spell gathers for 0.55 s: green motes and leaves spiral into the seed, the seed light swells and the leaves stir. It then releases (`MagicAttackDirect1H01_R` Load → Cast). **FP is spent on release**, so a roll during the wind-up cancels it at no cost. A bundle of **3 leafy vines** leaves the staff. They bow apart mid-flight, writhing, then curl back in onto the target (lock-on target, else the nearest foe within 32° of the aim), so the spell reads even from behind. Homing ramps from 210 to 420°/s. **On hit:** a leaf explosion, then **vines burst out of the struck spot**. They stick to it (they ride a moving boss), arch out, coil in and **squeeze** once more (a second hit in 1.6 m), then wither. Light: 72 + 22 squeeze, 14 FP. Casts chain if you press again in the follow-through. |
| **Charged Vine Seeker** | **RT, hold:** a longer gather (`MagicAttackDirect2H01`) that keeps charging while held, for up to 1.1 s. 4–5 thicker vines, up to ×1.9 damage, a bigger burst, 20–26 FP. |
| **Ranged: Verdant Orb** | **LT** raises both hands (`MagicAttackDirect2H02`) and a ball of green energy gathers between them, growing as it readies. **RT** throws it. It's **slow** (17 m/s, versus 34–88 for arrows and bolts) and **slower to cycle** (1.25 s), flies straight with no homing, and is **big and glowing**: a faceted core, two spinning leafy vine rings, a green light that lights the ground it passes, and a trail of motes. **On impact:** a big green flash and burst, 96 direct + 44 splash in 2.6 m. 18 FP. |
| **Special: Wrath of the Grove** | **RB + RT:** the druid roars (`Angry01`) and **swells to 2.2× its size** over 0.65 s, with a little overshoot, leaves thrashing and the camera pulling back. It then **stomps twice**, right foot then left. Each stomp is the kick's knee-up chamber (`AttackKick02_R/_L`, held at 30%) cut into the heavy landing. Every stomp sends a **ring of vines rising out of the ground from the foot**. The ring spreads to 7.5 m at 6 m/s. Wherever its front passes, vines shoot up, writhe and sink back, so you see a rolling band of vines, and the second stomp's ring follows the first like a ripple. Everything the front reaches on the ground is struck once per ring: 72, then ×1.35 on the last stomp (169 total on a dummy). Then the druid shrinks back. 1 special charge, hyper armour throughout. A hit that does land, or a form swap, ends it and the druid eases back to normal size. |

**Tuning** (created once, your edits survive rebuilds): `Data/Equipment/Spell_VineSeeker` (cast time, walk speed, charge, homing, fan, damage, burst), `Data/Equipment/Orb_Verdant`, `Data/Equipment/Special_WrathOfTheGrove` (size, stomps, raise time, ring radius / speed, vine height / life, damage, camera), `Data/Forms/Form_Druid`, `Data/Masks/Mask_Druid`. Fan shape and strand look are on the `VineSeeker` prefab (`fanShare`, `points`, `width`).

**Code map:** `Player/PlayerController.Druid.cs` (new `PlayerState.Cast`, cast / charge / release, target pick, orb aim and throw). Casters get no melee, run, jump or roll attacks: those inputs cast instead or do nothing. `Combat/VineSeeker.cs`, `Combat/VineBurst.cs`, `Combat/DruidOrb.cs`, `Combat/VineRing.cs` (the stomp ring), `FX/LeafSway.cs`. New data types: `SpellData`, `OrbData`, `GrowthStompData` (a `SpecialData`; the special's size is applied as a scale on the player's Visual, `PlayerController.GrowScale`). The druid's leaf puffs use their own `FX_LeafFall` (real leaves) instead of the Shark set's chip squares. `FormData` gained `magic` and `orb`, and `RangedKind.Orb` was added. `FormRig` gained `spellFocus`, `spellGather`, `focusLight` and `leafSway`. `Explosions.Blast` gained `silent` (damage without the fire FX). Builder: `Editor/ElderTreeBuilder.DruidMask.cs` (menu **8c**, part of `BuildAll()` after the Shark set). Model: `Tools/Blender/build_druid_form.py -- <abs Art/Models/DruidMask>` (body, leaves, staff, orb, mask). New FX: `LeafExplosion`, `OrbBurst`, and `FX_SpellGather` (on the staff). New SFX, appended: `SpellGather`, `VineCast`, `VineBurst` (the package's nature-spell vine clips), `OrbCharge`, `OrbFire`, `OrbImpact` (energy blasts, since you asked for an energy ball). **None heard by ear yet.**

**Verified in play mode (AutoPilot):**
- **Light cast:** gather → release → vines fan out and curl into a training dummy (93 = 72 + 22 squeeze).
- **Charged cast:** 5 vines, 151–153 damage, vines burst out of the dummy.
- **Roll during the wind-up:** cast cancelled, FP unchanged (120 → 120).
- **Two chained casts:** 28 FP, 166 damage.
- **Orbs:** thrown, flying and bursting, with the ball re-gathering between the hands.
- **Mask:** pickup → cinematic → card → druid worn in slot 3.
- **Console:** `BuildAll()` and play mode, 0 errors (the one warning is the old `BossEncounter.primaryFelled`).
- **Special:** grow → stomp → ring → stomp → second ring → shrink. Both rings hit the dummy (72 + 97).
- **Shots:** `22`–`27` in `Docs/Screenshots` (`27` is the two vine rings).

**Not verified:** feel on a pad (cast time, walk speed while casting, orb speed), the sounds, walking while casting (the legs layer is wired like the cannon's but wasn't watched), and the full kill → drop path (the debug spawn uses the same pickup).

**Questions:**
1. The control hints panel still says "Light attack / Heavy attack" in every form. Should the hints follow the form?

## 0000000000000000. Boss: Druid (giant dryad, root feet, topple to hit the face)

**Start it:** pause menu → **Summon Druid** (under Summon Shark), or F1 → row 3. It stands still at the arena edge like a tree with its arms raised, its eyes light up, it tears each root foot out of the dirt, then roars.

Inspired by the "Brr Brr Patapim" meme and the three dryad references, in the PSX dark-fantasy style. It's a giant, about 11 m to the antler tips. The face is bark with Patapim's long, drooping, bulbous nose, a scowling brow, sunken sickly-green eyes and a mouth slit. It has long gnarled arms with twig fingers and two trunk legs. The feet are **splayed roots** (a big toe-root forward, a fan to each side, heel roots behind) instead of big human feet. The whole body is covered in **101 leaf clumps that never stop moving**. They sway on their own, harder while it walks and much harder when it stomps, gets hit or falls, and leaves tear loose on big moments.

| Move | Tell → attack | Counterplay / punish |
|---|---|---|
| Stomp | A root foot rises high over you (it tracks you for ~70% of the tell, weight shifts to the other leg, arms spread), then slams down. 1 stomp, sometimes 2; **3 alternating in phase 2**. | Under the foot: 170, knockdown. Shockwave to 5.4 m: 70, but **only if you're on the ground** (jump it). **Planted** for 0.6 s after. |
| Kick | Draws the near leg back, leaning in over it, then punts through you. 1 kick (2 in phase 2). | A hit (140) **launches you across the arena**. Roll through or step aside. **Off balance** 0.7 s after. |
| Crouch & grab | Sinks low, leans in, both arms rise wide with the fingers spread (0.9 s), then both hands sweep in and down onto you, with a short lunge if you're far. | Roll through it (i-frames make the grab fail). **A miss:** hands thump into the dirt and it's **Stuck** for 1.35 s with its head low, so the face is in reach. **Caught:** it lifts you to its face, crushes you twice (40 each, 3× in phase 2), then hurls you into the dirt (110, knockdown). |
| **Footing → Topple** | Hits on the **root feet** wear its footing down (about 8 clean light hits, fewer with heavies or charged hits). The roots crack and glow ember, the struck knee buckles, and past half it groans and **sways**. Footing regenerates after 4 s without a foot hit. | At zero it wobbles, the feet slide out and it **falls on its back like a felled tree**, face to the sky, for 5.5 s. **Face hits are criticals ×2.6, then ×1.6 on top** (light hits went from ~35 to 150–210 in the test). It pushes itself back up after that. |
| Phase 2 (≤50%) | Sheds leaves and roars. 20% faster, more stomps, kicks and squeezes, brighter eyes, and its footing needs +20 more. | |

Stats on the prefab: 3,200 HP, footing 100, arena radius 15 m. Root hits deal 85% damage because they're tough, but they're how you win. Felling it drops nothing (no `rewardMask` is set yet).

**How it's built**
- **Model** (`Tools/Blender/build_druid.py -- <abs dir>`, exports `Art/Models/Druid/Druid.fbx`): rigid PS1 segments with origins on the joints (pelvis, torso, head, upper arm / forearm / hand ×2, thigh / shin / root foot ×2), plus `Druid_Leaf_<Part>_<n>` clumps (3 cutout cards each, hanging like shingles) whose origin is where they grow. **3,166 tris** (2,560 body + 606 leaf cards). That's over the 2,500 character budget in ART_DIRECTION. It's a giant, and the root feet (338 each) and twig hands (214 each) are where the extra polygons went. Cut the root `sides` or the toe count if it matters.
- **Textures** (`TextureForge`, 128 px, point filtered, no mips): `T_DruidBark` (long vertical plates, black furrows, moss, lichen), `T_DruidRoot` (warmer, knotty, dirt caked), `T_DruidLeaf` (2×2 atlas of hanging leaf sprays, a few dying ones, alpha cutout). Eyes are a dim sickly green (`M_DruidEye`), to tell them apart from the Elder Tree's amber eyes.
- **Animation** (`Enemies/Druid/DruidBoss.cs`) is procedural: feet are **planted in the world and step one at a time** (a step fires when the hip has drifted from its foot, a footfall thuds), and **3D two-bone IK** drives the legs (knees forward) and the arms (elbows back and out). Moves take a foot over (stomp, kick) or set wrist targets. The hips shift over the standing foot, bob with steps, buckle on foot hits and sway once footing is low. The head tracks you. Lying down, a hull of extreme points keeps the body on the dirt.
- **Hit routing:** the root feet and the face (with the nose) have their own hurtboxes with a `DruidPart` (`Enemies/Druid/DruidPart.cs`). It's the nearest `IDamageable`, so `Hurtbox` routes the hit to it and it forwards to the boss with the part. The face reports `IsStaggered` while toppled, so the weapon applies its critical. One blow counts once even if it passes through several hurtboxes (same source within 0.12 s). While toppled, a hit that lands within 1.9 m of the head counts as a face hit even when the shoulder caught the swing first.
- **Grab (player side):** new `PlayerState.Held` (`Player/PlayerController.Grab.cs`). `TryGrab()` fails during roll i-frames. While held, the CharacterController is off and the motor doesn't tick, the boss places the body in its palm every frame, `HeldSqueeze` costs HP (it can kill), and `ReleaseHeld(hit, launch)` throws you as a Slam hit with a launch velocity. `DropHeld()` runs if the boss topples, dies or is sent away mid-hold. Mask swaps are already blocked outside locomotion/aim, so you can't transform in the fist.
- **Builder:** `Editor/ElderTreeBuilder.Druid.cs` → `BuildDruidBoss()` (menu **7c**, part of `BuildAll()` after the Shark). It builds textures, materials, import, the prefab (hierarchy, leaves parented by name, left/right resolved by world position because the FBX conversion mirrors `_L/_R`, palms, hurtboxes, solid shin colliders, hull, lock point) and the scene placement (roster index 2).
- **Sound** (new cues appended to `Sfx`): `WoodGroan` (long door creaks pitched to ~0.55: its roar and strain, no voice), `RootSnap` (cracks pitched down: roots splintering, squeezes), `DeepRumble` (cinematic bass rumble: roar, stomps, the fall). Stomps and footfalls reuse `Slam` / `Footstep` pitched low. **Not heard by ear:** volumes are guesses (0.7–0.8), not loudness-matched like the rest of SoundPicks.
- **AutoPilot** additions: `boss Druid`, `bossmove Stomp/Kick/Grab/Roar/Topple` (Topple knocks it down at once), plus three new general commands: `waitmove X [timeout]`, `viewcam yaw dist height [lookY]` / `viewcam off` (a review camera that follows the focus boss), and `tpboss d` (stand d m in front of the focus boss, negative = behind).

**Verified in play mode (AutoPilot):** intro (dormant, uproot, roar), stalking and stepping, stomp (raise, slam, ring), kick (draw back, punt, launch), crouch-grab both ways (a miss leaves it stuck; a catch goes Held → lift → 2 squeezes → throw → knockdown), real club hits on the root feet draining footing (~12–15 per light hit, shin hits only cost HP), topple onto its back, face crits (~4× body damage), rise, phase 2 roar at 50%, death on its back with the eyes fading. With damage on: stomp −170, squeeze −40, throw −110, kick −140. `BuildAll()` and play mode: **0 errors, 0 warnings**. Shots: `Docs/Screenshots/20_druid_toppled.png`, `21_druid_roar.png`.

**Not verified:** feel with a human on a pad (tell lengths, whether ~8 foot hits is the right count while dodging stomps), the sounds by ear, footing regen pacing, and walking over long distances (it's a slow giant: 2.4 m/s). **Known limits:** like the other bosses it walks through arena props. The grabbed player is a small dark shape inside a big dark fist at night, so it reads mostly through the camera and the squeeze shakes. The pause-menu text for **Shark** still says "three sneakered legs" from before its rework; I didn't touch it.

## 000000000000000. Shark boss is a regular shark now (no legs, no sneakers)

- **Model** (`build_shark_boss.py`): the three legs and the sneakers are gone. It's a regular great shark: body, jaw, tail stock and caudal fin, flatter swept pectoral fins, and new small pelvic fins under the rear belly. 1,130 tris. The body was lowered 0.85 m (`DROP`), so it **swims through the air just above the dirt**: the body centre sits at 1.25 m and the belly floats about 0.4 m up. Its bites now land at the player's chest height.
- **Prefab:** `SharkPivotHeight` 2.1 → 1.25, lock point lowered with it, leg hurtboxes removed, `burrowDepth` 3.15 → 2.3 (the fin breaks the surface by the same amount as before). The sneaker textures and materials are still built because the **Shark form** (player mask) still wears its blue running shoes.
- **Animation** (`SharkBoss.cs`): the leg IK and gait are gone. The tail swims all the time, the head sways against the tail, and the body swells slowly up and down. A new `thrash` value lashes the tail hard (stagger, death, roar). A **hull** of extreme mesh points (`SharkBoss.hull`, built by the editor; the pectoral fins are left out) keeps the lowest point on or above the ground. So a raised head plants the tail, a tail slam props the body up, and a flop rests on the flank. This is off while it dives, erupts and belly-flops.
- **Moves reworded for a legless shark:** the sprint warning is two tail slaps in the dirt (thump and dust) instead of the sneaker scuff. The roar rears up onto its tail. The tornado coil grinds its belly (skid) instead of squealing. "Stranded" and "Landed" lie on the dirt. The stagger and death thrash the tail instead of pedalling. The boss no longer plays `SneakerSqueak` (the Shark form's tornado still does).

**Verified in play mode (AutoPilot, god mode):** the intro (fin, eruption, flop, roar), hovering stalk, bite, tail slam, leap and landing, stagger on its side and death lying flat all look right in screenshots. `BuildAll()` and play mode: 0 errors, 0 warnings. **Not verified:** the sprint and tornado on camera, and how the new hit heights feel on a pad.

## 00000000000000. Audio layers, music, footsteps in every form

- **Layers and a mixer:** `AudioManager` mixes in layers (Music, Ambience, SFX, UI, Footsteps). Each has a volume, a group in the new `Assets/ElderTree/Audio/ElderTree.mixer` (made once by `Editor/GameMixer.cs`, then yours to edit in the Audio Mixer window) and a voice priority. The boss engine and dig loops route to SFX.
- **Sounds stack instead of cutting each other off:**
  - The old pool took the next voice round-robin, so a burst of footsteps and swings could cut a roar, an explosion or a sting mid-play.
  - It's now 40 voices that take a free voice first. They respect caps (6 per cue, 4 footsteps, 6 UI) and otherwise steal the voice with the least left to play: footsteps first, then SFX, then UI.
  - The project's real-voice limit went from 32 to 64. In a 160-play flood test, YOU DIED and an explosion both kept ringing.
- **Music:** the explore track (`music_misty_woods_calling`) plays in the arena and crossfades to the boss track (`music_epic_fallen_empire`) when a fight starts. It falls silent under GREAT ENEMY FELLED, then returns. Loops crossfade before each track's fade-out tail. The pause menu has new **Music volume** and **Effects volume** settings.
- **Footsteps after transforming:** `FootstepFX` now follows whichever animator the controller drives every frame and re-finds the foot bones if the rig changed or was hidden. Tested through the real slot keys: the tree, alligator, shark and back to the tree all play about 9 steps per 2.7 s of walking.
- **Footsteps 30% quieter:** cue volume 0.69 → 0.48.
- `AudioManager.Played` (event per one-shot) and `ActiveVoices` are there for debugging.

**Verified in play mode:** the above, music crossfades both ways, 0 errors. **Not verified by ear:** the music choice and level, and the overall mix.

## 0000000000000. Audio feedback 2: dark UI, physical specials, footsteps that actually fire

- **UI sounds match the game:** the bright app-style UI clips are gone. Menus (F1 debug, pause, title flow) move with a dry switch click, open/confirm with a door latch and close with the latch shutting. Out-of-mana is a locked door rattling, and lock-on is a small metal clink.
- **Shark special (Shark Tornado) and spirit:** new `Sfx.BladeRing` (a blade being drawn) replaces the magic charge on spirit spend, spirit level-up (plus a blade whoosh) and the Tornado wind-up. The spin's turns use `BladeWhoosh` and the weapon-spin whoosh, and the old fan whoosh is gone. The Tornado wind-up's "squeak" (`SneakerSqueak`, shared with Shark) is now a rubber-sole scuff, not a squeaky toy.
- **Alligator special (Sky Bombardment):** new `Sfx.GroundGrind` (stone grinding) on the crouch. The thrown hammer and Crocodile's falling bombs (`BombWhistle`) are a deep incoming flyby instead of a slide whistle. The impact adds `RockBreak` on top of the slam and the explosion.
- **Charged heavy** (all forms): the charge is a leather grip tightening, and the release is a deep whoosh, replacing the magic charge and energy blast.
- **Footsteps fixed:** `FootstepFX` used a fixed 0.105 m foot height and the Elder Tree's planted ankle sits at ~0.11–0.13 m, so steps almost never played. It now learns the planted height per rig (re-learned on mask swaps) and plays a bit louder.
- `Sfx` gained `BladeRing` and `GroundGrind` (appended, so serialized cue ids are unchanged). The `Comedy_Cartoon` and `User_Interface_Menu` folders are no longer used; their clips' import settings were reverted.

**Verified in play mode (AutoPilot):** footsteps fire continuously while walking; F1 opens and closes with the latch sounds; Sky Bombardment plays GroundGrind → flyby → Explosion + Slam + RockBreak; Tornado plays BladeRing → scuff → spin whooshes + blade hits; 0 errors. **Not verified by ear.**

## 000000000000. Audio feedback: quieter bed, no human voices, original cutscene sounds back

- **Ambience:** the night-crickets bed was loud and distracting. It is now a soft low wind (`wind_general_soft_low_loop_02`) at 0.3, about 9 dB under the previous bed (`SoundPicks.AmbienceVolume`).
- **No human voices:** everything from `Voice/*` and the human-performed `Animal_Impersonations` is gone. PlayerHurt is a hollow wooden thud (`Doors/door_old_wooden_close_thud`), JawSnap a lid snapping shut (`Doors/small_box_close`; the comedy bite clips were mouth noises).
- **Transformation cutscene uses the original sounds again:** MaskReceived, MaskOn, MaskScream, Transform and the form roars (CrocRoar, SharkRoar, which the cutscene plays) use the synthesized clips restored to `Assets/ElderTree/Audio` (same GUIDs and import settings, old volumes). The bosses use the same roar cues, so their roars are the originals again too. `SoundPicks.Own(...)` loads them.
- Package clips that are no longer used had their import settings reverted to the package defaults.

**Verified:** 0 errors; in play mode all 55 cues resolve (112 package clips + 8 own) and the wind bed plays at 0.3. **Not verified by ear.**

## 00000000000. Real audio: everything now comes from the Gamemaster Audio package

- **Placeholders gone:** `SfxForge.cs` and all 95 synthesized clips in `Assets/ElderTree/Audio` are deleted. Every one of the 55 `Sfx` cues, the night ambience bed and both boss loops (Crocodile engine, Shark dig) now play clips from `Assets/Gamemaster Audio - Pro Sound Collection` (123 one-shot clips + 3 loops).
- **Sound design lives in one table:** `Editor/SoundPicks.cs` maps each cue to its package clips, volume, pitch range, spatial blend and min interval. Change a line there and run **Elder Tree/5. Build Audio** (also part of `BuildAll()`).
- **Package map:** `Editor/GamemasterAudio.cs` has folder constants, `Load`, `Family` (every variation of a family) and the import settings for used clips (mono, Normalize off, Vorbis 0.7). `Docs/AUDIO.md` has the cue table, the catalog by category with a shortlist for upcoming work, and `Docs/audio_package_families.csv` lists all 1,349 families with variation numbers and lengths.
- **Mix carried over:** cue volumes were set from measured loudness against the old mix (every cue within 3 dB). Ambience bed 0.32 → 0.5, Crocodile engine 0.85 → 1, Shark dig 0.8 → 1, since those package loops are quieter.

**Verified:** 0 compile errors; in play mode all 55 cues resolve (no empty or missing clips), firing every cue starts voices, the ambience loop plays; no asset references the old clips. **Not verified by ear:** the picks were chosen by name, length and loudness. First to audition: Knock (a woody impact thunk; the pure wood-block knocks are ~8 dB quieter), CrocRoar (bear roar), SharkRoar (walrus bark), LockOn, Title.

## 0000000000. Long sword pass 2: new left-to-right light, a real iai, blade roll

- **Light 2 (left-to-right)** is now **Light 1 mirrored**: the same Attack2H01 kesa on a humanoid-mirrored animator state (`Sword_L2`, `mirror = true`), so it answers Light 1 stroke for stroke (Light 1 ends low left, Light 2 low right, same heights, same off-hand IK gap of 0.35 m). The mirrored hand turns the blade over, so it uses `bladeRoll` 120° (edge lead 0.56; unrolled it was −0.28). Moveset v5. Chain: kesa right→left, mirrored kesa left→right, thrust, rising right→left. (The dual-wield backhand and the TreeChopping02 cut tried before are gone.)
- **Iai rebuilt** as a **stance + draw**. The hand settles on the hilt at the left hip with the blade sheathed along the leg (UnsheatheHips01_R held at 0.5). A tap holds it for 0.2 s; holding heavy keeps the stance and charges it (up to ×1.8, with a squash and rumble), and roll cancels it. On release the blade is drawn straight up over the shoulder (a 0.07 s cross-fade) into AttackPolearm02's cut down across with a 2.2 m lunge. The old draw was the pack's everyday unsheathe: upright, arm only, with the blade travelling flat-side first so its curve bent sideways (edge lead 0.13).
- **Heavy chain:** Iai → **Helm-splitter** (Attack2H02, straight overhead cleave) → Kesa from the upper right → Spirit Roundslash.
- **Blade roll per swing:** `AttackData.bladeRoll` turns the sword about its own axis during that swing (blended in over the wind-up) so the edge leads the cut. Values were measured on the rig by sweeping the roll: mirrored Light 2 +120° (0.56), helm-splitter 180° (0.99; that slam swings spine-first), upper-right kesa +10° (0.89), the iai cut and run attack 0° (0.99).
- New AttackData fields: `bladeRoll`, and the stance block `stanceState`, `stanceNorm`, `stanceTime`, `drawFade` (any attack can use them). `SharkSwordMoveset` is v3. `PreviewRenderer.RenderFormSheet` gains `weaponRoll`.

**Verified:** front renders on the shark rig with the rolls applied; in play mode the heavy chain ran Iai (held, CHARGED 125) → Helm → Kesa R → Roundslash and the light chain ran 1 → 2 → 3 → 4. 0 errors after `BuildAll()`. **Not verified by hand:** the feel of the stance timing and the charge on a pad.

## 000000000. Shark Mask adjustments: head, sword edge, katana moveset, spin variety, lock-on while aiming

- **Head rebuilt** (`build_shark_form.py`): one tapered great-white head that flows out of the neck (no neck step), a closed conical snout, slate on top and pale underneath (the old countershading ran side to side), an underslung jaw hanging about 10° open with a dark mouth, two rows of big triangular teeth (larger toward the front), small flush eyes, nostril slits. The horn-like brow spikes are gone. Body is 2,042 tris.
- **Sword:** the cutting edge (pale steel) now faces out and leads every cut. Measured on the shark rig inside each hit window, edge-vs-motion is 0.89 to 0.98 for the kesa, backhand, rising slash, right kesa, roundslash and run attack (the thrust leads with the point). The dorsal-fin hook near the tip is removed. The spine teeth are 10% bigger.
- **Light combo alternates direction:** kesa right→left (Attack2H01), **backhand left→right** (AttackDW02, one-handed), thrust (AttackPolearm01), rising right→left (Attack2H03), then loop.
- **Heavy combo is katana work, with no hammer clips:**
  1. **Iai draw-cut** (UnsheatheHips01_R): the hand drops to the hilt with the blade sheathed back along the left hip, then the sword is drawn low and out to the right while lunging in. **Hold heavy to stay in the stance and charge** (up to ×1.8). One-handed: the off hand stays at the hip.
  2. **Kesa from the upper left** (TreeChopping02).
  3. **Kesa from the upper right** (TreeChopping01): the two cuts cross in an X.
  4. **Spirit Roundslash** (AttackPolearm04): a crouched wheel of the blade while the body turns 360° clockwise with the cut. A spirited hit raises the gauge level.
  `SharkSwordMoveset` is now v2 (the builder upgrades older assets once; later edits survive). New `AttackData.oneHanded` lets the off hand leave the grip for a swing.
- **Shark Tornado varies through the spin:** turn 1 low (blade at the knees), turn 2 rising (blade at the waist, body lifts 14 cm), turn 3 airborne (blade overhead, 50 cm off the ground, leaning in), turn 4 crashes down (landing dust and squash) into the wide finisher. Each turn leaves its own ring of slash at its height. Tune it in `PlayerController.spinTurns` (pose state, frame, lift, lean per turn).
- **Lock-on while aiming:** raising any ranged weapon (bow, cannon, crossbow) drops the lock so you aim freely with the crosshair. Lowering it re-locks the same target if it's still valid and in range. Pressing lock-on while aiming cancels the re-lock.
- Tools: `PreviewRenderer.RenderFormSheet(file, clip, modelPath, weaponPath, …)` renders any form with its weapon and the two-hand IK (`SpawnForm`). The alligator's `RenderCrocSheet` / `SpawnCroc` now call it.

**Verified:** close renders of the head and of each new move on the shark rig; in play mode the heavy chain runs Iai → Kesa L → Kesa R → Roundslash → Iai (the roundslash raised the spirit level), the light chain runs Light1 → backhand → thrust, the spin changes pose, height and lean per turn, and the lock goes off when LT is raised and comes back on release. Console: 0 errors. **Not verified:** how the iai charge and the crossing kesa cuts feel on a pad. The iai draw and the upper-left kesa travel partly flat-side first (edge-vs-motion 0.13 and 0.57), as the pack clips swing them.

## 00000000. Shark Mask (Shark reward)

**Reward:** felling Shark drops the **Shark Mask** beside the wreck (only if you don't own it yet). The first pickup plays the same Majora's Mask cinematic as the Alligator Mask, with deep-sea vortex colours and the Shark's "tra-la-la" roar, then the **SHARK MASK** card. Afterwards it's **slot 2 / D-pad →**. Rewards are now per boss: `BossBase.rewardMask` (Shark = Shark Mask; Crocodile still uses `BossEncounter.rewardMask`). If several bosses fall in one fight, each drops its own mask.

| Shark form | What it does |
|---|---|
| Body | `SharkForm.fbx`, humanoid shark, 1,960 tris, **same skeleton as the Elder Tree** (all pack clips work). Countershaded slate hide and pale belly, tall swept dorsal fin, gill slits, blunt head with an underslung grin of serrated teeth, pectoral fins sweeping off the forearms, crescent caudal fin, a shark-jaw pauldron, and the Shark's worn blue running shoes (plain stripe, no brand). 4% faster, 5% longer roll, no damage reduction. |
| **Shark Long Sword** (Monster Hunter long sword) | Two-handed nodachi: rayskin grip, jaw-bone guard ringed with teeth, tooth pommel, curved blade with a saw of shark teeth on the spine. Long reach (hitbox to 1.58 m). Every swing leaves a **crescent slash** that hangs in the air and drifts out as it fades (`FX/SlashArc.cs`), and hits throw cut streaks. **Light:** overhead slash → thrust → rising slash (loops). **Heavy:** Spirit Blade I → II → III → **Spirit Roundslash** (the body turns a full 360°). **Run attack:** lunging thrust. **Roll attack:** fade slash (cuts while stepping back). |
| **Spirit gauge** | Shown under stamina when the sword is out. Hits fill it. Heavy (spirit) attacks spend 22 for ×1.25 damage and a red-tinted slash. A **spirited roundslash that lands raises the gauge level** (white ×1.05 → yellow ×1.1 → red ×1.2, shown by the pips and the bar colour). Each level lasts 45 s, then drops one. The gauge drains if you stop hitting for 3 s. |
| **Cuts vines** | Blade hits carry `cutPower`. Vines (`World/Cuttable.cs`) are cut at the height the blade meets them: the free part drops and tumbles, hanging stubs snap back up, leaves burst. Once 55-60% of the strands are gone the blocking collider opens. Club, hammer, arrows, bolts and explosions only rustle them. |
| **Shark Crossbow** | Crescent tail-fin prod, hide-wrapped stock, a shark jaw around the bolt exit, side crank, fin sight. Hangs on the right thigh (the dorsal fin owns the back) and the sword goes to the left hip while aiming. LT raises it (Rifle aim clip), RT fires **at once** (no draw), then the string is cranked back with the Rifle reload clip: **1.45 s per bolt** (the bow cycles in about 0.6 s). Bolts: 88 m/s, flat flight, **92 damage / 32 poise every shot** (the bow tops out at 50 at full draw), 12 mana. The loaded bolt is visible in the groove only once it's cranked in. |
| **Shark Tornado** (RB + RT) | Coil, then 4 turns in 1.25 s with the blade held level, drifting with the stick or toward the lock target. Each turn cuts everything within 3.3 m once (36 dmg) and leaves a ring of slash. The last turn is a 4.1 m finishing cut (95 dmg, short slow-mo). It also clears vines. 40 mana, hyper armour. |

**Vines in the arena:** a **vine curtain** hangs from a mossy lintel in the second gap of the wall ring (the one opposite the cracked wall), and three **bramble thickets** grow in the yard. F1 → "Restore breakables + vines" regrows them.

**All tuning is in ScriptableObjects** (created only when missing, so your edits survive rebuilds): `Forms/Form_Shark`, `Equipment/Melee_SharkLongSword` (slash colours, cut width, spirit gauge numbers), `Equipment/Crossbow_Shark`, `Equipment/Special_SharkTornado`, `Masks/Mask_Shark`, `SharkSwordMoveset` (attack timings, `spinDegrees`, `levelsSpirit`). New data types: `CrossbowData`, `SpinSlashData`, and `SpecialData` (shared base of the specials, `SpecialAttackData` now derives from it). `FormData` gained `crossbow`, `roar` / `roarPitch`.

**Debug:** F1 lists a pickup row for each boss's mask (Alligator, Shark). Autopilot: `wear shark|alligator|none` (instant form swap), `spirit <level>` (fills the gauge).

**Code map:** `Player/PlayerController.Shark.cs` (spirit gauge, slash colours, crossbow fire/reload, spin special), `PlayerController.Form.cs` (shouldered ranged path now serves cannon and crossbow; special dispatch by type), `FX/SlashArc.cs`, `World/Cuttable.cs`, `CannonRig` (optional `loadedRound`, eases off the aim lock while reloading), `Editor/ElderTreeBuilder.Shark.cs` (menu **8b**, part of `BuildAll()` right after the Alligator set). Models: `Tools/Blender/build_shark_form.py -- <abs Art/Models/Shark dir>` (body, sword, crossbow with LoadedBolt + Muzzle, bolt, mask, vine segments, lintel, root mound). New animator states (all Human Mega pack): `Sword_L1-3`, `Sword_H1-4`, `Sword_Run`, `Sword_Dodge`, `Sword_Spin` (2H / Polearm clips), `CrossbowAim/Shoot/Reload` (Rifle clips). New SFX: blade whoosh / hit, vine cut / rustle, crossbow fire / crank, spin whirl. New FX: `SlashHit`, `LeafBurst`.

**Verified in play mode** (autopilot, god mode): sword light chain (135 dmg on a dummy) and spirit chain into the roundslash (518 dmg, gauge reached level 1), crescents visible on every swing, spin special (246 dmg on one dummy, level horizontal rings), two crossbow bolts (186 dmg) with the reload animation between them; light slash + heavy + spin cut all 25 strands of the curtain and the shark walked out through the gap; Shark killed → Shark Mask rose by the wreck → full cinematic → shark form worn, slot 2 owned; spirit HUD bar and pips. Console after `BuildAll()`: 0 errors, 0 warnings.

**Not verified:** feel on a real pad (timings, crossbow reload length, spin drift), the keyboard path, the thickets (only the curtain was cut in the test), and close-up hand placement on the crossbow during the reload clip. Sword hit windows reuse the alligator's measured 2H / Polearm windows (same clips, same skeleton), not a fresh `AnalyzeCrocSwing` pass on the sword.

**Questions:**
1. The spirit gauge levels up only on a spirited roundslash that lands (as in Monster Hunter). Do you want a Foresight Slash (counter) too?
2. The vortex in the cinematic still goes magenta at its peak (the hue-shift effect runs over the mask's colours). Do you want it to stay in the deep-sea blues for the shark?

## 0000000. Boss: Shark

**Start it:** pause menu → **Summon Shark** (new entry under Summon Crocodile), or F1 → row 2. It arrives underground: the dorsal fin circles you once, it erupts, belly-flops and screams on its back sneaker.

A scarred slate shark on three legs in worn, mud-caked blue sneakers (no brand marks: a plain side stripe and a pale toe cap). It's a ground boss. Every attack is announced by the body, with no ground markers.

| Move | Tell → attack | Counterplay / punish |
|---|---|---|
| Sprint (run through the arena and bite) | Scuffs its back sneaker twice (squeak, dust), crouches, then sprints at you with limited homing; jaws open as it closes, then a lunge bite. Runs through, skids round, goes again. 2 passes (3 in phase 2). Body contact also hurts. | Roll through the lunge. **Winded** for 1.4 s at the end. |
| Bite combo | Rears its head, jaws wide, then snaps forward. 2 bites (3 in phase 2). | Roll sideways. |
| Tail whip | You're on its flank or behind it: it turns away with the tail cocked, then whips round 215°. | Back off or roll toward the head. |
| Tail slam | You're behind it: the nose dips, the back leg lifts, the tail rises high and crashes down (ring + debris). | Leave the line behind it. |
| Shark tornado | You hug it or keep hitting it (pressure builds): it coils with the sneakers squealing, then spins 3.5 turns (5 in phase 2) while drifting at you. Multi-hit. | Run. **Dizzy** for 1.5 s after. |
| Spin leap | Mid range: a deep crouch, then a spinning leap that belly-slams where you were heading. | Roll out. **Landed** for 0.85 s. |
| Burrow | Rears up and dives nose first. Only the fin cuts through the dirt (dust trail, dig rumble), circling you, then it cuts in. The fin slips under and **the ground boils** where it will come up (0.78 s, 0.62 in phase 2). It erupts jaws first, nose to the sky, then belly-flops. Twice in phase 2. Can't be hit while underground. | Move off the boiling spot. **Stranded** for 1.5 s after the flop. |
| Stance break | Flops onto its side with the sneakers pedalling: 3.4 s critical window. | |
| Phase 2 (≤50%) | Rears up and screams; 20% faster, more passes, bites, spins and eruptions. | |

Stats on the prefab: 2,600 HP, 130 poise, arena radius 15 m. The walls have gaps, so the boss's limit stretches to follow a player who slips outside (up to 2.5× the radius). Otherwise it couldn't reach you and you could wait it out. Felling Shark alone doesn't drop the Alligator Mask (that stays Crocodile's reward); it drops the Shark Mask (§00000000).

**Animation is procedural** (PS1-style rigid segments, no skeleton): a three-beat gait drives **two-bone IK** on each leg (pivot space, knee forward), so the sneakers stay on the ground at any body height; feet stay flat and lift their toes in the swing. The tail swims side to side with speed. Jaw, tail pitch and yaw, body pitch, roll, lift and burrow sink are pose targets that each move sets, smoothed in `LateUpdate`.

**Shared boss base:** `Enemies/BossBase.cs` is what the encounter, boss bar, F1 roster and autopilot use (`DisplayName`, `MaxHealth`, `StateLabel`, `Damaged` / `Felled`…). `CrocodileBoss` now derives from it (its fields are unchanged), and `BossEncounter.bosses` is a `List<BossBase>`. To add a boss: derive from `BossBase` and add it to the list (`PlaceBoss` → `PlaceSharkBoss` shows how). `HUD.SummonBossRequested` is now `Action<int>` (roster index).

| Piece | Where |
|---|---|
| Model (1,928 tris: body 682, jaw, 2 tail segments, thigh/shin/sneaker × 3) | `Tools/Blender/build_shark_boss.py -- <abs dir>` (exports straight into `Art/Models/SharkBoss`) |
| AI, gait + IK | `Enemies/Shark/SharkBoss.cs` |
| Textures (scarred hide, pale dirty belly, mesh-weave sneaker, tread sole), materials, prefab, hurtboxes on every bone, scene | `Editor/ElderTreeBuilder.SharkBoss.cs` → `BuildSharkBoss()` (menu **7b**, part of `BuildAll()` right after `BuildBoss`) |
| SFX: "tra-la-la" three-syllable gargled roar, sneaker squeak, burrow burst, seamless dig loop | `SfxForge` (Shark section), cues `SharkRoar`, `SneakerSqueak`, `Burrow` |
| Autopilot | `boss Shark` spawns it and makes it the focus of `bossmove Sprint/Bite/TailWhip/TailSlam/Spin/Leap/Burrow`, `bosshp`, `lockboss`, `bossaway` and `log` |

Verified in play mode with the autopilot: intro, every move (forced and natural), bite, lunge, spin and eruption hits landing, stance break, phase 2, death, pause-menu summon, the F1 roster, no mask drop on a solo kill. Console: 0 errors, 0 warnings. Shot: `Docs/Screenshots/19_shark_stranded.png`.

**Not verified:** how fair it feels with a human on the pad (roll timing against the lunge and the eruption tell). All tells and windows are fields or constants at the top of each move, so they're easy to tune. **Known limits:** like Crocodile, it runs through arena props. The legs don't splay sideways (IK is in the sagittal plane only), and with three legs the turn-in-place step is a scurry, not a pivot.

## 000000. Adjustments: grounded roll, two-handed hammer, heavy chain, special camera

- **Roll** now plays the pack's **Roll01** (the in-place version). It used to play Dodge01, a sidestep, while the whole model spun 360° around a pivot 1 m above the ground, which is why it floated. The procedural spin is gone. The character's travel follows the clip's own root motion (`Roll01 [RM]` RootT.z, as a table in `PlayerController.Dodge.cs`): a fast push-off, then the travel stops once the feet are back under the hips. Roll length is 0.8 s and plays the clip from 2% to 80% (`rollClipRange`). I-frames are unchanged. The backstep keeps Dodge01. This applies to both forms.
- **Two hands on the hammer, always.** The alligator now has its own override controller (`Animation/ElderTree_Croc.overrideController`): the idle is **CombatIdle2H01** and the guard is **Parry2H01**. A new arms-only layer (`TwoHandArms`, mask `M_Arms`) keeps the 2H hold on the arms during walk, run and jumps. The hammer is gripped like the pack's own 2H weapons. The builder reads HumanM's `B-handProp.R` prop bone on the same CombatIdle2H01 frame and transfers that grip to the alligator's hand (`CalibrateHammerGrip`). **`TwoHandGrip`** (new) then pins the left fist to the shaft after the animator, with two-bone IK on both arms. The alligator's wider shoulders leave its left fist about 35 cm off the handle, so the right hand also pulls the hammer in, by at most 18 cm. Each clip keeps its own hold: the 2H clips stack the hands, while the Polearm and Mining clips slide the left hand up the shaft. Measured after IK: the fist stays within 1 cm of the shaft in every hammer clip (7 cm for one instant of the leaping finisher). The grip lets go during rolls, hits, the cannon, the throw and transformations.
- **Hammer moveset v2** (`CrocHammerMoveset`, all pack clips, with windows measured on the alligator by `PreviewRenderer.AnalyzeCrocSwing`):

| | Clip | Notes |
|---|---|---|
| Light 1 | Attack2H03 | low sweep; the hit window opens **0.16 s** after the press (was ~0.73 s) |
| Light 2 / 3 | Attack2H01 / Attack2H02 | diagonal / wind-round ground slam |
| **Heavy 1** | Mining01 (overhead) | chargeable at the top, ground slam |
| **Heavy 2** | AttackPolearm03 | chargeable, deep crash down |
| **Heavy 3** | Attack2H04 | leaping finisher (not chargeable), 3 m slam |
| Run / Roll attack | AttackPolearm02 / AttackPolearm01 | two-handed lunge / thrust |

  The v1 heavies reused the club's heavy states, which the lights also played, so the hammer had no heavy chain of its own. `Moveset.version` is new: the builder rewrites the combos only when the asset is older than `CrocMovesetVersion` (2). Your future edits to the asset still survive rebuilds.
- **No weapon glow:** charged heavies and the Sky Bombardment hang no longer light the weapon (emission and the tip flash are removed). A charge now reads through the charge sound, the FOV punch and a rumble on release.
- **Sky Bombardment camera:** at the top of the leap the camera takes over (`CameraDirector.FocusOn`). It pulls back 2.6 m, rises to 24°, and turns to frame the impact point between you and the blast. Look input is ignored while it holds. It lets go 0.9 s after the blast (`ReleaseFocus`) and eases back, and the stick works again right away. Tune it in `Special_SkyBombardment` (Camera section). If the special is interrupted, the camera returns after 0.35 s.

**Verified** (play mode, autopilot, god mode, alligator forced on): idle, walk and run with both hands on the shaft (grip gap ≈ 0); L1 → L2 → L3 and H1 → H2 → H3 chain; ground blasts on the heavies; roll on both forms (no spin, 3.8 m of travel, front-loaded); the special's camera pulls back to 7.2 m, rises to 24°, holds on the blast (all three dummies hit), releases, and then the right stick turns the view again. Console: 0 errors, 0 warnings. **Not verified by hand:** how the timings feel on a real pad, and the guard pose with the 2H parry. The preview renders are too low-res to judge the finger wrap closely.

## 00000. Alligator Mask (Crocodile reward) + feedback pass

**Reward:** felling Crocodile drops the **Alligator Mask** beside the wreck (only if you don't own it yet). Walk into it to pick it up. The first time, a Majora's Mask–style cinematic plays: the mask is raised to the face and clamps on, then the world drops away into a churning vortex (only the body is drawn) while the body screams and flickers faster and faster between Elder Tree and alligator in hard camera cuts (face, low spin, overhead, profile), with bone-scale body horror, chromatic aberration, lens warp and hue shift. A white-out follows, the alligator roars, and an **ALLIGATOR MASK** card shows the controls. Skippable after 2.5 s (B / Esc / A). Ownership is saved in PlayerPrefs (`ElderTree.Masks.Owned`).

**Masks:** slots on **1-4 / D-pad ↑ → ↓ ←** (alligator = slot 1 / ↑). Press a slot to put the mask on (a quick 0.6 s hand-to-face transformation, invulnerable), press it again to return to the Elder Tree. Empty or unowned slots shake in the HUD's mask strip (above the equipment cross).

| Alligator form | What it does |
|---|---|
| Body | `CrocForm.fbx`, humanoid alligator, 2,548 tris, **same skeleton as the Elder Tree** (all pack clips and sockets work), PS1 rigid skinning. Croc hide, pale belly scutes, toxic eyes, leather aviator cap and goggles, riveted bomber-metal pauldron, bomb bandolier, tail with a scute crest. Takes 15% less damage and ignores light flinches; 6% slower, shorter roll. |
| Crocodile Hammer | Drop-bomb head with a toothed croc nose. 3-hit light chain + 2 chargeable heavies on the pack's 2H clips. **Every swing that connects explodes once**, whether it hits an enemy, an obstacle, or the ground (overheads and slams). Breaks obstacles. |
| Crocodile Cannon | Engine-nacelle cannon with croc jaws. LT raises it (Bazooka clips; the spine pitches to the aim and the barrel is aimed at the crosshair every frame), RT fires a shell that explodes. 1.5 s reload (the crosshair closes as it reloads, gold when ready), 62 m/s, 140 m range, 95 direct + 60 blast. 22 mana. |
| Sky Bombardment (RB + RT) | Crouch, leap 5.5 m, hang, hurl the hammer down at the lock target (or 6 m ahead): 6.5 m blast (260 dmg), secondary pops, slow-mo, then the hammer flies back. 1 special charge. Hyper armour while it runs. |

**All tuning is in ScriptableObjects** (`Assets/ElderTree/Data`). The builder only *creates* them when they are missing, so your edits survive rebuilds:
`Forms/Form_ElderTree`, `Forms/Form_Alligator` (loadout + body handling), `Equipment/Melee_ElderClub`, `Melee_CrocodileHammer` (explosion radius / damage, break power), `Bow_Elder` (mana per arrow, draw, flight, damage, lead), `Cannon_Crocodile`, `Special_SkyBombardment`, `Masks/Mask_Alligator` (cinematic colours, card text), `CrocHammerMoveset` (attack timings; clip lengths are refreshed on build). Note: `ElderTreeMoveset.asset` is still rewritten by `BuildMoveset()` as before.

**Breakables:** a cracked wall plugs one gap in the arena ring and three boulders sit around the yard (`World/Breakable.cs`, prefabs in `Prefabs/Breakables`). Only blows with `breakPower ≥ requiredPower` shatter them into tumbling chunks: the hammer, cannon shells and Sky Bombardment. The club and arrows clank off with sparks.

**Debug (F1, now "BOSSES & TESTS"):** besides the boss roster, **Alligator Mask (pickup)** spawns the pickup 3 m ahead (it plays the cinematic if the mask isn't owned), **Forget all masks** resets ownership and returns you to the Elder Tree, and **Restore breakables** puts the obstacles back.

**Feedback addressed:**
- **Bow precision:** an unreachable aim point (open sky, or too far for a weak draw) used to fall back to a **45° lob** over the crosshair. Now the arrow flies straight at the point, and open sky aims 60 m down the view ray. While locked on, arrows **lead moving targets** (velocity tracked, iterated on flight time) unless you steer the lead offset yourself. A shot that enters a hurtbox always lands, even when it misses the mesh-ellipsoid test that decides where it sticks (it used to pass through wings and thin parts). Arrow speed went from 22-60 to 34-72 m/s.
- **Boss too easy:** the red ground rings are gone. **Bombs cast a shadow**: a dark soft blob under the falling bomb that tightens and darkens as it comes down. That and the whistle are the only warning. Precision strikes lead you harder and, in phase 2, drop a second bomb where you stand. **On the ground** the croc now fights with body-language telegraphs, not markers: a bite combo (rears up with jaws gaping, then lunges; 2 bites, 3 in phase 2), a wing sweep at a flank (turns away, cocks the wing, whips round), a tail slam behind (the nose dips so the tail rises, then it crashes down), a propeller spin when you hug or spam it (crouches as the props scream up, whirls 1.5 turns, then stays dizzy as a punish window), a hop slam at mid range (bounces onto you and shakes bombs loose), and a bomb belch at range (rears back and lobs a spread). The grounded window is 9 s.
- **Special = light + heavy together** (RB + RT within 75 ms on a pad; single presses wait that long before they count). Y no longer does anything. Keyboard keeps Shift + RMB.
- **Block on LB:** guard was already bound to LB on the pad (Left Alt on keyboard) and nothing changed. See question 1.
- **Mana instead of ammo:** FP became **mana** (blue bar). It regenerates over time, and every spend stops regeneration for **2 s** before it resumes. Arrows (7) and cannon shells (22) cost mana. When you don't have enough, you hear a dry click and the bar blinks. (Specials no longer use mana: see **Special charges**.) The equipment cross shows the cost per shot (◆ 7 / ◆ 22).

**Code map:** `Runtime/Equipment/*` (SO types), `Runtime/Masks/` (`MaskSystem`, `MaskPickup`, `MaskCinematic`, `FormRig`), `Player/PlayerController.Form.cs` (form swap, hammer blasts, cannon, Sky Bombardment, quick transform, cinematic hold), `Player/CannonRig.cs`, `Combat/Explosions.cs`, `Combat/CannonShell.cs`, `World/Breakable.cs`, `UI/HUD.Masks.cs`, `Editor/ElderTreeBuilder.Masks.cs` (menu **8. Build Alligator Mask set**, part of `BuildAll()`; note the new order: Boss → Mask set → Prefabs), boss AI in `CrocodileBoss.cs` (grounded melee section). Models: `Tools/Blender/build_croc_form.py -- <abs out dir>`, then copy the 7 FBX into `Art/Models/Croc`. New SFX in `SfxForge` (deflect, rock break, cannon, mask on, scream, transform swap, mana empty, mask fanfare). New animator states, all from the Human Mega pack: SpecialHold/Throw (ThrowBigRock01), CannonAim/Shoot (Bazooka_Aim01), MaskDon (Drink01_R), MaskScream (Pain01), MaskRoar (Angry01).

**Verified in play mode** (autopilot, god mode): pickup → full cinematic → alligator; the hammer combo and charged heavy explode on the dummies; the cannon fires, explodes and costs 22 mana; Sky Bombardment rises to 5 m and its blast hits all three dummies; mana resumes after the 2 s pause; D-pad ↑ swaps back to the Elder Tree; the club clanks off a boulder and the hammer shatters it; the grounded croc chains bites with the rear-up telegraph; bomb shadows show on the ground. Console: 0 errors, 0 warnings. **Not verified:** the full kill → drop-at-the-wreck path (the debug spawn uses the same pickup), every grounded move in turn (only bites were captured), the keyboard chord, and how the difficulty feels by hand.

**Questions:**
1. **Block on LB:** it was already LB on the pad. Did something else feel wrong (for example guard not working while locked on or right after aiming), or did you mean the keyboard (currently Left Alt)?
2. On keyboard, should the special also be LMB + RMB? It's still Shift + RMB, because RMB alone raises the bow.
3. Mask ownership persists between sessions (PlayerPrefs). Is that what you want, or should it reset each run until there's a save system?

## 0000. Animation: Human Mega Animations Pack is the source of truth

All player motion now comes from **Kevin Iglesias / Human Mega Animations Pack** (`Assets/Kevin Iglesias/Human Animations/Animations/Male`). Don't mix in other packs: the ExplosiveLLC jump/fall/land were the last outside clips and are gone.

**Rig retargeted onto the pack's HumanM skeleton** (`Tools/Blender/build_elder_tree.py`, 2,264 tris, 51 bones):
- Joint heights and segment ratios follow `HumanM_Model` × 0.95: hips 0.93, spine 1.01, chest 1.20, neck 1.455, head 1.525, shoulders raised from 1.25 to **1.38** (they used to sit inside the trunk, so pack poses with raised arms buried them). Shoulders stay wider than HumanM (0.30 vs 0.19) so the arms clear the trunk.
- Same chain as the pack: **no UpperChest** (shoulders parent to Chest).
- **5-finger hands, 30 finger bones**, laid out like HumanM's hand. The twig fingers are modelled straight; the pack's clips curl them (fist on the club, closed bow hand). The old pre-curled right fist is gone.
- Unity import (`ConfigureCharacterImport`): fingers are mapped (Blender bone = HumanTrait name without spaces), **default muscle limits** like the pack's own avatar (the old damped spine/neck ranges muted the torso twist), and the skeleton description is rebuilt from the file each time (a stale one invalidates the avatar).
- Check tool: `PreviewRenderer.RenderCompareSheet(file, clip)` draws the pack's HumanM (top) and the Elder Tree (bottom) on the same frames; `RenderBoneCloseup` zooms on one bone. Shot: `Docs/Screenshots/17_pack_retarget_compare.png`.

**Clip map** (`ElderTreeBuilder.Animation.cs`):

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

## 000. Boss: Crocodile

**Start it:** pause menu → **Summon Crocodile**. The training dummies leave the field, and the boss flies in low over the arena, roars, and climbs into its orbit. **Reset Training** ends the fight.

**Debug: F1** opens a boss roster listing every boss in `BossEncounter.bosses` (the primary `boss` is added automatically). ↑/↓ + Enter or 1-9 spawns / despawns one, 0 despawns all; the game pauses while it's open. Several bosses can be up at once: the bar follows the next living one, and victory waits until all are down. To add a boss, put its `CrocodileBoss` in the list.

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
| Model (4,116 tris; Santos-Dumont 14-bis fused with the croc: its hide is the fuselage, stretched over dark ash longerons and hoops, forelegs clutch the canard, hind legs grip the gear, tail curls under; hide webbing closes the canard around the head and fills the wing cells; Antoinette V8, one two-paddle pusher prop, separate jaw), aerial cannonball (200), living cannonball (578). Built live through the Blender MCP socket or headless | `Tools/Blender/build_crocodile.py`: run headless with `-- <abs out dir>`, then copy the 3 FBX to `Art/Models/Boss` |
| AI: flight (banked steering, Hermite paths), moves, stance, phases | `Enemies/Crocodile/CrocodileBoss.cs` (all tuning is on the prefab) |
| Bombs, markers, walkers, blasts | `AerialBomb.cs`, `LivingBomb.cs`, `Blast.cs` |
| Fight flow (summon, dummies, death and victory) | `BossEncounter.cs` (on GameSystems) |
| HUD: bottom boss bar with chip and damage number, YOU DIED, screen fade, menu entry | `UI/HUD.Boss.cs`, `ElderTreeHUD.uss` |
| Textures (riveted rusty aluminium, croc scutes, teeth, flesh, marker), materials, FX (explosion with light flash, bomb pop, smoke), prefabs | `Editor/ElderTreeBuilder.Boss.cs` → `BuildBoss()` (part of `BuildAll()`) |
| SFX: explosion, bomb whistle, beep, croc roar, jaw snap, metal hit, player hurt, skid, YOU DIED, GREAT ENEMY FELLED, a seamless four-engine drone loop | `SfxForge` (Crocodile section) |
| Autopilot commands | `key F1` (tap any keyboard key), `boss`, `bossmove Carpet/Strike/Living/Charge`, `bosshp <dmg> [poise]`, `lockboss`, `god 0/1` |

Verified in play mode with the autopilot: every move, hits landing on the player, melee and arrows damaging the boss, stance break → crash/stagger → criticals, phase 2, death with the banner, and player death → respawn. Console: 0 errors, 0 warnings. Shots: `Docs/Screenshots/16_crocodile_fight.png`.

**Known limits:** the arena props (lamps, crates, hut) don't block the boss; it taxis through them, and a living bomb can land on a lamp top. The plane has no skeleton (procedural jaw and props only). There is no boss music yet; the engine drone carries the mood. `TextureForge.BuildIron` (and a few older textures) use `Mathf.SmoothStep` as if it were a threshold, but it interpolates between its first two arguments. The boss textures use a proper smoothstep; the older ones were left alone.

## 00. Character v3: Elder Tree (PSX dark fantasy)

- **Art direction** now lives in `Docs/ART_DIRECTION.md` (from the 91 images in `Assets/References`, the 4 treant images and PS1 research). Check every new model against it.
- `Tools/Blender/build_elder_tree.py` builds the player as a low-poly treant: **2,222 tris**. 12-sided fluted trunk with a carved face (angry V brow, deep ember-lit sockets, long drooping nose), moss-strand beard cards, 8-branch antler crown with sparse dead-leaf cards, shoulder burls and spurs, root skirt over the thighs, gnarled 6-sided arms with twig fingers (now finger-rigged, see §0000), and root-toe feet.
- Skeleton: superseded by the pack retarget (§0000). It used to be the old log's 22-bone humanoid.
- **Textures**: `T_ElderBark.png` (256², point filtered, no mips, uncompressed) is painted per texel: bark fibres, fissures, plates, cross cracks, knots, lichen, moss on up-facing surfaces, ground grime, ember glow around the eyes. All of it is multiplied by baked AO and a soft top light, then quantised to 15-bit with a 4×4 Bayer dither. `T_ElderMoss.png` (256², alpha cutout) holds the moss strands, leaves and hanging roots.
- **Materials** (`BuildElderMaterials()`): `M_ElderBark` (smoothness 0.04, no specular or reflections), `M_ElderMoss` (alpha clip, double sided), `M_ElderEye` (dim amber emission).
- `ElderTreeBuilder.ModelPath` → `ElderTree.fbx`. The old log is kept at `LogModelPath`.
- Rebuild: `blender -b --factory-startup --python Tools/Blender/build_elder_tree.py -- Tools/Blender/export --bake Assets/ElderTree/Art/Textures`, copy `ElderTree.fbx` into `Assets/ElderTree/Art/Models`, then `ElderTreeBuilder.BuildAll()`. The bake takes about 5 s. Live look-dev: run the builder with `TS_EXPORT=False`, then `preview_elder_tree.py` (`ET_Cam*` cameras).
- Shots: `Docs/Screenshots/11_elder_tree_ingame.png`, `12_elder_tree_blender.png`, `13_elder_tree_atlases.png`. Play mode console: 0 errors, 0 warnings.
- **World pass (PSX dark fantasy)**: every lit material now uses `ElderTree/PSXLit` (vertex snap + 15-bit dither), the game renders at 0.4 scale with point upscaling, the world textures are 64–256 px dithered and point filtered, and the props, trees, club, bow, arrow and quiver were rebuilt low-poly. The sky, fog, lights and grade are now indigo/violet. Details in `Docs/ART_DIRECTION.md` §4, shots in `Docs/Screenshots/14_psx_world_sheet.png` and `15_psx_world_full.png`. Play mode console: 0 errors, 0 warnings.
- Rebuild the world: `blender -b --factory-startup --python Tools/Blender/build_props.py -- Assets/ElderTree/Art/Models/Props`, `... build_psx_weapons.py -- Tools/Blender/export` (copy `ElderClub.fbx` to Models), `... build_bow.py -- Tools/Blender/export --icon Assets/ElderTree/UI/Icon_Bow.png` (copy the three FBX), then `ElderTreeBuilder.BuildAll()`.
- Unused after the pass (safe to delete when you review): the `T_*_Normal.png` world normal maps, `T_LogClub_*`, and the old `LegacyLog_Club.fbx` (kept at `LogClubPath`).

## 0. Character v2 (reference-matched model)

- `Tools/Blender/build_legacy_log.py` now sculpts the face as a height field on a dense lathe (carved brows, socketed wide-open eyes with lids, bulbous nose, apple cheeks, smirking grin, nasolabial folds), chunkier log (r 0.27), thicker legs, big feet, gripping right fist, plain-wood bat.
- Textures are **baked** (`--bake <Assets/ElderTree/Art/Textures>`): `T_LogBody_Albedo/Normal` (4K, albedo x local AO, seam-free cylindrical face UVs), `T_LogClub_*`, and a generated `T_LogEye_Sculpt` (dark-brown iris, black pupil). `BuildArt()` uses them when present, else falls back to the procedural tileables.
- Look-dev in live Blender via MCP: run the builder with `TS_EXPORT=False`, then `Tools/Blender/preview_legacy_log.py` (pose, warm lamp lights, `CamBody/CamFace/CamFeet/CamHand`).
- Rebuild: `blender -b --factory-startup --python Tools/Blender/build_legacy_log.py -- Tools/Blender/export --bake Assets/ElderTree/Art/Textures`, copy the two FBX into `Assets/ElderTree/Art/Models`, then `ElderTreeBuilder.BuildAll()`.
- Comparison: `Docs/Screenshots/10_reference_vs_model.png`.

## 1. What was built

| Request | Status | Notes |
|---|---|---|
| 3D model of Elder Tree from the image, in Blender | ✅ | Built by a Python script run in **headless Blender 5.3** (see §3). Log body, carved brows/nose/smile, bulging eyes with heavy lids, thin limbs, fists, bare feet with toes, and a separate baseball-bat club. Source `.blend` + previews in `Tools/Blender/export/`. |
| Unity Humanoid rig | ✅ | 22-bone armature named to Unity's HumanTrait names, explicit humanoid mapping, computed skin weights. Avatar imports **valid / humanoid**. Spine/neck muscle ranges are narrowed so the log stays rigid instead of bending like rubber. |
| Retarget the imported animation packs | ✅ | Kevin Iglesias (locomotion 8-way, 1H/2H attacks, dodge, hits, guard), ExplosiveLLC 2-Handed Warrior (jump/fall/land). Vendor import settings untouched (a looping copy of *Fall* lives in our folder). |
| Attacks that sell the club's movement, strength, weight | ✅ | Clips chosen by contact-sheet review; hit windows **measured from the club-tip speed curve** of each clip. Per-phase playback speed (slow wind-up → fast snap → heavy follow-through), lunges, hit-stop, impulse shake, FOV punch, rumble, wood-knock audio, splinters. |
| Cinemachine third-person camera | ✅ | Cinemachine 3: OrbitalFollow + RotationComposer + Deoccluder + ImpulseListener, driven by `CameraDirector`. |
| Light + heavy attacks | ✅ | 4-hit light chain, 2-hit heavy chain with **hold-to-charge**, running attack, roll attack, jump (plunging) attack, special "Elder Slam" (1 charge) with ground shockwave. |
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

Exact frame data varies by source and patch (the wikis disagree on light vs medium i-frames), so values are starting points, all exposed on the prefab / `ElderTreeMoveset.asset`.

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
2. **No fingers/grip pose** — the Elder Tree's hands are sculpted fists; the club is parented to the right hand. Two-handed heavies show the left hand near, not on, the handle.
3. Arms can clip the log during wide swings (a log is wider than a human torso).
4. Hut sign uses a legacy `TextMesh`; fine at distance, soft up close.
5. The dummies don't attack, so guard/hit-reaction states exist but only matter once an enemy fights back.
6. Audio is fully synthesized — good for feel, but placeholder next to real foley.

## 5. Questions for you

1. **Combat identity:** should the club feel *heavier* (Great Hammer: slower, more stance damage, fewer hits) or *snappier* (current: Hammer class)? This decides the light-combo speed curve.
2. **Enemies next?** Do you want a sparring enemy that attacks back (parry/guard/dodge become meaningful), or a boss (a giant *kentongan* drum? a rival "Ballerina Cappuccina"-style brainrot character)?
3. **Art direction:** keep the moonlit stylized-PBR look, or go toward the reference's warmer, photoreal-ish lamp lighting?
4. **Roll:** keep the comedic full log-tumble, or a grounded side-roll? Can I pull a roll animation from Mixamo or another pack?
5. **Character proportions:** happy with the model? Changes like a thicker log, longer legs or bigger eyes are one-number tweaks in `build_legacy_log.py`.
6. **Keyboard layout:** ER-faithful (Shift+LMB heavy, RMB guard) or the common action layout (LMB light / RMB heavy)?
7. **Audio:** keep synthesized SFX, or should I wire a slot for real recorded kentongan hits and a voice bark?
8. **Target platform & performance budget** (PC only? Steam Deck? consoles)? That decides shadow/post quality tiers.
9. Do you want the **Blender MCP** set up so future model edits happen live in your Blender session?

## 6. Suggestions (ranked)

1. **An enemy that fights back**: reuse the humanoid pipeline with the DoubleL *Enemy_Attack* / *Hit* clips; add a player hurtbox, guard/parry and posture, and the existing `Hit` state comes alive.
2. **Riposte / critical animation**: a dedicated finisher ("THWACK!" overhead with a camera cut) instead of a damage multiplier.
3. **Weapon arts and two-handing (Y+RB)** switching to the 2H idle and a 2H moveset, so the club gets an ER-style stance toggle.
4. **Dawn-themed objective**: wake the village before dawn (hit kentongan drums in order, a timer to sunrise, lighting goes from night to dawn), a natural "first level".
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
| HUD: crosshair whose ticks close with the draw, gold pulse at full draw, warm dot over targets, hit marker; bow slot + ∞ in the equipment cross; lock reticle fades while aiming | `UI/HUD.Bow.cs`, `ElderTreeHUD.uss` |
| SFX: draw creak, release twang, nock click, arrow thunk, full-draw chime | `SfxForge` bow section |
| Build | `ElderTreeBuilder.Bow.cs` (`BuildBowAssets`, `AttachBow`), included in `BuildAll()` |

Verified in play mode with the autopilot (raise, draw, full-draw hit, rapid fire, aim from a run, dodge out of aim, lower). Console: 0 errors.

Also removed the ExplosiveLLC `Editor/SetupInputLayers.cs` popup ("Warrior Animation Mecaim…"), which reopened after every asset import.

**Known limits / next ideas:** no dedicated upper-body bow clip (the pose is IK-driven; the DoubleL `Bow_Attack_*` clips don't suit the Elder Tree's log body); no arrow pickup or ammo types yet (infinite by design); the arena is small, so a far archery target would show the arc better.

## Special charges (2026-11-20)

Specials (RB + RT: Elder Slam, Sky Bombardment, Shark Tornado, Wrath of the Grove) cost **one special charge** instead of mana. Mana still pays for ranged shots and the druid's spells.
- **Gain:** only from hits on a boss or a training dummy. Light hit +0.05, heavy hit +0.10, Vine Seeker hit +0.10, charged Vine Seeker hit +0.15. Ranged shots, specials and props give nothing. Measured with the Elder Club: 8 lights (393 dmg) gave 0.40 and 5 heavies (493 dmg) gave 0.50. That is about **1,000 damage per charge** for either button, roughly 30% of a 3,200 HP boss.
- **Limits:** up to 2 charges banked. A boss fight (`BossEncounter.Spawn`) starts at 0 and allows at most `specialsPerBoss` = 2 specials. After that, hits stop filling until the fight ends (win, death or dismiss). Outside fights the bank simply caps at 2.
- **HUD:** two gold slots under the stamina bar (`UI/HUD.Charges.cs`). They flash when a slot fills, shake when a special is refused, and go dark once spent in a boss fight.
- **Tuning:** `PlayerStats` (`chargePerLightHit`, `chargePerHeavyHit`, `chargePerSpellHit`, `chargePerChargedSpellHit`, `maxSpecialCharges`, `specialsPerBoss`). AutoPilot: `charge N` sets the charge, and the log prints `charge=`.
