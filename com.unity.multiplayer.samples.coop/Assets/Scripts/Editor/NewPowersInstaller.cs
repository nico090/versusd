using System.IO;
using Unity.BossRoom.Gameplay.Actions;
using Unity.BossRoom.Gameplay.Configuration;
using UnityEditor;
using UnityEngine;
using Action = Unity.BossRoom.Gameplay.Actions.Action;

namespace Unity.BossRoom.Editor
{
    /// <summary>
    /// One-click installer for the three added hero powers: the Rogue's Twisting Slash, the Mage's
    /// Meteor and the Tank's Frost Nova.
    /// </summary>
    /// <remarks>
    /// <para>This exists as an Editor tool rather than as hand-written .asset YAML for a reason
    /// this project has been bitten by before: the GameData assets are stored in Git LFS, and while
    /// the Unity Editor has the project open it serves its own cached copy of them — an asset
    /// edited on disk is silently reverted when the build is made. Going through AssetDatabase
    /// means the Editor itself performs the write, so it sticks.</para>
    ///
    /// <para>Three things have to line up for a new power to work at all, and doing any of them by
    /// hand is easy to get half-right: the .asset has to exist, it has to be in GameDataSource's
    /// prototype list (that list is what assigns the ActionID the network protocol uses — an action
    /// missing from it simply cannot be played), and it has to be in a CharacterClass skill slot.
    /// This does all three, and is safe to run repeatedly: existing assets are updated in place
    /// rather than duplicated.</para>
    /// </remarks>
    public static class NewPowersInstaller
    {
        const string k_GameDataSourcePath = "Assets/Prefabs/GameDataSource.prefab";
        const string k_AoeInputPath = "Assets/Prefabs/Actions/ClientAoeInput.prefab";

        // The meteor's three visuals, borrowed from the imp's toss attack — the one effect in the
        // project that already means "something lands here in a moment". The stock
        // AoeActionVisualization is deliberately NOT used: it is the sample's placeholder, an
        // unscaled primitive with no material on it at all (its child is literally named
        // TMP-REPLACE-ME), so it draws as a magenta lump the size of a barrel.
        const string k_MeteorTelegraphFxPath = "Assets/VFX/Prefabs/Imp/TossAttack/FX_IMP_TossAttack_Radius.prefab";
        const string k_MeteorFallingFxPath = "Assets/VFX/Prefabs/Imp/TossAttack/FX_IMP_TossAttack_Glow.prefab";
        const string k_MeteorImpactFxPath = "Assets/VFX/Prefabs/Imp/TossAttack/FX_IMP_TossAttack_Impact.prefab";

        const string k_TwistingSlashPath = "Assets/GameData/Action/Rogue/RogueTwistingSlash.asset";
        const string k_MeteorPath = "Assets/GameData/Action/Mage/MageMeteorStrike.asset";
        const string k_FrostNovaPath = "Assets/GameData/Action/Tank/TankFrostNova.asset";

        const string k_RogueClassPath = "Assets/GameData/Character/Rogue/Rogue.asset";
        const string k_MageClassPath = "Assets/GameData/Character/Mage/Mage.asset";
        const string k_TankClassPath = "Assets/GameData/Character/Tank/Tank.asset";

        // "Attack1" and "AnticipateMove" are the two triggers every hero animator is known to
        // have (all four base attacks use them). Inventing a new trigger name here would produce
        // a power that works mechanically but never animates.
        const string k_SafeAnim = "Attack1";
        const string k_SafeAnticipation = "AnticipateMove";

        [MenuItem("Boss Room/Actions/Install New Powers (Twisting Slash, Meteor, Frost Nova)")]
        public static void InstallPowers()
        {
            var twistingSlash = CreateOrUpdate<SpinAttackAction>(k_TwistingSlashPath, BuildTwistingSlashConfig());
            var meteor = CreateOrUpdate<MeteorStrikeAction>(k_MeteorPath, BuildMeteorConfig());
            var frostNova = CreateOrUpdate<FrostNovaAction>(k_FrostNovaPath, BuildFrostNovaConfig());

            if (twistingSlash == null || meteor == null || frostNova == null)
            {
                return;
            }

            // Without this the actions have no ActionID and cannot be requested over the network.
            int registered = RegisterPrototypes(twistingSlash, meteor, frostNova);

            AssignSkill3(k_RogueClassPath, twistingSlash, "Rogue");
            AssignSkill3(k_MageClassPath, meteor, "Mage");
            AssignSkill3(k_TankClassPath, frostNova, "Tank");

            // Icons are placeholders borrowed from each class's existing powers: the action bar
            // draws Config.Icon, and a null one renders as an empty button that looks broken.
            BorrowIconFrom(k_RogueClassPath, twistingSlash);
            BorrowIconFrom(k_MageClassPath, meteor);
            BorrowIconFrom(k_TankClassPath, frostNova);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[NewPowers] Installed 3 powers ({registered} newly added to GameDataSource). " +
                      "Rogue -> Twisting Slash, Mage -> Meteor, Tank -> Frost Nova. " +
                      "Icons are placeholders borrowed from each class's other skills; swap them when art exists.");
        }

