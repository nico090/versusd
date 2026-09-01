using System.Collections.Generic;
using Unity.BossRoom.Gameplay.Actions;
using Unity.BossRoom.Gameplay.Configuration;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using UnityEngine;
using Action = Unity.BossRoom.Gameplay.Actions.Action;
using Random = UnityEngine.Random;

namespace Unity.BossRoom.Gameplay.Bots
{
    /// <summary>
    /// Server-side "hands" of a bot: it looks around, walks, aims and presses the same buttons a
    /// player presses.
    /// </summary>
    /// <remarks>
    /// <para>Attached at runtime to a bot's avatar by <see cref="ServerBotManager.TryAttachBrain"/>,
    /// so nothing needs to be wired into the player prefab and a human's avatar is untouched.</para>
    ///
    /// <para>Deliberately not built on <see cref="Unity.BossRoom.Gameplay.GameplayObjects.Character.AI.AIBrain"/>:
    /// that one drives monsters, and its rules are monster rules — it only attacks characters it
    /// "hates", it refuses to consider NPCs as foes at all, and it closes to melee on everything.
    /// A bot has to behave like a hero in a free-for-all: hold a class-appropriate distance, kite,
    /// pick a fight worth having, back off when hurt.</para>
    ///
    /// <para>Every action it takes goes through the <c>Server*</c> entry points on
    /// <see cref="ServerCharacter"/>, which are the exact bodies of the player Commands. That is
    /// what keeps a bot honest: it is frozen at the end of a match like everyone else, it loses
    /// spawn protection the moment it attacks, and it cannot fire an attack that is still on
    /// cooldown.</para>
    /// </remarks>
    public class BotBrain : MonoBehaviour
    {
        // How often the bot re-evaluates who to fight. Fast enough to react to someone walking up
        // behind it, slow enough that a dozen bots don't sweep physics every frame.
        const float k_TargetScanInterval = 0.4f;

        // Range of the "who is around me" sweep. Comfortably beyond any hero's attack range so the
        // bot notices a fight before it is already in one.
        const float k_ScanRadius = 22f;

        // Perlin noise drives the aim error, so misses drift in and out like a wobbly hand
        // instead of flickering randomly frame to frame.
        const float k_AimNoiseSpeed = 0.7f;

        // Eye height for line-of-fire checks — waist/chest high, matching ClientInputSender.
        const float k_EyeHeight = 1f;

        // Fallback attack range when an action declares none.
        const float k_DefaultAttackRange = 8f;

        // Seconds of retreat budget regained per second spent not retreating. Below 1 on purpose:
        // room to disengage is something a bot earns by staying in the fight.
        const float k_RetreatBudgetRefillRate = 0.5f;

        // A player is worth this many "metres" of preference over an NPC at the same distance:
        // in a deathmatch a player kill is worth five imps, and imps are everywhere.
        const float k_PlayerPreferenceMetres = 12f;

        // How much closer a badly wounded foe feels to a bot that likes finishing people off.
        const float k_WoundedPreferenceMetres = 14f;

        // How much further away a foe feels for every *other* bot already fighting them. Being
        // jumped by the whole lobby at once is the thing that makes a match unplayable, and it
        // happens by accident: every bot scores targets the same way, so they all converge on
        // whoever is most convenient. This much penalty is enough that the second bot goes and
        // finds its own fight unless the crowded one is dramatically closer.
        const float k_ContestedTargetMetres = 16f;

        // Every brain currently running, so a bot can tell how many of its peers are already on a
        // given foe. Server-only and tiny (a handful of bots), so a linear scan costs nothing.
        static readonly List<BotBrain> s_ActiveBrains = new List<BotBrain>();

        ServerCharacter m_Self;
        BotProfile m_Profile;

        ServerCharacter m_Target;
        float m_NextScanTime;
        float m_EngageAtTime;
        float m_NextSkillTime;
        float m_AimNoiseSeed;

        // Strafe direction (+1/-1) and when to flip it, so the bot circles rather than jittering.
        float m_StrafeSign = 1f;
        float m_NextStrafeFlip;

        // Kiting budget: how much backing away this bot has left in it, and until when it is
        // pinned into standing and fighting. See UpdateRetreatBudget.
        float m_RetreatSecondsLeft;
        float m_HoldGroundUntil;

