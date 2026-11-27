using ElderTree.Audio;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ElderTree.UI
{
    /// <summary>
    /// The in-game level select: the main menu's <see cref="LevelSelectView"/> over the game, paused. F2 opens and
    /// closes it from anywhere (also over the pause or F1 menu), the pause menu's Level Select entry opens it, and
    /// B / Esc closes it again.
    /// </summary>
    public partial class HUD
    {
        LevelSelectView levelSelect;

        public bool LevelSelectOpen => levelSelect != null && levelSelect.IsOpen;

        void BuildLevelSelect()
        {
            var catalog = StageCatalog.Instance;
            if (catalog == null) return;
            levelSelect = new LevelSelectView(root, this, catalog, "Close");
            levelSelect.BackRequested += () => SetLevelSelect(false, false);
        }

        public void SetLevelSelect(bool open, bool sound = true)
        {
            if (levelSelect == null || levelSelect.Travelling || open == LevelSelectOpen) return;
            if (open && TitleShowing) return;
            if (open)
            {
                if (DebugMenuOpen) SetDebugMenu(false);
                if (PauseOpen) SetPause(false);
                levelSelect.Root.BringToFront();
                levelSelect.Open(SceneManager.GetActiveScene().name);
                if (sound) AudioManager.Play2D(Sfx.UISelect, 0.7f);
            }
            else
            {
                levelSelect.Close();
                if (sound) AudioManager.Play2D(Sfx.UIBack, 0.7f);
            }
            PauseChanged?.Invoke(open);
        }

        void TickLevelSelect()
        {
            if (levelSelect == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame && !levelSelect.Travelling)
            {
                // co-op: the host takes the party back to the stage vote; nobody travels alone
                if (Coop.InStage) { if (Coop.IsHost) Coop.ReturnToVote(); return; }
                SetLevelSelect(!LevelSelectOpen);
                return;
            }
            levelSelect.Tick(LevelSelectOpen ? input : null);
        }
    }
}