        // ── Configs ───────────────────────────────────────────────────────────────────────────

        static ActionConfig BuildTwistingSlashConfig()
        {
            return new ActionConfig
            {
                Logic = ActionLogic.SpinAttack,
                // Per TICK, not per cast. The spin ticks roughly 6 times over 2 seconds, so this
                // is ~90 damage in total to anything that stays in it — worth committing to, and
                // survivable if you walk out.
                Amount = 15,
                Range = 0f,       // centred on the caster; there is nothing to close on
                Radius = 3.5f,
                DurationSeconds = 2f,
                ExecTimeSeconds = 0.25f,   // first tick, so it isn't a free instant hit
                ReuseTimeSeconds = 9f,
                AnimAnticipation = k_SafeAnticipation,
                Anim = k_SafeAnim,
                // NOT interruptible, and that is what lets the Rogue move while it spins.
                //
                // "Interruptible" in this codebase means "movement cancels it", and it is enforced
                // twice over: ServerActionPlayer stops the character dead the moment an
                // interruptible action starts, and ServerCharacter clears the action again as soon
                // as any movement input arrives. So the flag meant to be the escape hatch was the
                // thing pinning the Rogue in place — press the button and you rooted yourself,
                // touch the stick and the spin died on its first frame. Walking out is the escape
                // now, and it is a better one: you keep the move and you keep control.
                ActionInterruptible = false,
                BlockingMode = BlockingModeType.EntireDuration,
                IsFriendly = false,
                DisplayedName = "Twisting Slash",
                Description = "Spin with your blade out, striking everything around you repeatedly. You can keep moving while it lasts.",
            };
        }

        static ActionConfig BuildMeteorConfig()
        {
            // The reticle matters more than the art here: a telegraphed strike the caster
            // can't place precisely is just a gamble.
            var aoeInput = AssetDatabase.LoadAssetAtPath<GameObject>(k_AoeInputPath);

            BaseActionInput actionInput = aoeInput != null ? aoeInput.GetComponent<BaseActionInput>() : null;
            if (actionInput == null)
            {
                Debug.LogWarning($"[NewPowers] No BaseActionInput found at {k_AoeInputPath}. " +
                                 "Meteor will land wherever the player is aiming instead of showing a placement reticle.");
            }

            return new ActionConfig
            {
                ActionInput = actionInput,
                // Order is load-bearing: MeteorStrikeAction reads these by index — ground
                // telegraph, falling body, impact burst. Missing entries are tolerated there, so
                // art that moves costs a plainer-looking meteor rather than a broken power.
                Spawns = new[]
                {
                    LoadFxPrefab(k_MeteorTelegraphFxPath),
                    LoadFxPrefab(k_MeteorFallingFxPath),
                    LoadFxPrefab(k_MeteorImpactFxPath),
                },
                Logic = ActionLogic.MeteorStrike,
                Amount = 70,      // a real punish for standing still, but not a one-shot on anyone
                Range = 18f,      // called down from a distance; that IS the Mage's role
                // Radius up and delay down together, because the old pair could not connect
                // with a moving target: a full second of warning is about five metres of walking,
                // so a 4.5 radius centred on where somebody was standing was, in practice, a
                // guaranteed miss against anyone not standing still. 0.75s to clear 6 metres is
                // still an escape — you just have to react to it instead of strolling out.
                Radius = 6f,
                DurationSeconds = 1.6f,
                ExecTimeSeconds = 0.75f,   // the telegraph window — this is the counterplay
                ReuseTimeSeconds = 14f,
                KnockbackSpeed = 8f,
                KnockbackDuration = 0.35f,
                AnimAnticipation = k_SafeAnticipation,
                Anim = k_SafeAnim,
                // Not interruptible, for a different reason than the Rogue's spin: the impact
                // point is fixed at cast time, so there is nothing left for the caster's own
                // movement to invalidate — but with the flag on, walking during the telegraph
                // cleared the action and the meteor simply never landed. A Mage who casts and
                // keeps moving is every Mage, which is most of why this power "never hit".
                ActionInterruptible = false,
                BlockingMode = BlockingModeType.OnlyDuringExecTime,
                IsFriendly = false,
                DisplayedName = "Meteor",
                Description = "Call a meteor down on a spot. It takes a moment to land — and anyone with sense will have moved.",
            };
        }

