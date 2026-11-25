using System.Linq;
using ElderTree.Audio;
using UnityEngine;
using static ElderTree.EditorTools.GamemasterAudio;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// Which Gamemaster Audio clips play for each <see cref="Sfx"/> cue, and how loud. This is the single place the
    /// game's sound design lives: change a pick here and run "Elder Tree/5. Build Audio" (or BuildAll) to rewire.
    ///
    /// Volumes were matched by loudness (loudest-300 ms RMS) to the old procedural mix, so the balance between cues
    /// carries over. When swapping a clip, keep its loudness in the same ballpark or retune the volume.
    /// Paths are relative to the package root (see <see cref="GamemasterAudio.Folder"/> and Docs/AUDIO.md).
    /// </summary>
    public static class SoundPicks
    {
        static AudioManager.Cue C(Sfx id, AudioClip[] clips, float vol, float pmin = 0.94f, float pmax = 1.06f, float spatial = 0.65f, float interval = 0.03f) =>
            new() { id = id, clips = clips, volume = vol, pitchMin = pmin, pitchMax = pmax, spatial = spatial, minInterval = interval };

        /// <summary>Package clips (paths relative to the Gamemaster Audio root).</summary>
        static AudioClip[] P(params string[] rels) => Load(rels);

        /// <summary>
        /// The game's own clips in <see cref="OwnDir"/>: the original synthesized transformation-cutscene sounds, kept by
        /// request because they fit better than anything in the package (mask fanfare, mask clamp, scream, body swap and
        /// the form roars, which the cutscene also plays). Import settings are left as they are.
        /// </summary>
        static AudioClip[] Own(params string[] names) => names.Select(n =>
        {
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>($"{OwnDir}/{n}.wav");
            if (clip == null) Debug.LogError($"[SoundPicks] Missing clip: {OwnDir}/{n}.wav");
            return clip;
        }).Where(c => c != null).ToArray();

        public const string OwnDir = "Assets/ElderTree/Audio";

        /// <summary>Menu feedback and full-screen stings: mixed on the UI layer so they never lose a voice to combat.</summary>
        static readonly System.Collections.Generic.HashSet<Sfx> UiCues = new()
        {
            Sfx.UIMove, Sfx.UISelect, Sfx.UIBack, Sfx.ManaEmpty, Sfx.LockOn, Sfx.LockOff,
            Sfx.Title, Sfx.YouDied, Sfx.GreatEnemyFelled, Sfx.MaskReceived,
        };

        public static AudioManager.Cue[] Cues()
        {
            var cues = new[]
            {
                // ---------------------------------------------------------------- player: club, movement, guard
                C(Sfx.Knock, P("Guns_Weapons/Bullets/bullet_impact_wood_heavy_02", "Guns_Weapons/Bullets/bullet_impact_wood_heavy_03",
                               "Guns_Weapons/Bullets/bullet_impact_wood_heavy_05", "Guns_Weapons/Bullets/bullet_impact_wood_heavy_06"), 1f, 0.92f, 1.08f),
                C(Sfx.ImpactHeavy, P("Punches/punch_head_weapon_bat_impact_01", "Punches/punch_head_weapon_bat_impact_02", "Punches/punch_head_weapon_bat_impact_05"), 1f),
                C(Sfx.Slam, P("Impacts_Smashable/rock_impact_heavy_slam_01", "Impacts_Smashable/rock_impact_heavy_slam_04"), 0.63f, 0.95f, 1.03f, 0.4f),
                C(Sfx.WhooshLight, P("Whooshes/whoosh_swish_med_02", "Whooshes/whoosh_swish_med_03", "Whooshes/whoosh_swish_med_05"), 1f, 0.9f, 1.12f),
                C(Sfx.WhooshHeavy, P("Whooshes/whoosh_slow_deep_03", "Whooshes/whoosh_slow_deep_05", "Whooshes/whoosh_slow_deep_09"), 0.65f, 0.92f, 1.05f),
                C(Sfx.Footstep, P("Footsteps/footstep_dirt_walk_run_02", "Footsteps/footstep_dirt_walk_run_03",
                                  "Footsteps/footstep_dirt_walk_run_09", "Footsteps/footstep_dirt_walk_run_11"), 0.48f, 0.85f, 1.15f, 0.7f, 0.06f),
                C(Sfx.Dodge, P("Footsteps/footstep_gravel_slide_05", "Footsteps/footstep_gravel_slide_07", "Footsteps/footstep_gravel_slide_09"), 1f),
                C(Sfx.Jump, P("Foley/foley_jump_movement_throw_01", "Foley/foley_jump_movement_throw_08"), 0.68f),
                C(Sfx.Land, P("Footsteps/footstep_dirt_land_v2_01", "Footsteps/footstep_dirt_land_v2_05"), 0.75f),
                C(Sfx.Guard, P("Miscellaneous/Wood/wood_block_rattle_movement_01", "Miscellaneous/Wood/wood_block_rattle_movement_03"), 0.99f),
                C(Sfx.Stagger, P("Punches/punch_heavy_huge_distorted_01", "Punches/punch_heavy_huge_distorted_04"), 0.41f),
                C(Sfx.ChargeUp, P("Foley/foley_leather_stretch_couch_chair_06", "Foley/foley_leather_stretch_couch_chair_13", "Foley/foley_leather_stretch_couch_chair_20"), 1f, 1f, 1f),   // grip tightening
                C(Sfx.ChargeRelease, P("Whooshes/whoosh_slow_deep_04", "Whooshes/whoosh_slow_deep_06", "Whooshes/whoosh_slow_deep_07"), 0.55f),
                C(Sfx.PlayerHurt, P("Doors/door_old_wooden_close_thud_01", "Doors/door_old_wooden_close_thud_03", "Doors/door_old_wooden_close_thud_05"), 0.95f, 0.95f, 1.05f, 0.2f, 0.08f),
                C(Sfx.DummyHit, P("Punches/punch_general_body_impact_01", "Punches/punch_general_body_impact_04", "Punches/punch_general_body_impact_06"), 0.69f, 0.9f, 1.1f),
                C(Sfx.Felled, P("Miscellaneous/Object Deep Thud/impact_deep_thud_bounce_05", "Miscellaneous/Object Deep Thud/impact_deep_thud_bounce_06"), 1f, 1f, 1f, 0f, 1f),

                // ---------------------------------------------------------------- bow
                C(Sfx.BowDraw, P("Guns_Weapons/Bow_Arrow/bow_crossbow_arrow_draw_stretch2_03", "Guns_Weapons/Bow_Arrow/bow_crossbow_arrow_draw_stretch1_02"), 1f, 0.95f, 1.05f, 0.5f, 0.1f),
                C(Sfx.BowRelease, P("Guns_Weapons/Bow_Arrow/bow_crossbow_arrow_shoot_type1_01", "Guns_Weapons/Bow_Arrow/bow_crossbow_arrow_shoot_type1_04"), 0.95f, 0.95f, 1.05f, 0.45f),
                C(Sfx.BowNock, P("Guns_Weapons/Bow_Arrow/bow_crossbow_place_arrow_load_01", "Guns_Weapons/Bow_Arrow/bow_crossbow_place_arrow_load_03"), 0.47f, 0.95f, 1.08f, 0.5f),
                C(Sfx.ArrowHit, P("Guns_Weapons/Bullets/bullet_impact_body_thump_01", "Guns_Weapons/Bullets/bullet_impact_body_thump_03",
                                  "Guns_Weapons/Bullets/bullet_impact_body_thump_06"), 0.71f, 0.92f, 1.08f, 0.8f),
                C(Sfx.FullDraw, P("Magic_Spells/chimes_magic_bell_ding_1", "Magic_Spells/chimes_magic_bell_ding_4"), 0.38f, 1f, 1f, 0f, 0.2f),

                // ---------------------------------------------------------------- UI, lock-on, flow stings
                C(Sfx.LockOn, P("Metal/metal_small_movement_03", "Metal/metal_small_movement_12"), 0.53f, 1f, 1f, 0f),
                C(Sfx.LockOff, P("Metal/metal_small_movement_11", "Metal/metal_small_movement_15"), 0.54f, 1f, 1f, 0f),
                C(Sfx.UIMove, P("Switches_Buttons_Gears_Levers/switch_button_push_small_02", "Switches_Buttons_Gears_Levers/switch_button_push_small_03"), 0.32f, 0.96f, 1.02f, 0f),
                C(Sfx.UISelect, P("Doors/door_lock_open_01", "Doors/door_lock_open_02"), 0.85f, 0.96f, 1f, 0f),
                C(Sfx.UIBack, P("Doors/door_lock_close_01", "Doors/door_lock_close_02"), 1f, 0.96f, 1f, 0f),
                C(Sfx.ManaEmpty, P("Doors/door_lock_fail_01", "Doors/door_lock_fail_02", "Doors/door_lock_fail_05"), 0.59f, 0.97f, 1.03f, 0f, 0.12f),
                C(Sfx.Title, P("Cinematic Sounds/cinematic_deep_boom_impact_01"), 0.65f, 1f, 1f, 0f, 1f),
                C(Sfx.YouDied, P("Collectibles_Items_Powerup/game_over_dark_bell_chime_01"), 1f, 1f, 1f, 0f, 1f),
                C(Sfx.GreatEnemyFelled, P("Miscellaneous/Bell/bell_large_ringing_01"), 1f, 1f, 1f, 0f, 1f),

                // ---------------------------------------------------------------- Crocodile: bombs, cannon, croc
                C(Sfx.Explosion, P("Explosion_Fire_Gas/explosion_large_01", "Explosion_Fire_Gas/explosion_large_02", "Explosion_Fire_Gas/explosion_large_04"), 0.54f, 0.9f, 1.08f, 0.55f, 0.04f),
                C(Sfx.BombPop, P("Explosion_Fire_Gas/explosion_small_no_tail_01", "Explosion_Fire_Gas/explosion_small_no_tail_02"), 0.3f, 0.92f, 1.08f, 0.7f),
                C(Sfx.BombWhistle, P("Guns_Weapons/Bullets/bullet_flyby_deep_01", "Guns_Weapons/Bullets/bullet_flyby_deep_02", "Guns_Weapons/Bullets/bullet_flyby_deep_03"), 1f, 0.95f, 1.08f, 0.6f, 0.12f),
                C(Sfx.BombBeep, P("Alarms_Beeps_Siren/beep_01", "Alarms_Beeps_Siren/beep_06"), 1f, 1f, 1f, 0.6f, 0.05f),
                C(Sfx.CrocRoar, Own("sfx_croc_roar_0", "sfx_croc_roar_1"), 1f, 0.95f, 1.05f, 0.35f, 0.3f),
                C(Sfx.JawSnap, P("Doors/small_box_close_01", "Doors/small_box_close_02", "Doors/small_box_close_05"), 0.53f, 0.95f, 1.05f, 0.6f),
                C(Sfx.MetalHit, P("Metal/metal_tin_impacts_hit_hard_01", "Metal/metal_tin_impacts_hit_hard_02", "Metal/metal_tin_impacts_hit_hard_05"), 0.81f, 0.92f, 1.08f, 0.6f),
                C(Sfx.Skid, P("Vehicles_Engines_Motors/tyre_skid_01", "Vehicles_Engines_Motors/tyre_skid_03"), 0.59f, 0.95f, 1.05f, 0.6f, 0.2f),
                C(Sfx.CannonFire, P("Guns_Weapons/Guns/weapon_cannon_shot_01", "Guns_Weapons/Guns/weapon_cannon_shot_02"), 0.52f, 0.95f, 1.05f, 0.3f, 0.1f),

                // ---------------------------------------------------------------- Alligator Mask set
                C(Sfx.Deflect, P("Guns_Weapons/Bullets/bullet_impact_rock_01", "Guns_Weapons/Bullets/bullet_impact_rock_04"), 0.68f, 0.93f, 1.07f, 0.6f, 0.08f),
                C(Sfx.RockBreak, P("Impacts_Smashable/rock_smashable_hit_impact_large_01", "Impacts_Smashable/rock_smashable_hit_impact_large_02"), 0.72f, 0.93f, 1.05f, 0.6f, 0.1f),
                C(Sfx.MaskOn, Own("sfx_mask_on"), 0.9f, 0.97f, 1.03f, 0.2f, 0.2f),
                C(Sfx.MaskScream, Own("sfx_mask_scream"), 0.95f, 1f, 1f, 0f, 0.5f),
                C(Sfx.Transform, Own("sfx_transform"), 0.8f, 0.95f, 1.05f, 0.3f, 0.06f),
                C(Sfx.MaskReceived, Own("sfx_mask_received"), 0.85f, 1f, 1f, 0f, 1f),

                // ---------------------------------------------------------------- Shark
                C(Sfx.SharkRoar, Own("sfx_shark_roar_0", "sfx_shark_roar_1"), 1f, 0.96f, 1.04f, 0.4f, 0.3f),
                C(Sfx.SneakerSqueak, P("Footsteps/footstep_concrete_slide_03", "Footsteps/footstep_concrete_slide_04"), 1f, 0.9f, 1.12f, 0.7f, 0.08f),   // rubber sole scuff
                C(Sfx.Burrow, P("Impacts_Smashable/rock_blast_impact_projectile_01", "Impacts_Smashable/rock_blast_impact_projectile_02"), 0.66f, 0.94f, 1.06f, 0.55f, 0.1f),

                // ---------------------------------------------------------------- Shark Mask set
                C(Sfx.BladeWhoosh, P("Whooshes/whoosh_swish_high_big_01", "Whooshes/whoosh_swish_high_big_03", "Whooshes/whoosh_swish_high_big_04"), 0.68f, 0.92f, 1.08f),
                C(Sfx.BladeHit, P("Guns_Weapons/Knife_Sword_Pick/sword_hit_impact_heavy_02", "Guns_Weapons/Knife_Sword_Pick/sword_hit_impact_heavy_04",
                                  "Guns_Weapons/Knife_Sword_Pick/sword_hit_impact_heavy_05"), 0.59f, 0.92f, 1.08f),
                C(Sfx.VineCut, P("Magic_Spells/nature_spell_bush_tree_whip_01", "Magic_Spells/nature_spell_bush_tree_whip_03"), 0.49f, 0.9f, 1.1f, 0.65f, 0.04f),
                C(Sfx.VineRustle, P("Magic_Spells/nature_spell_vines_tree_impact_01", "Magic_Spells/nature_spell_vines_tree_impact_02",
                                    "Magic_Spells/nature_spell_vines_tree_impact_03"), 0.31f, 0.9f, 1.1f, 0.65f, 0.1f),
                C(Sfx.CrossbowFire, P("Guns_Weapons/Bow_Arrow/bow_crossbow_arrow_shoot_type2_01", "Guns_Weapons/Bow_Arrow/bow_crossbow_arrow_shoot_type2_03"), 0.91f, 0.95f, 1.05f),
                C(Sfx.CrossbowReload, P("Switches_Buttons_Gears_Levers/metal_small_gear_lever_winding_03", "Switches_Buttons_Gears_Levers/metal_small_gear_lever_winding_06"), 0.92f, 0.97f, 1.03f),
                C(Sfx.SpinWhirl, P("Whooshes/whoosh_weapon_spin_01", "Whooshes/whoosh_weapon_spin_02"), 0.81f, 0.9f, 1.12f, 0.6f, 0.05f),
                C(Sfx.BladeRing, P("Guns_Weapons/Knife_Sword_Pick/knife_unsheathe_01", "Guns_Weapons/Knife_Sword_Pick/knife_unsheathe_02", "Guns_Weapons/Knife_Sword_Pick/knife_unsheathe_03"), 1f, 0.95f, 1.05f, 0.5f, 0.1f),   // spirit drawn into the blade

                // ---------------------------------------------------------------- Alligator Mask special (Sky Bombardment)
                C(Sfx.GroundGrind, P("Doors/rock_door_slide_block_move_drag_02", "Doors/rock_door_slide_block_move_drag_04", "Doors/rock_door_slide_block_move_drag_07"), 0.68f, 0.95f, 1.05f, 0.5f, 0.2f),   // crouch: stone grinding underfoot

                // ---------------------------------------------------------------- Druid (a giant tree: no voice, only wood and earth)
                C(Sfx.WoodGroan, P("Doors/door_creak_long_01", "Doors/door_creak_long_02", "Doors/door_creak_long_03", "Doors/door_creak_long_04"), 0.8f, 0.5f, 0.6f, 0.45f, 0.25f),   // a trunk bending: its roar and its strain
                C(Sfx.RootSnap, P("Foley/bone_break_neck_snap_crack_01", "Foley/bone_break_neck_snap_crack_02", "Foley/bone_break_neck_snap_crack_03"), 0.7f, 0.62f, 0.78f, 0.6f, 0.06f),   // roots splintering under blows
                C(Sfx.DeepRumble, P("Cinematic Sounds/cinematic_deep_bass_rumble_01", "Cinematic Sounds/cinematic_deep_bass_rumble_02"), 0.7f, 0.9f, 1.05f, 0.3f, 0.5f),   // the ground taking its weight

                // ---------------------------------------------------------------- Druid Mask (caster). Not heard by ear yet:
                // volumes are guesses. Vines are the package's nature spells (whips, wood and leaves, nothing sparkly); the
                // orb is the one place an energy sound belongs (the user asked for a "green energy ball").
                C(Sfx.SpellGather, P("Magic_Spells/casting_charge_matter_grow_01", "Magic_Spells/casting_charge_matter_grow_02",
                                     "Magic_Spells/casting_charge_matter_grow_03"), 0.5f, 0.9f, 1.05f, 0.6f, 0.15f),
                C(Sfx.VineCast, P("Magic_Spells/nature_spell_vines_whoosh_01", "Magic_Spells/nature_spell_vines_whoosh_02",
                                  "Magic_Spells/nature_spell_vines_whoosh_03"), 0.75f, 0.92f, 1.08f, 0.6f, 0.05f),
                C(Sfx.VineBurst, P("Magic_Spells/nature_spell_vines_blast_impact1", "Magic_Spells/nature_spell_vines_blast_impact2",
                                   "Magic_Spells/nature_spell_vines_blast_impact3"), 0.8f, 0.92f, 1.06f, 0.65f, 0.05f),
                C(Sfx.OrbCharge, P("Magic_Spells/magic_conjure_charge1_01", "Magic_Spells/magic_conjure_charge1_02",
                                   "Magic_Spells/magic_conjure_charge1_03"), 0.4f, 0.85f, 0.95f, 0.6f, 0.2f),
                C(Sfx.OrbFire, P("Magic_Spells/energy_blast_small_01", "Magic_Spells/energy_blast_small_02",
                                 "Magic_Spells/energy_blast_small_03"), 0.6f, 0.8f, 0.92f, 0.6f, 0.08f),
                C(Sfx.OrbImpact, P("Magic_Spells/energy_blast_large_01", "Magic_Spells/energy_blast_large_02",
                                   "Magic_Spells/energy_blast_large_03"), 0.7f, 0.82f, 0.95f, 0.65f, 0.08f),

                // ---------------------------------------------------------------- health potion (the gulp). Not heard by ear yet.
                C(Sfx.PotionDrink, P("Magic_Spells/potion_heal_flask_spell_01", "Magic_Spells/potion_heal_flask_spell_02"), 0.6f, 0.9f, 1f, 0.3f, 0.2f),
                // falling through a hole in the dock into the sea (Bleached Atoll)
                C(Sfx.WaterSplash, P("Water/water_splash_object_body_01", "Water/water_splash_object_body_02", "Water/water_splash_object_body_03",
                    "Water/water_splash_object_body_04", "Water/water_splash_object_body_05"), 0.85f, 0.88f, 1.02f, 0.5f, 0.2f),
            };
            foreach (var c in cues) c.layer = c.id == Sfx.Footstep ? AudioLayer.Footsteps : UiCues.Contains(c.id) ? AudioLayer.UI : AudioLayer.SFX;
            Configure(cues.SelectMany(c => c.clips).Where(c => UnityEditor.AssetDatabase.GetAssetPath(c).StartsWith(Root)), Use.OneShot);
            return cues;
        }

        // ---------------------------------------------------------------------- loops (AudioSources outside the AudioManager)

        /// <summary>Arena bed: a soft, low wind (no crickets: they drew attention). Played 2D by the AudioManager.</summary>
        public static AudioClip Ambience() => Loop("Animals_Nature_Ambiences/wind_general_soft_low_loop_02", Use.Ambience);

        /// <summary>The Shark arena's bed: a calm surf on the beach instead of the wind (kept as quiet as the wind).</summary>
        public static AudioClip BeachAmbience() => Loop("Animals_Nature_Ambiences/beach_waves_ocean_calm_loop_02", Use.Ambience);

        /// <summary>The Druid arena's bed: a tropical forest morning (distant birds, insects), kept as quiet as the wind. Not heard by ear yet.</summary>
        public static AudioClip ForestAmbience() => Loop("Animals_Nature_Ambiences/birds_tropical_forrest_ambience_loop", Use.Ambience);

        /// <summary>The Fairy arena's bed: a cave's hollow air and slow drips, kept as quiet as the wind. Not heard by ear yet.</summary>
        public static AudioClip CaveAmbience() => Loop("Animals_Nature_Ambiences/cave_ambience_loop_02", Use.Ambience);

        /// <summary>Ambience bed level (AudioManager.ambienceVolume). Kept low so it sits under the fight.</summary>
        public const float AmbienceVolume = 0.3f;

        /// <summary>Crocodile's engines (volume driven by prop spin in CrocodileBoss).</summary>
        public static AudioClip EngineLoop() => Loop("Vehicles_Engines_Motors/truck_low_engine_rattle_loop", Use.Loop3D);

        /// <summary>The Shark's fin churning through the earth (volume driven by burrow speed in SharkBoss).</summary>
        public static AudioClip DigLoop() => Loop("Doors/rock_door_slide_block_move_drag_loop1", Use.Loop3D);

        // ---------------------------------------------------------------------- music (AudioManager crossfades and loops them)

        /// <summary>Arena music: slow, misty and dark. The track fades out at its end, so the loop restarts at 133 s.</summary>
        public static AudioManager.MusicTrack ExploreMusic() =>
            Music("Ω_Bonus_Music_16bit44kOnly/music_misty_woods_calling", 0.4f, 0f, 133f);

        /// <summary>Boss fights: heavier, driving. Loops before its fade-out tail at 92 s.</summary>
        public static AudioManager.MusicTrack BossMusic() =>
            Music("Ω_Bonus_Music_16bit44kOnly/music_epic_fallen_empire", 0.25f, 1f, 92f);

        static AudioManager.MusicTrack Music(string rel, float volume, float loopStart, float loopEnd) =>
            new() { clip = Loop(rel, Use.Ambience), volume = volume, loopStart = loopStart, loopEnd = loopEnd };

        static AudioClip Loop(string rel, Use use)
        {
            var clip = Load(rel);
            Configure(new[] { clip }, use);
            return clip;
        }
    }
}