        // Earliest the basic attack may go out again, which is later than its cooldown.
        float m_NextBasicAttackTime;

        // Engagement rhythm: when this bot started pressing its current target, and until when it
        // is taking a breather. See UpdateEngagementRhythm.
        float m_PressureStartedAt;
        float m_BreatherUntil;

        // Wander state, used when there is nobody to fight.
        Vector3 m_WanderDirection;
        float m_NextWanderChange;
        Vector3 m_LastWanderPosition;
        float m_NextStuckCheck;

        // When a charged attack should be released early, or 0 if it should charge to full.
        float m_ChargeReleaseTime;

        readonly Collider[] m_ScanHits = new Collider[48];
        readonly RaycastHit[] m_LineOfFireHits = new RaycastHit[8];
        LayerMask m_ScanMask;
        LayerMask m_LineOfFireMask;

        void OnEnable() => s_ActiveBrains.Add(this);

        void OnDisable() => s_ActiveBrains.Remove(this);

        public void Initialize(ServerCharacter self, BotProfile profile)
        {
            m_Self = self;
            m_Profile = profile;
            m_AimNoiseSeed = Random.value * 1000f;
            m_ScanMask = LayerMask.GetMask("PCs", "NPCs");
            m_LineOfFireMask = LayerMask.GetMask("Default", "Environment");
            m_NextSkillTime = Time.time + profile.EffectiveSkillIntervalSeconds * Random.Range(0.3f, 1f);
            m_RetreatSecondsLeft = profile.EffectiveRetreatBudgetSeconds;
            m_WanderDirection = Random.insideUnitSphere.WithY(0).normalized;
        }

        void Update()
        {
            if (m_Self == null || m_Profile == null)
            {
                return;
            }

            // The match is over and the final table is up. Players have their input dropped at the
            // ServerCharacter Commands; a bot must stop asking, or it would spend the endgame
            // walking into a wall.
            if (ServerCharacter.MatchInputFrozen)
            {
                Stop();
                return;
            }

            if (m_Self.LifeState != LifeState.Alive)
            {
                // Dead or downed: no input at all. The respawn is handled by ServerBossRoomState,
                // the same as for a player, and this brain simply picks up again afterwards.
                m_Target = null;
                return;
            }

            ReleaseChargedAttackIfDue();

            if (Time.time >= m_NextScanTime)
            {
                m_NextScanTime = Time.time + k_TargetScanInterval;
                AcquireTarget();
            }

            if (m_Target == null || !IsViableTarget(m_Target))
            {
                m_Target = null;
                m_PressureStartedAt = 0f;
                m_BreatherUntil = 0f;
                Wander();
                return;
            }

            UpdateEngagementRhythm();

            // Reaction time: the bot has seen the foe but hasn't acted on it yet. It still walks
            // (it was already walking), it just doesn't shoot — which is what a delay actually
            // looks like on a human.
            bool reacted = Time.time >= m_EngageAtTime;

            AimAtTarget();
            MoveRelativeToTarget();

            if (!reacted)
            {
                return;
            }

            if (IsTakingBreather)
            {
                // A bot on a breather stays in the fight and keeps circling, it just isn't
                // throwing anything: that pause is the player's turn. Patching itself up is the
                // exception, because backing off to heal is the main reason a player pauses.
                if (m_Self.CharacterClass != null)
                {
                    TryUseSelfSupportSkill(m_Self.CharacterClass);
                }

                return;
            }

            TryAttack();
        }

        // ── Engagement rhythm ─────────────────────────────────────────────────────────────────

        /// <summary>True while the bot is between bursts of aggression.</summary>
        bool IsTakingBreather => Time.time < m_BreatherUntil;

