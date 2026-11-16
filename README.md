# Tung Tung Tung Sahur — a night-watch soulslike

A third-person soulslike vertical slice in Unity 6.3 (URP). You play Tung Tung Tung Sahur, a living log with
a baseball-bat club, in a moonlit training yard beside a *Pos Ronda* (night-watch hut). Train on straw
dummies: chain light attacks, charge heavy smashes, break their stance and land criticals, lock on and strafe,
roll through, and slam the ground.

**Open `Assets/TungSahur/Scenes/Arena.unity` and press Play.**

## Controls (Elden Ring layout)

| Action | Gamepad | Keyboard & Mouse |
|---|---|---|
| Move / camera | Left stick / right stick | WASD / mouse |
| Light attack (4-hit chain) | RB | LMB |
| Heavy attack (hold to charge) | RT | Shift + LMB |
| Skill: *Tung Tung Slam* (FP) | LT | Shift + RMB |
| Guard | LB | RMB |
| Roll (with direction) / backstep (no direction) · hold = sprint | B | Space |
| Jump (attack in the air = plunging slam) | A | F |
| Lock on / reset camera | R3 | Q or middle mouse |
| Switch target | Flick right stick | Flick mouse / mouse wheel |
| Walk toggle | L3 | Ctrl |
| Menu (settings, controls, reset) | Start | Esc |
| Show / hide control hints | View | Tab |

Context attacks: **running attack** (light while sprinting), **roll attack** (light right after a roll),
**jump attack** (light/heavy in the air), **critical** (any hit on a stance-broken dummy).

## What's in here

```
Assets/TungSahur/
  Art/Models/            TungSahur.fbx (rigged), TungSahur_Club.fbx, Props/*.fbx  — all made in Blender by script
  Art/Textures|Materials Procedural textures (wood, eye, club, ground, planks, stone, burlap, FX)
  Art/Shaders/           NightSky.shader (procedural moon/stars/cloud skybox)
  Animation/             TungSahur.controller (built by script), copied loop clips, upper-body mask
  Audio/                 Every sound is synthesized by SfxForge (wood knocks, whooshes, slams, ambience…)
  Data/TungMoveset.asset All attack tuning (timings, damage, poise, lunge, hit-stop, shake) — edit here
  Input/                 TungControls.inputactions (Gameplay + UI maps, gamepad + KBM schemes)
  Prefabs/               Player, TrainingDummy, FX/*
  Scenes/Arena.unity
  Scripts/Runtime/       Player, Combat, CameraSystem, Enemies, FX, Audio, UI, Core, Dev
  Scripts/Editor/        TungSahurBuilder (one-click rebuild), TextureForge, SfxForge, PreviewRenderer
  UI/                    TungHUD.uss, panel settings, club icon
Tools/Blender/           build_tung_sahur.py (character + rig + club), build_props.py, export/*.blend
Docs/HANDOFF.md          Design notes, Elden Ring research, open questions and suggestions
```

### Rebuild pipeline
Everything generated is reproducible:

1. `blender -b --factory-startup --python Tools/Blender/build_tung_sahur.py -- <outdir> [preview.png]`
   (same for `build_props.py`), then copy the FBX files into `Assets/TungSahur/Art/Models[/Props]`.
2. In Unity: **Tung Sahur ▸ Build EVERYTHING** (or the numbered steps individually).

The scripts also run unchanged through a Blender MCP `execute_code` call; set `OUT_DIR` at the top.

### Architecture in one breath
`PlayerInputReader` (buffered, tap-vs-hold, device aware) → `PlayerController` (state machine:
locomotion/air/landing/attack/dodge; partial files for combat and dodge) → `PlayerMotor` (CharacterController).
Attacks are data (`Moveset`/`AttackData`): per-phase playback speed sells the club's weight, code-driven lunges
replace root motion, a swept capsule (`WeaponHitbox`) finds hurtboxes, and feedback is routed through
`TimeDirector` (hit-stop/slow-mo, the only writer of `Time.timeScale`), `CameraDirector` (Cinemachine 3
orbit + lock-on framing + impulse), `FXManager` (pooled particles) and `AudioManager` (pooled voices).
`HUD` is UI Toolkit, built in code and styled by `TungHUD.uss`.
