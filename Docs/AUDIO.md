# Audio

Game audio comes from **Gamemaster Audio – Pro Sound Collection** (`Assets/Gamemaster Audio - Pro Sound Collection`, 6,827 clips). The procedural `SfxForge` generator is gone. The one exception is the **transformation cutscene**: its original synthesized clips were kept because they fit better (`Assets/ElderTree/Audio`: mask fanfare, mask clamp, scream, body swap, and the croc / shark roars, which the cutscene plays as the form's roar). They load through `SoundPicks.Own(...)`.

**Tone rules (from playtest feedback):** everything should sound physical and dark-fantasy. Use wood, stone, metal, leather and fire, and nothing cartoonish, magical-sparkly or "app UI". The `Comedy_Cartoon` and `User_Interface_Menu` folders are out. Menus use door latches and switch clicks, the shark's special uses blade metal, and the alligator's uses stone and explosions.

**No human voices.** The package's `Voice/*` sets and the human-performed `Animal_Impersonations` sounded wrong in game and were removed; don't use them for the player or bosses.

## Where things live

| What | Where |
|---|---|
| Package map (folder constants, `Load`, `Family`, import settings) | `Assets/ElderTree/Scripts/Editor/GamemasterAudio.cs` |
| Cue → clips + mix (volume, pitch range, spatial blend, min interval) | `Assets/ElderTree/Scripts/Editor/SoundPicks.cs` |
| Cue enum + pooled one-shot player + ambience bed | `Assets/ElderTree/Scripts/Runtime/Audio/AudioManager.cs` (`Sfx`) |
| Mixer: Master → Music, Ambience, SFX, UI, Footsteps | `Assets/ElderTree/Audio/ElderTree.mixer` (created once by `Editor/GameMixer.cs`; edit it freely in Window > Audio > Audio Mixer) |
| Wiring into the scene | `ElderTreeBuilder.BuildAudio()` → menu **Elder Tree/5. Build Audio** (also part of `BuildAll()`) |
| Boss loops | `SoundPicks.EngineLoop()` (Crocodile prefab), `SoundPicks.DigLoop()` (Shark prefab) |
| Every package family with variation numbers, length, mono/stereo, loop flag | `Docs/audio_package_families.csv` (1,349 families) |

## Adding a sound

1. Find a family in `Docs/audio_package_families.csv` (or the catalog below). Clip paths are `<folder>/<family>_<NN>` with no extension, e.g. `Punches/kick_hard_impact_04`. Most variation numbers are zero-padded; a few aren't (`Magic_Spells/chimes_magic_bell_ding_1`).
2. One-shot: add a value to `Sfx`, add a `C(Sfx.X, P(...), volume, pitchMin, pitchMax, spatial, minInterval)` line in `SoundPicks.Cues()`, call `AudioManager.Play(Sfx.X, pos)` / `Play2D`.
   Loop on an object: add a `SoundPicks.XLoop()` like `EngineLoop()` and assign it to an `AudioSource` in the builder.
   Quick experiments in editor code: `GamemasterAudio.Family(GamemasterAudio.Folder.Punches, "kick_hard_impact")` loads every variation.
3. Run **Elder Tree/5. Build Audio**, or `BuildAll()`. A wrong path logs `[GamemasterAudio] Missing clip: …`.

**Import settings** are applied only to clips the game uses (`GamemasterAudio.Configure`): force to mono for one-shots and positional loops (the AudioManager pans them), **Normalize off** (otherwise Unity peak-normalizes the mono downmix and breaks the mix), Vorbis 0.7, Decompress On Load under 3 s / Compressed In Memory above, Streaming for the stereo ambience bed. The rest of the package keeps its import defaults, so unused clips cost nothing in a build.

**Loudness:** the package isn't level-matched between categories (wood knocks sit near −27 dB RMS, explosions near −5). Cue volumes were set by measuring each pick's loudest-300 ms RMS against the old mix, so the balance carried over: every cue is within 3 dB of where it was. When swapping a clip, keep its loudness close or retune the cue volume. `AudioSource.volume` caps at 1, so for a quiet family prefer its louder variations.

## Layers and voices

Every one-shot cue belongs to a layer (`AudioLayer`): **SFX**, **UI** (menus, lock-on, mana, and the full-screen stings Title / YOU DIED / GREAT ENEMY FELLED / mask fanfare) or **Footsteps**. The ambience bed and the music are layers too. Each layer has:
- a volume on the AudioManager (`sfxVolume`, `uiVolume`, `footstepVolume`, `ambienceVolume`, `musicVolume`);
- its own group in the mixer;
- a voice priority (music > ambience > UI > SFX > footsteps).

The pause menu has Master, **Music** and **Effects** volume.

One-shots share a pool of 40 voices (the project's real-voice limit was raised from 32 to 64), so sounds stack instead of cutting each other off:
- **Limits:** at most 6 copies of one cue, 4 footsteps and 6 UI sounds ring at once. Hitting a limit re-uses the voice of that group closest to finishing.
- **Free voices first:** otherwise a free voice is taken.
- **When everything is busy:** a voice is stolen from footsteps first, then SFX, then UI, choosing the one with the least left to play. Long stings and explosion tails are the last to go.
- **Debugging:** `AudioManager.Played` fires for every one-shot (debug and tests), and `ActiveVoices` counts the voices sounding.

## Music

`AudioManager` plays `exploreMusic` in the arena and crossfades to `bossMusic` while a boss fight is on. `BossEncounter` calls `AudioManager.SetMusic`: Boss on spawn; None when the last boss falls (the victory sting plays over silence); Explore once things settle or the fight is called off.
- **Looping:** tracks fade in and out, so each loop restarts with a 4 s crossfade before its `loopEnd`.
- **Explore:** `Ω_Bonus_Music_16bit44kOnly/music_misty_woods_calling` (139 s, loops at 133 s, volume 0.4).
- **Boss:** `music_epic_fallen_empire` (99 s, loops at 92 s, volume 0.25).
- **Changing tracks:** do it in `SoundPicks.ExploreMusic()` / `BossMusic()`. Other dark candidates: `music_cinematic_darkness_falls`, `music_journey_to_the_battlefield`, `music_epic_orchestral_bg_underscore`.

## Footsteps

`FX/FootstepFX` (on the Player) fires `Sfx.Footstep` when a foot bone plants. It learns each rig's planted ankle height, because the Elder Tree's ankle sits ~0.11 m above the root and the old fixed 0.105 m threshold almost never fired. A step lands within `plantMargin` (3 cm) of that height and re-arms after the foot lifts `liftMargin` (7 cm). It follows whichever body the controller drives (checked every frame), so mask swaps re-learn it. Volume scales with speed. The cue sits on the Footsteps layer at 0.48 (cut 30% from 0.69). Bosses play their own steps (Shark `FootPos`).

## Current cues

| Cue | Clips (package-relative) | Vol |
|---|---|---|
| Knock (club hit) | Guns_Weapons/Bullets/bullet_impact_wood_heavy_02, 03, 05, 06 | 1.00 |
| ImpactHeavy | Punches/punch_head_weapon_bat_impact_01, 02, 05 | 1.00 |
| Slam | Impacts_Smashable/rock_impact_heavy_slam_01, 04 | 0.63 |
| WhooshLight | Whooshes/whoosh_swish_med_02, 03, 05 | 1.00 |
| WhooshHeavy | Whooshes/whoosh_slow_deep_03, 05, 09 | 0.65 |
| Footstep | Footsteps/footstep_dirt_walk_run_02, 03, 09, 11 | 0.48 |
| Dodge (roll) | Footsteps/footstep_gravel_slide_05, 07, 09 | 1.00 |
| Jump | Foley/foley_jump_movement_throw_01, 08 | 0.68 |
| Land | Footsteps/footstep_dirt_land_v2_01, 05 | 0.75 |
| Guard | Miscellaneous/Wood/wood_block_rattle_movement_01, 03 | 0.99 |
| Stagger | Punches/punch_heavy_huge_distorted_01, 04 | 0.41 |
| ChargeUp (charged heavy) | Foley/foley_leather_stretch_couch_chair_06, 13, 20 (the grip tightening) | 1.00 |
| ChargeRelease | Whooshes/whoosh_slow_deep_04, 06, 07 | 0.55 |
| PlayerHurt | Doors/door_old_wooden_close_thud_01, 03, 05 (a hollow wooden thud: the treant takes a hit) | 0.95 |
| DummyHit | Punches/punch_general_body_impact_01, 04, 06 | 0.69 |
| Felled | Miscellaneous/Object Deep Thud/impact_deep_thud_bounce_05, 06 | 1.00 |
| BowDraw | Guns_Weapons/Bow_Arrow/bow_crossbow_arrow_draw_stretch2_03, stretch1_02 | 1.00 |
| BowRelease | Guns_Weapons/Bow_Arrow/bow_crossbow_arrow_shoot_type1_01, 04 | 0.95 |
| BowNock | Guns_Weapons/Bow_Arrow/bow_crossbow_place_arrow_load_01, 03 | 0.47 |
| ArrowHit | Guns_Weapons/Bullets/bullet_impact_body_thump_01, 03, 06 | 0.71 |
| FullDraw | Magic_Spells/chimes_magic_bell_ding_1, 4 | 0.38 |
| LockOn / LockOff | Metal/metal_small_movement_03, 12 / 11, 15 (small metal clinks) | 0.53 / 0.54 |
| UIMove / UISelect / UIBack | Switches_Buttons_Gears_Levers/switch_button_push_small_02, 03 / Doors/door_lock_open_01, 02 / Doors/door_lock_close_01, 02 | 0.32 / 0.85 / 1.00 |
| ManaEmpty | Doors/door_lock_fail_01, 02, 05 (a locked door rattling) | 0.59 |
| Title | Cinematic Sounds/cinematic_deep_boom_impact_01 | 0.65 |
| YouDied | Collectibles_Items_Powerup/game_over_dark_bell_chime_01 | 1.00 |
| GreatEnemyFelled | Miscellaneous/Bell/bell_large_ringing_01 | 1.00 |
| Explosion | Explosion_Fire_Gas/explosion_large_01, 02, 04 | 0.54 |
| BombPop | Explosion_Fire_Gas/explosion_small_no_tail_01, 02 | 0.30 |
| BombWhistle (falling bombs, thrown hammer) | Guns_Weapons/Bullets/bullet_flyby_deep_01, 02, 03 | 1.00 |
| BombBeep | Alarms_Beeps_Siren/beep_01, 06 | 1.00 |
| CrocRoar | own: sfx_croc_roar_0, 1 | 1.00 |
| JawSnap | Doors/small_box_close_01, 02, 05 (a lid snapping shut) | 0.53 |
| MetalHit | Metal/metal_tin_impacts_hit_hard_01, 02, 05 | 0.81 |
| Skid | Vehicles_Engines_Motors/tyre_skid_01, 03 | 0.59 |
| CannonFire | Guns_Weapons/Guns/weapon_cannon_shot_01, 02 | 0.52 |
| Deflect | Guns_Weapons/Bullets/bullet_impact_rock_01, 04 | 0.68 |
| RockBreak | Impacts_Smashable/rock_smashable_hit_impact_large_01, 02 | 0.72 |
| MaskOn | own: sfx_mask_on | 0.90 |
| MaskScream | own: sfx_mask_scream | 0.95 |
| Transform | own: sfx_transform | 0.80 |
| MaskReceived | own: sfx_mask_received | 0.85 |
| SharkRoar | own: sfx_shark_roar_0, 1 | 1.00 |
| SneakerSqueak | Footsteps/footstep_concrete_slide_03, 04 (rubber sole scuff) | 1.00 |
| Burrow | Impacts_Smashable/rock_blast_impact_projectile_01, 02 | 0.66 |
| BladeWhoosh | Whooshes/whoosh_swish_high_big_01, 03, 04 | 0.68 |
| BladeHit | Guns_Weapons/Knife_Sword_Pick/sword_hit_impact_heavy_02, 04, 05 | 0.59 |
| VineCut | Magic_Spells/nature_spell_bush_tree_whip_01, 03 | 0.49 |
| VineRustle | Magic_Spells/nature_spell_vines_tree_impact_01, 02, 03 | 0.31 |
| CrossbowFire | Guns_Weapons/Bow_Arrow/bow_crossbow_arrow_shoot_type2_01, 03 | 0.91 |
| CrossbowReload | Switches_Buttons_Gears_Levers/metal_small_gear_lever_winding_03, 06 | 0.92 |
| SpinWhirl | Whooshes/whoosh_weapon_spin_01, 02 | 0.81 |
| BladeRing (shark: spirit spend / level-up, Tornado wind-up) | Guns_Weapons/Knife_Sword_Pick/knife_unsheathe_01, 02, 03 | 1.00 |
| GroundGrind (alligator: Sky Bombardment crouch) | Doors/rock_door_slide_block_move_drag_02, 04, 07 | 0.68 |
| Ambience bed (2D) | Animals_Nature_Ambiences/wind_general_soft_low_loop_02 (27.4 s; crickets were distracting) | 0.30 (`SoundPicks.AmbienceVolume`) |
| Crocodile engine | Vehicles_Engines_Motors/truck_low_engine_rattle_loop (10.2 s) | runtime |
| Shark dig | Doors/rock_door_slide_block_move_drag_loop1 (4.0 s) | runtime |

Picks were chosen by name, length and measured loudness, not by ear. Candidates to audition first if something sounds off: Knock (a wood "bullet impact" thunk; the pure wood-block knocks in `Miscellaneous/Wood/wood_block_sticks_hit_clap_*` have the right hollow-knock timbre but are ~8 dB quieter), PlayerHurt, JawSnap, LockOn, Title.

## Package catalog

Counts are clip files. **Bold** families are the ones most likely to matter for a dark-fantasy soulslike.

| Folder | n | What's there |
|---|---|---|
| `Alarms_Beeps_Siren` | 36 | beep 01–17, siren loops, timer/warning beeps |
| `Animal_Impersonations` | 1085 | ⚠ human-performed, don't use (sounded wrong in game). Human-voiced animals: **bear_roar / bear_roar_soft / bear_pain_hurt**, **gorilla_angry / growl_deep / hurt**, **wolf_growl / howl / hurt**, hippo_groan, **snake_2_attack / hiss**, lizard_taunt_hiss, crow_raven_*, seal_walrus_*, plus farm animals and birds |
| `Animals_Nature_Ambiences` | 187 | loops 4–38 s: **wind_cold_howling_haunted(_night)**, **cave_ambience_loop_01–04**, wind_general_*, crickets / cicadas night, **swamp_night_***, rain light/medium/heavy, river streams, fantasy_jungle_forrest; one-shots: **thunder_lightning_strike_rumble**, crows, snake hiss |
| `Backgrounds` | 34 | room tone, fans, machinery, crowd chatter loops |
| `Cinematic Sounds` | 17 | **cinematic_deep_boom_impact**, **deep_low_whoosh_impact** (2–7 s), deep_bass_pass_whoosh, deep_bass_rumble, buildup_reverse_whoosh, LowDrone1 |
| `Collectibles_Items_Powerup` | 148 | **game_over_dark_bell_chime**, jingle_chime_01–15 positive / 16–25 negative, collect_item*, coins, points tickers |
| `Comedy_Cartoon` | 162 | ⚠ cartoonish, don't use. slide whistles, boings, bites/chews, squeaks, burps, **battle_viking_horn_call_close/far** |
| `Doors` | 132 | old wooden doors / creaks / thuds, metal hatches, locks, **rock_door_slide_block_move_drag (+ loops)**, metal_gate_fence_impact |
| `Electricity_Hums` | 36 | neon hums, radio static |
| `Explosion_Fire_Gas` | 86 | explosion_large (+no_tail), small, far_distant, deep_low; **fire_burning_flames_crackle_loop**, fuse loop, flint sparks, gas ignite |
| `Foley` | 198 | cloth movement, grab/throw, combat grabs, **bone_break_neck_snap_crack**, leather, gear rattles, zippers |
| `Footsteps` | 581 | walk / run / land / slide for **dirt, gravel, grass, mud, sand, snow, ice, wood, metal, concrete, water**; `_land_v2` sets are ~8 dB louder than `_land` |
| `Guns_Weapons/Bow_Arrow` | 51 | draw_slide / draw_stretch, shoot type1–3, place_arrow_load |
| `Guns_Weapons/Bullets` | 263 | flybys; **impacts per surface** (body_flesh, body_thump, wood light/heavy, rock, dirt, mud, metal, ice, snow, water, glass) — good for any projectile or blunt hit |
| `Guns_Weapons/Guns` (+ `Guns_Silenced`) | 398 | firearms, **weapon_cannon_shot**, gun_tank_cannon_turret_shot, grenade launcher |
| `Guns_Weapons/Knife_Sword_Pick` | 44 | **sword_hit_impact(_heavy/_ringing)**, sword_impact_body, **unsheathe_sword_with_ringout**, knife_unsheathe, knife_sharpen, pick_axe_stone hit |
| `Impacts_Smashable` | 39 | **rock_impact_heavy_slam**, rock_earthquake_impact, rock_avalanche_landslide_debris, rock_smashable (hit/large/falling), rock_impact_spike_trap, glass breaks |
| `Magic_Spells` | 395 | charges (casting_charge_*, magic_conjure_charge), **dark_magic_conjure_blast**, **dark_portal_wind**, **dark_wind_growls**, fireball conjure/projectile/impact, ice, lightning, water, **nature_spell_vines_* / bush_tree_whip**, **healing_magic_spell**, potion_flask_mana_collect, time_warp_reverse_*, chimes, spell book pages |
| `Metal` | 179 | tin / sheet / drum impacts, creaks and ship-structure groans, scrapes, robot steps |
| `Miscellaneous/*` | 347 | **Bell** (large/med/small ringing), **Object Deep Thud**, **Wood** (block knocks, **wood_tree_branch_break / move**), Gravel Dirt Stones, Clock, Cooking sizzle, Broom, Typing, Phone… |
| `Punches` | 68 | **kick_hard / kick_heavy**, **punch_low_deep**, punch_general_body, punch_grit_wet, **punch_blocked**, punch_head_weapon_bat, punch_heavy_huge_distorted |
| `Snow_Ice` | 51 | ice cracking, snow digging (+ loop), snowball hits |
| `Switches_Buttons_Gears_Levers` | 107 | switches, **metal_small_gear_lever_winding** (ratchet), lever cranks, gear drills |
| `User_Interface_Menu` | 152 | ⚠ bright app-style UI, don't use. ui_menu_button_click / beep / scroll / scroll_back / confirm / cancel / error, popups, text-type effects, ui_stamp |
| `Vehicles_Engines_Motors` | 49 | engine / generator loops, tyre_skid, horns, trains, helicopter loop |
| `Voice/*` | 1680 | ⚠ human voices, don't use. **TrollMonster** (194: attack fast/slow, battle groan/grunt/shout, growl(_long), hurt, death, laugh, taunt) — the go-to for big creatures; **GoblinFairy** (214, small creature set); Human Male A–D / Female A–C (attacks, efforts, hurt, death, breathing); **Ghost** (witch hiss), **Monster** (distant roar, orc grunt), Fun Characters / Creatures (cartoon), FPS announcer, radio soldier lines |
| `Water` | 73 | drips, bubbling loops, splashes, underwater |
| `Whooshes` | 81 | whoosh_swish small / med / high_big / high_fast, **whoosh_slow_deep**, whoosh_low_deep(_soft), **whoosh_weapon_knife_swing / spin**, fan_spin, object pass-bys |
| `Zombie` | 95 | groans, grunts, hiss, attack grunts, eating |
| `Ω_Bonus_Music_16bit44kOnly` | 33 | **music_cinematic_darkness_falls** (48 s), **music_epic_fallen_empire** (99 s), **music_journey_to_the_battlefield** (89 s), **music_misty_woods_calling** (139 s), music_epic_orchestral_bg_underscore (121 s), music_epic_heroes_story (188 s), music_cinematic_reveal (15 s sting), music_magical_story_intro (27 s), music_calm_tree_of_life (144 s), plus comedy / kids / 8-bit tracks |

### Shortlist for upcoming work

- **Creature sounds without voices:** Magic_Spells/dark_wind_growls and dark_portal_wind for menace, Cinematic deep_low_whoosh_impact for big tells, Impacts / Metal / Doors for body sounds. Design roars the way the cutscene ones were (own clips) rather than using the package's human-performed voices.
- **Boss music / stings:** music_cinematic_darkness_falls or music_epic_fallen_empire for a fight, music_cinematic_reveal for an intro sting, music_misty_woods_calling for the hub. No music is wired yet.
- **Surfaces:** Footsteps has walk/run/land per surface, Bullets has impacts per surface: enough for surface-aware footsteps and hit sparks.
- **Arena dressing:** fire_burning_flames_crackle_loop (braziers), wind_cold_howling_haunted_night_loop (a harsher bed), thunder_lightning_strike_rumble (weather), cave_ambience_loop (interiors), bell_large_ringing / bell_med_ringing (checkpoints).
- **Player actions:** potion_heal_flask_spell / healing_magic_spell (heal), unsheathe_sword_with_ringout (weapon swap), punch_blocked (perfect guard), Foley cloth for idle movement.