        /// <summary>
        /// Runs the press/rest clock: after <see cref="BotProfile.PressureSeconds"/> of unbroken
        /// aggression the bot backs off and stops attacking for
        /// <see cref="BotProfile.BreatherSeconds"/>, then starts a fresh stint.
        /// </summary>
        /// <remarks>
        /// Nothing else in here ever makes a bot stop coming at you. It closes to its preferred
        /// range and attacks whenever it can, which one-on-one is merely hard, but three of them
        /// doing it at once leaves no moment in the fight that belongs to the player. Players
        /// break off constantly — to reposition, to wait out a cooldown, because they lost their
        /// nerve — and this is the cheapest way to give a bot the same shape.
        /// </remarks>
        void UpdateEngagementRhythm()
        {
            float now = Time.time;

            if (now < m_BreatherUntil)
            {
                return;
            }

            if (m_BreatherUntil > 0f)
            {
                // Rested: start the next stint from here.
                m_BreatherUntil = 0f;
                m_PressureStartedAt = now;
                return;
            }

            if (m_PressureStartedAt <= 0f)
            {
                m_PressureStartedAt = now;
                return;
            }

            if (now - m_PressureStartedAt >= m_Profile.EffectivePressureSeconds)
            {
                m_BreatherUntil = now + m_Profile.EffectiveBreatherSeconds;
            }
        }

        // ── Target selection ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Picks the fight worth having. Scored in metres so every preference is expressed as
        /// "this foe feels N metres closer", which keeps distance, target type and the bot's taste
        /// for finishing off the wounded on one comparable scale.
        /// </summary>
        void AcquireTarget()
        {
            Vector3 myPosition = m_Self.physicsWrapper.Transform.position;
            int hitCount = Physics.OverlapSphereNonAlloc(myPosition, k_ScanRadius, m_ScanHits, m_ScanMask);

            ServerCharacter best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                var candidate = m_ScanHits[i].GetComponentInParent<ServerCharacter>();
                if (!IsViableTarget(candidate))
                {
                    continue;
                }

                Vector3 candidatePosition = candidate.physicsWrapper.Transform.position;
                float score = Vector3.Distance(myPosition, candidatePosition);

                if (candidate.IsNpc)
                {
                    // An uninterested bot walks past imps; an interested one treats them as a
                    // legitimate source of points.
                    score += k_PlayerPreferenceMetres * (1f - m_Profile.NpcInterest);
                }
                else
                {
                    score -= k_PlayerPreferenceMetres;
                }

                float healthFraction = candidate.MaxHitPoints > 0
                    ? Mathf.Clamp01((float)candidate.HitPoints / candidate.MaxHitPoints)
                    : 1f;
                score -= (1f - healthFraction) * k_WoundedPreferenceMetres * m_Profile.WoundedTargetBias;

                // Somebody else's fight. Without this every bot picks the same foe and the match
                // turns into a gang-up, which is the single worst thing to be on the receiving
                // end of — and it is nobody's decision, just four bots agreeing by accident.
                score += k_ContestedTargetMetres * CountBotsTargeting(candidate);

                // Don't commit to someone on the far side of a wall.
                if (!HasLineOfFire(myPosition, candidatePosition))
                {
                    continue;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best != m_Target)
            {
                m_Target = best;
                m_EngageAtTime = Time.time + m_Profile.EffectiveReactionSeconds;
                // A new fight starts a fresh stint, so switching targets can't be used to dodge
                // the breather — nor does it carry an old one over onto somebody new.
                m_PressureStartedAt = 0f;
                m_BreatherUntil = 0f;
            }
        }

        /// <summary>How many other bots are already fighting <paramref name="candidate"/>.</summary>
        int CountBotsTargeting(ServerCharacter candidate)
        {
            int count = 0;
            for (int i = 0; i < s_ActiveBrains.Count; i++)
            {
                var brain = s_ActiveBrains[i];
                if (brain != this && brain.m_Target == candidate)
                {
                    count++;
                }
            }

            return count;
        }

        bool IsViableTarget(ServerCharacter candidate)
        {
            return candidate != null
                   && candidate != m_Self
                   && candidate.LifeState == LifeState.Alive
                   && !candidate.IsStealthy
                   && candidate.physicsWrapper != null;
        }

