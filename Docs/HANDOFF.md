# Handoff — Tung Tung Tung Sahur soulslike slice

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