        /// <summary>Loads one VFX prefab, warning rather than failing if the art has moved.</summary>
        static GameObject LoadFxPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[NewPowers] Missing VFX prefab at {path}. The power still works; " +
                                 "that part of the effect just won't be drawn.");
            }

            return prefab;
        }

        static ActionConfig BuildFrostNovaConfig()
        {
            return new ActionConfig
            {
                Logic = ActionLogic.FrostNova,
                Amount = 20,      // low: the freeze is the payload, the damage is a garnish
                Range = 0f,       // centred on the caster
                Radius = 5f,
                DurationSeconds = 0.8f,
                ExecTimeSeconds = 0.3f,
                EffectDurationSeconds = 1.5f,   // how long victims stay frozen
                ReuseTimeSeconds = 16f,
                AnimAnticipation = k_SafeAnticipation,
                Anim = k_SafeAnim,
                ActionInterruptible = false,
                BlockingMode = BlockingModeType.OnlyDuringExecTime,
                IsFriendly = false,
                DisplayedName = "Frost Nova",
                Description = "Burst of ice around you. Anyone caught is frozen solid for a moment — long enough to finish them.",
            };
        }

        // ── Asset plumbing ────────────────────────────────────────────────────────────────────

        static T CreateOrUpdate<T>(string path, ActionConfig config) where T : Action
        {
            EnsureDirectory(path);

            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                // Re-running the installer re-applies the tuned config but keeps the asset's
                // identity — its GUID is what the CharacterClass and GameDataSource point at, so
                // deleting and recreating would quietly break both.
                existing.Config = config;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                Debug.LogError($"[NewPowers] {path} already exists but is not a {typeof(T).Name}. " +
                               "Move or delete it and run this again.");
                return null;
            }

            var created = ScriptableObject.CreateInstance<T>();
            created.Config = config;
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        static void EnsureDirectory(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Adds the actions to GameDataSource's prototype array. That array is the single source of
        /// ActionIDs (see GameDataSource.BuildActionIDs), so this is the step that actually makes a
        /// power exist as far as the network is concerned.
        /// </summary>
        static int RegisterPrototypes(params Action[] actions)
        {
            var gameDataSource = AssetDatabase.LoadAssetAtPath<GameObject>(k_GameDataSourcePath);
            if (gameDataSource == null)
            {
                Debug.LogError($"[NewPowers] Could not find {k_GameDataSourcePath}. The powers exist but are unusable until they are added to GameDataSource's action prototypes.");
                return 0;
            }

            var component = gameDataSource.GetComponent<Unity.BossRoom.Gameplay.GameplayObjects.GameDataSource>();
            if (component == null)
            {
                Debug.LogError("[NewPowers] GameDataSource.prefab has no GameDataSource component.");
                return 0;
            }

            var serializedObject = new SerializedObject(component);
            var prototypes = serializedObject.FindProperty("m_ActionPrototypes");
            if (prototypes == null || !prototypes.isArray)
            {
                Debug.LogError("[NewPowers] Could not find the m_ActionPrototypes array on GameDataSource.");
                return 0;
            }

            int added = 0;
            foreach (var action in actions)
            {
                if (ContainsAction(prototypes, action))
                {
                    continue;
                }

                prototypes.InsertArrayElementAtIndex(prototypes.arraySize);
                prototypes.GetArrayElementAtIndex(prototypes.arraySize - 1).objectReferenceValue = action;
                added++;
            }

            if (added > 0)
            {
                serializedObject.ApplyModifiedProperties();
                PrefabUtility.SavePrefabAsset(gameDataSource);
            }

            return added;
        }

        static bool ContainsAction(SerializedProperty array, Action action)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == action)
                {
                    return true;
                }
            }

            return false;
        }

        static void AssignSkill3(string classAssetPath, Action action, string label)
        {
            var characterClass = AssetDatabase.LoadAssetAtPath<CharacterClass>(classAssetPath);
            if (characterClass == null)
            {
                Debug.LogError($"[NewPowers] Could not load {classAssetPath}; {label} keeps its old skills.");
                return;
            }

            if (characterClass.Skill3 != null && characterClass.Skill3 != action)
            {
                // Never silently displace a power somebody already put there.
                Debug.LogWarning($"[NewPowers] {label}'s Skill3 is already '{characterClass.Skill3.name}'. " +
                                 $"Left alone — assign '{action.name}' by hand if you meant to replace it.");
                return;
            }

            characterClass.Skill3 = action;
            EditorUtility.SetDirty(characterClass);
        }

        /// <summary>
        /// Gives a new power a placeholder icon taken from one of its class's existing skills, so
        /// the action bar shows something recognisable instead of an empty button.
        /// </summary>
        static void BorrowIconFrom(string classAssetPath, Action action)
        {
            if (action.Config.Icon != null)
            {
                return;
            }

            var characterClass = AssetDatabase.LoadAssetAtPath<CharacterClass>(classAssetPath);
            if (characterClass == null)
            {
                return;
            }

            foreach (var donor in new[] { characterClass.Skill2, characterClass.Skill1 })
            {
                if (donor != null && donor != action && donor.Config.Icon != null)
                {
                    action.Config.Icon = donor.Config.Icon;
                    EditorUtility.SetDirty(action);
                    return;
                }
            }
        }
    }
}
