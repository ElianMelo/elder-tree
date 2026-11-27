using System;
using ElderTree.InputHandling;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    /// <summary>
    /// The main menu's CO-OP page as the main menu sees it: the network layer (ElderTree.Net) builds the real thing
    /// (lobby browser, party room, stage vote) and registers a factory here, so the menu never depends on networking.
    /// </summary>
    public interface ICoopMenu
    {
        bool IsOpen { get; }
        /// <summary>Show the page where the session stands: the browser, the party room, or the vote.</summary>
        void Open();
        void Close();
        /// <summary>Every frame (unscaled). <paramref name="input"/> is null while the page doesn't own the input.</summary>
        void Tick(PlayerInputReader input);
        /// <summary>Back out of the page (from the browser; inside a party, back means leave and asks first).</summary>
        event Action BackRequested;
        /// <summary>Line for the menu footer's help slot.</summary>
        string Help { get; }
        /// <summary>Button prompts for the menu footer.</summary>
        string Keys(bool pad);
    }

    public static class CoopMenuHook
    {
        /// <summary>Set by the network layer at load: builds the page under the menu root.</summary>
        public static Func<VisualElement, MonoBehaviour, StageCatalog, ICoopMenu> Create;
    }
}
