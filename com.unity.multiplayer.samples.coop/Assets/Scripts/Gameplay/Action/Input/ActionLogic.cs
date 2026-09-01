using System;

namespace Unity.BossRoom.Gameplay.Actions
{
    /// <summary>
    /// List of all Types of Actions. There is a many-to-one mapping of Actions to ActionLogics.
    /// </summary>
    public enum ActionLogic
    {
        Melee,
        RangedTargeted,
        Chase,
        Revive,
        LaunchProjectile,
        Emote,
        RangedFXTargeted,
        AoE,
        Trample,
        ChargedShield,
        Stunned,
        Target,
        ChargedLaunchProjectile,
        StealthMode,
        DashAttack,
        ImpToss,
        PickUp,
        Drop,

        // NOTE: new values go at the END. ActionLogic is serialized by index in the Action
        // .asset files (Logic: 7 is AoE, and so on), so inserting a value in the middle would
        // silently re-point every existing action at the wrong logic.

        /// <summary>A spinning melee attack centred on the caster, hitting repeatedly while it
        /// runs. The Rogue's Twisting Slash.</summary>
        SpinAttack,

        /// <summary>A delayed area strike that lands on a chosen spot from above. The Mage's
        /// Meteor.</summary>
        MeteorStrike,

        /// <summary>A burst around the caster that damages and freezes whoever it catches. The
        /// Tank's Frost Nova.</summary>
        FrostNova,
    }
}