        bool HasLineOfFire(Vector3 from, Vector3 to)
        {
            Vector3 eye = from + Vector3.up * k_EyeHeight;
            Vector3 targetEye = to + Vector3.up * k_EyeHeight;
            Vector3 delta = targetEye - eye;
            float distance = delta.magnitude;
            if (distance < 0.01f)
            {
                return true;
            }

            int hitCount = Physics.RaycastNonAlloc(new Ray(eye, delta / distance), m_LineOfFireHits,
                distance, m_LineOfFireMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                // Bodies are not cover — same rule as the player's aim assist, and for the same
                // reason: a plain raycast ends inside the target and would always report a wall.
                if (m_LineOfFireHits[i].transform.GetComponentInParent<ServerCharacter>() != null)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        // ── Aiming ────────────────────────────────────────────────────────────────────────────

        void AimAtTarget()
        {
            Vector3 toTarget = m_Target.physicsWrapper.Transform.position - m_Self.physicsWrapper.Transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                return;
            }

            m_Self.ServerSetAimDirection(ApplyAimError(toTarget.normalized));
        }

        /// <summary>
        /// Swings the aim off-target by a slowly wandering amount. A bot whose aim is exact reads
        /// as a machine even when everything else about it is human; a bot whose aim jitters
        /// randomly reads as broken. Perlin noise gives it a hand that drifts.
        /// </summary>
        Vector3 ApplyAimError(Vector3 direction)
        {
            float error = m_Profile.EffectiveAimErrorDegrees;
            if (error <= 0.01f)
            {
                return direction;
            }

            float noise = Mathf.PerlinNoise(m_AimNoiseSeed, Time.time * k_AimNoiseSpeed) * 2f - 1f;
            return Quaternion.Euler(0f, noise * error, 0f) * direction;
        }

        // ── Movement ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Holds the distance the personality wants to fight at: close it, back off it, or circle
        /// on it. This is the main thing that makes a Brawler and a Sniper read as different
        /// players rather than the same bot with different numbers.
        /// </summary>
        void MoveRelativeToTarget()
        {
            Vector3 myPosition = m_Self.physicsWrapper.Transform.position;
            Vector3 toTarget = m_Target.physicsWrapper.Transform.position - myPosition;
            toTarget.y = 0f;

            float distance = toTarget.magnitude;
            if (distance < 0.01f)
            {
                Stop();
                return;
            }

            Vector3 forward = toTarget / distance;
            Vector3 strafe = Vector3.Cross(Vector3.up, forward) * CurrentStrafeSign();

            float preferredDistance = Mathf.Max(1.5f, AttackRange() * m_Profile.PreferredRangeFactor);
            bool hurt = HealthFraction() < m_Profile.RetreatHealthFraction;
            bool crowded = distance < preferredDistance * 0.75f;

            // Charged against the budget here rather than inside the branch: the bot is trying to
            // back off for as long as it wants space, whether or not it gets to this frame.
            bool mayGiveGround = UpdateRetreatBudget(crowded && !hurt);

            Vector3 desired;
            if (IsTakingBreather)
            {
                // Resetting the fight rather than leaving it: gives ground only while the foe is
                // inside the range it wants, otherwise just circles. It isn't attacking during
                // this window, so backing off here reads as a pause, not as running away.
                desired = (crowded ? -forward * 0.55f : forward * 0.1f)
                          + strafe * Mathf.Max(0.6f, m_Profile.StrafeAmount);
            }
            else if (hurt)
            {
                // Break off. Still circling, so it retreats around cover rather than backing into
                // a corner in a straight line. A real escape at low HP is not budgeted — a player
                // runs too, and RetreatHealthFraction already makes it rare.
                desired = -forward * 0.8f + strafe * 0.6f;
            }
            else if (distance > preferredDistance * 1.15f)
            {
                desired = forward * Mathf.Lerp(0.6f, 1f, m_Profile.Aggression)
                          + strafe * m_Profile.StrafeAmount * 0.5f;
            }
            else if (crowded && mayGiveGround)
            {
                desired = -forward * 0.7f + strafe * m_Profile.StrafeAmount;
            }
            else if (crowded)
            {
                // Out of room to give: stand and trade. It keeps circling, because a bot planted
                // dead still reads as one that has frozen, but it stops walking away — which is
                // the entire point of the budget.
                desired = strafe * Mathf.Max(0.5f, m_Profile.StrafeAmount) + forward * 0.15f;
            }
            else
            {
                // At the range it wants: circle, and drift in or out a little so two bots at the
                // same preferred range don't lock into a static stand-off.
                desired = strafe * Mathf.Max(0.4f, m_Profile.StrafeAmount)
                          + forward * Random.Range(-0.15f, 0.15f);
            }

            desired.y = 0f;
            if (desired.sqrMagnitude < 0.0001f)
            {
                Stop();
                return;
            }

            m_Self.ServerSetMovementDirection(desired.normalized);
        }

        /// <summary>
        /// Runs the kiting budget and answers whether the bot may give ground this frame.
        /// </summary>
        /// <remarks>
        /// <para>A retreating bot moves at exactly the player's speed, so unlimited kiting is not
        /// a difficulty setting — it is an unbeatable one. Without this the loop was: attack,
        /// walk backwards for the whole cooldown, attack again the frame it is up. The budget
        /// drains while the bot wants space and trickles back while it doesn't, and running it dry
        /// pins the bot into <see cref="BotProfile.HoldGroundSeconds"/> of standing and trading —
        /// which is the window a player needs to actually land something.</para>
        ///
        /// <para>Surviving that window refills the budget: holding its ground is what earns a bot
        /// the right to disengage again, so the behaviour cycles instead of latching off.</para>
        /// </remarks>
        bool UpdateRetreatBudget(bool wantsSpace)
        {
            float now = Time.time;

            if (m_HoldGroundUntil > 0f)
            {
                if (now < m_HoldGroundUntil)
                {
                    return false;
                }

                m_HoldGroundUntil = 0f;
                m_RetreatSecondsLeft = m_Profile.EffectiveRetreatBudgetSeconds;
            }

            if (!wantsSpace)
            {
                m_RetreatSecondsLeft = Mathf.Min(
                    m_Profile.EffectiveRetreatBudgetSeconds,
                    m_RetreatSecondsLeft + Time.deltaTime * k_RetreatBudgetRefillRate);
                return true;
            }

            m_RetreatSecondsLeft -= Time.deltaTime;
            if (m_RetreatSecondsLeft > 0f)
            {
                return true;
            }

            m_RetreatSecondsLeft = 0f;
            m_HoldGroundUntil = now + m_Profile.EffectiveHoldGroundSeconds;
            return false;
        }

        /// <summary>
        /// Follow-through: having committed to an attack, the bot stays in it for a beat instead
        /// of stepping back on the same frame it swings.
        /// </summary>
        void HoldGroundAfterAttacking()
        {
            m_HoldGroundUntil = Mathf.Max(m_HoldGroundUntil, Time.time + m_Profile.EffectiveFollowThroughSeconds);
        }

        float CurrentStrafeSign()
        {
            if (Time.time >= m_NextStrafeFlip)
            {
                m_NextStrafeFlip = Time.time + Random.Range(1.2f, 3.5f);
                // A bot that always circles the same way is a tell; flipping at random intervals
                // is what makes the movement read as a person changing their mind.
                m_StrafeSign = Random.value < 0.5f ? -1f : 1f;
            }

            return m_StrafeSign;
        }

        /// <summary>
        /// No target: roam. Bots that stand still in a corner are the clearest possible giveaway,
        /// and roaming is also what puts them where the fighting is.
        /// </summary>
        void Wander()
        {
            Vector3 position = m_Self.physicsWrapper.Transform.position;

            if (Time.time >= m_NextWanderChange)
            {
                m_NextWanderChange = Time.time + Random.Range(2f, 4.5f);
                m_WanderDirection = Random.insideUnitSphere.WithY(0f).normalized;
            }

            // Walked into a wall: the NavMeshAgent slides but the bot would keep pushing forever.
            // If it has barely moved since the last check, pick somewhere else to go.
            if (Time.time >= m_NextStuckCheck)
            {
                m_NextStuckCheck = Time.time + 1f;
                if ((position - m_LastWanderPosition).sqrMagnitude < 0.25f)
                {
                    m_WanderDirection = Quaternion.Euler(0f, Random.Range(100f, 260f), 0f) * m_WanderDirection;
                    m_NextWanderChange = Time.time + Random.Range(2f, 4.5f);
                }

                m_LastWanderPosition = position;
            }

            m_Self.ServerSetMovementDirection(m_WanderDirection);
            m_Self.ServerSetAimDirection(m_WanderDirection);
        }

        void Stop()
        {
            m_Self.ServerSetMovementDirection(Vector3.zero);
        }

        // ── Attacking ─────────────────────────────────────────────────────────────────────────

        void TryAttack()
        {
            var characterClass = m_Self.CharacterClass;
            if (characterClass == null)
            {
                return;
            }

            // A support skill on itself (the Mage's self-heal is a friendly Melee) is worth more
            // than any attack when the bot is in trouble.
            if (TryUseSelfSupportSkill(characterClass))
            {
                return;
            }

            float distance = Vector3.Distance(
                m_Self.physicsWrapper.Transform.position,
                m_Target.physicsWrapper.Transform.position);

            // Out of reach: keep walking, don't fire into the floor.
            if (distance > AttackRange() * 1.1f)
            {
                return;
            }

            // Specials come out on their own rhythm; the basic attack fills the gaps.
            if (Time.time >= m_NextSkillTime)
            {
                var special = ChooseSpecialSkill(characterClass, distance);
                if (special != null)
                {
                    m_NextSkillTime = Time.time + m_Profile.EffectiveSkillIntervalSeconds * Random.Range(0.8f, 1.25f);
                    PlaySkill(special, m_Target);
                    return;
                }
            }

            // The basic attack waits out a short hesitation on top of its cooldown. A bot that
            // fires on the exact frame the cooldown ends wins every straight trade by reflex
            // alone, and it is what makes one read as a machine rather than a bad player.
            if (characterClass.Skill1 != null
                && Time.time >= m_NextBasicAttackTime
                && m_Self.ActionPlayer.IsReuseTimeElapsed(characterClass.Skill1.ActionID))
            {
                m_NextBasicAttackTime = Time.time + BotDifficulty.BasicAttackHesitationSeconds();
                PlaySkill(characterClass.Skill1, m_Target);
            }
        }

        /// <summary>
        /// The Mage's Healing Touch and anything else shaped like it: friendly, Melee, cast on
        /// self. Worth using well before the retreat threshold, since healing at 10% HP is already
        /// too late.
        /// </summary>
        bool TryUseSelfSupportSkill(CharacterClass characterClass)
        {
            if (HealthFraction() > Mathf.Max(0.55f, m_Profile.RetreatHealthFraction + 0.2f))
            {
                return false;
            }

            foreach (var skill in new[] { characterClass.Skill2, characterClass.Skill3, characterClass.Skill1 })
            {
                if (skill == null || !skill.Config.IsFriendly || skill.Config.Logic != ActionLogic.Melee)
                {
                    continue;
                }

                if (!m_Self.ActionPlayer.IsReuseTimeElapsed(skill.ActionID))
                {
                    continue;
                }

                var request = new ActionRequestData
                {
                    ActionID = skill.ActionID,
                    ShouldClose = false,
                    CancelMovement = true,
                };
                m_Self.ServerPlayAction(request);
                return true;
            }

            return false;
        }

        /// <summary>Picks a ready, in-range offensive special, or null if none apply.</summary>
        Action ChooseSpecialSkill(CharacterClass characterClass, float distance)
        {
            // Skill2 first, then Skill3 — the class's power before its situational trick, which
            // is roughly the order a player reaches for them.
            foreach (var skill in new[] { characterClass.Skill2, characterClass.Skill3 })
            {
                if (skill == null || skill.Config.IsFriendly)
                {
                    continue;
                }

                if (!m_Self.ActionPlayer.IsReuseTimeElapsed(skill.ActionID))
                {
                    continue;
                }

                float range = EffectiveRange(skill);
                if (range > 0f && distance > range)
                {
                    continue;
                }

                return skill;
            }

            return null;
        }

        /// <summary>
        /// Builds and sends the action request. The per-logic field population mirrors
        /// <c>ClientInputSender.PopulateSkillRequest</c> — a projectile needs a Direction, a ground
        /// AoE needs a Position — so a bot's request is indistinguishable from a player's on the
        /// wire and every Action reads the fields it expects.
        /// </summary>
        void PlaySkill(Action skill, ServerCharacter target)
        {
            Vector3 myPosition = m_Self.physicsWrapper.Transform.position;
            Vector3 targetPosition = target.physicsWrapper.Transform.position;

            Vector3 toTarget = targetPosition - myPosition;
            toTarget.y = 0f;
            Vector3 direction = toTarget.sqrMagnitude > 0.0001f
                ? ApplyAimError(toTarget.normalized)
                : m_Self.physicsWrapper.Transform.forward;

            // Aim error has to move the impact point too, or a bot would "miss" with its arrows
            // while its fireballs landed dead centre.
            Vector3 aimedPoint = myPosition + direction * toTarget.magnitude;

            var request = new ActionRequestData
            {
                ActionID = skill.ActionID,
                TargetIds = new[] { target.NetworkObjectId },
                ShouldClose = true,
            };

            switch (skill.Config.Logic)
            {
                case ActionLogic.LaunchProjectile:
                    request.Direction = direction;
                    request.ShouldClose = false;
                    break;
                case ActionLogic.Melee:
                    request.Direction = direction;
                    break;
                case ActionLogic.Target:
                    request.ShouldClose = false;
                    break;
                case ActionLogic.RangedFXTargeted:
                case ActionLogic.DashAttack:
                case ActionLogic.MeteorStrike:
                    request.Position = aimedPoint;
                    request.Direction = direction;
                    break;
                case ActionLogic.SpinAttack:
                case ActionLogic.FrostNova:
                    // Centred on the caster — no target, and nothing to chase. Leaving ShouldClose
                    // on would make the bot run at somebody before setting off a burst it was
                    // already standing in the middle of.
                    request.TargetIds = null;
                    request.Direction = direction;
                    request.ShouldClose = false;
                    break;
                default:
                    request.Direction = direction;
                    break;
            }

            m_Self.ServerPlayAction(request);
            HoldGroundAfterAttacking();
            ScheduleChargeRelease(skill);
        }

        /// <summary>
        /// A charged attack (the Archer's charged shot, the Tank's shield) fires by itself once it
        /// reaches full charge. A player often lets go early, though — under pressure, or because
        /// they misjudged it — so a bot whose personality is impatient does too, and takes the
        /// weaker projectile that comes with it.
        /// </summary>
        void ScheduleChargeRelease(Action skill)
        {
            m_ChargeReleaseTime = 0f;

            if (skill.Config.ActionInput == null || m_Profile.ChargeHoldFraction >= 1f)
            {
                return;
            }

            float chargeWindow = skill.Config.ExecTimeSeconds;
            if (chargeWindow <= 0f)
            {
                return;
            }

            m_ChargeReleaseTime = Time.time + chargeWindow * Mathf.Clamp01(m_Profile.ChargeHoldFraction);
        }

        void ReleaseChargedAttackIfDue()
        {
            if (m_ChargeReleaseTime > 0f && Time.time >= m_ChargeReleaseTime)
            {
                m_ChargeReleaseTime = 0f;
                m_Self.ServerStopChargingUp();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────

        float HealthFraction()
        {
            int max = m_Self.MaxHitPoints;
            return max > 0 ? Mathf.Clamp01((float)m_Self.HitPoints / max) : 1f;
        }

        /// <summary>How far this bot can actually hit from, based on its basic attack.</summary>
        float AttackRange()
        {
            var skill1 = m_Self.CharacterClass != null ? m_Self.CharacterClass.Skill1 : null;
            float range = skill1 != null ? EffectiveRange(skill1) : 0f;
            return range > 0f ? range : k_DefaultAttackRange;
        }

        /// <summary>
        /// An action's reach. Projectile actions leave <c>Config.Range</c> at zero and carry the
        /// real distance on the projectile, so read that instead — otherwise every ranged bot
        /// would think it had no reach at all and try to walk into melee.
        /// </summary>
        static float EffectiveRange(Action skill)
        {
            if (skill.Config.Range > 0f)
            {
                return skill.Config.Range;
            }

            float best = 0f;
            var projectiles = skill.Config.Projectiles;
            if (projectiles != null)
            {
                foreach (var projectile in projectiles)
                {
                    best = Mathf.Max(best, projectile.Range);
                }
            }

            return best;
        }
    }

    static class BotVectorExtensions
    {
        /// <summary>Flattens a vector onto the ground plane.</summary>
        public static Vector3 WithY(this Vector3 value, float y)
        {
            value.y = y;
            return value;
        }
    }
}
