using UnityEditor;
using UnityEngine;
using Unity.BossRoom.Gameplay.GameplayObjects;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// Gives the minor enemies the component that makes killing them worth points.
    /// </summary>
    /// <remarks>
    /// <para>The scoring rules were complete all along —
    /// <c>ServerScoreTracker.OnDeath</c> already awards <c>DeathmatchRules.PointsPerNpcKill</c>
    /// and bumps the <c>NpcKills</c> counter. What was missing sat one layer down: that tracker
    /// listens for <c>LifeStateChangedEventMessage</c>, and the only thing that publishes one is
    /// <see cref="PublishMessageOnLifeChange"/>, which was present on <c>PlayerAvatar</c> and
    /// <c>ImpBoss</c> but on neither <c>Imp</c> nor <c>VandalImp</c>. So a dead imp never announced
    /// itself, the tracker never ran, and the imp branch of the scoring code was unreachable.</para>
    ///
    /// <para>Attribution was never the problem: <c>ServerCharacter.ReceiveHP</c> sets
    /// <c>m_LastLethalInflicter</c> before flipping LifeState to Dead, so the killer is known by
    /// the time the message would be built.</para>
    ///
    /// <para><b>Why the prefabs and not the base.</b> Both imps are variants of <c>Enemy.prefab</c>,
    /// so adding the component there would cover them in one edit — but <c>ImpBoss</c> is a variant
    /// of <c>Enemy</c> too and already carries its own copy. It would end up with two, publish
    /// every death twice, and pay out double for the boss. Adding it to the two prefabs that
    /// actually lack it leaves the boss alone.</para>
    /// </remarks>
    public static class NpcScoringFixPass
    {
        const string k_CharacterFolder = "Assets/Prefabs/Character";

        /// <summary>
        /// Prefab name to the display name the kill feed falls back on. Minor NPCs get no kill-feed
        /// line today (at one point each they would flood it), but the field is what
        /// <c>GetDisplayName</c> reads when an imp is the *killer*, so it still has to be set.
        /// </summary>
        static readonly (string Prefab, string DisplayName)[] k_Targets =
        {
            ("Imp", "Imp"),
            ("VandalImp", "Vandal Imp"),
        };

        [MenuItem("Boss Room/Fixes/Enable NPC Kill Scoring")]
        public static void Apply()
        {
            int changed = 0;

            foreach (var (prefabName, displayName) in k_Targets)
            {
                string path = $"{k_CharacterFolder}/{prefabName}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogWarning($"[NpcScoring] {path} not found — skipped.");
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<PublishMessageOnLifeChange>() != null)
                    {
                        Debug.Log($"[NpcScoring] {prefabName} already publishes deaths — left alone.");
                        continue;
                    }

                    var publisher = root.AddComponent<PublishMessageOnLifeChange>();

                    // m_CharacterName is private and serialized; SerializedObject is the only way
                    // to set it without widening the field's access for the sake of this pass.
                    var so = new SerializedObject(publisher);
                    var nameProperty = so.FindProperty("m_CharacterName");
                    if (nameProperty != null)
                    {
                        nameProperty.stringValue = displayName;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    else
                    {
                        Debug.LogWarning($"[NpcScoring] {prefabName}: no m_CharacterName field to set.");
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed++;
                    Debug.Log($"[NpcScoring] {prefabName} now publishes its death as \"{displayName}\".");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[NpcScoring] Done — {changed} prefab(s) changed.");
        }

        [MenuItem("Boss Room/Fixes/Revert NPC Kill Scoring")]
        public static void Revert()
        {
            int changed = 0;

            foreach (var (prefabName, _) in k_Targets)
            {
                string path = $"{k_CharacterFolder}/{prefabName}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var publisher = root.GetComponent<PublishMessageOnLifeChange>();
                    if (publisher == null)
                    {
                        continue;
                    }

                    Object.DestroyImmediate(publisher, true);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[NpcScoring] Reverted — {changed} prefab(s) changed.");
        }
    }
}
