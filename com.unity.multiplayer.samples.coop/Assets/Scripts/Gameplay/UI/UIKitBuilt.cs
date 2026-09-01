using UnityEngine;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Marks a subtree that <see cref="UIKit"/> built, and which is therefore already dressed.
    /// <see cref="ToonMenuRestyler"/> skips anything under one.
    /// </summary>
    /// <remarks>
    /// The restyler exists to dress UI that was authored somewhere else — prefabs from the
    /// original sample, mostly. Kit-built UI has already chosen its sprites, its roles and its
    /// type sizes, so a second pass over it can only undo decisions: re-tinting a red danger
    /// button back to the neutral text colour, upper-casing a room name someone typed, or laying
    /// a second contour over one that is already there. One marker component is cheaper than
    /// teaching the restyler to recognise every widget the kit can produce.
    /// </remarks>
    public class UIKitBuilt : MonoBehaviour
    {
    }
}
