using System;
using System.Collections.Generic;
using Unity.BossRoom.Gameplay.GameplayObjects;
using Unity.BossRoom.Gameplay.GameplayObjects.Character;
using Unity.BossRoom.Infrastructure;
using Unity.BossRoom.VisualEffects;
using Mirror;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.Actions
{
    public partial class MeleeAction
    {
        //have we actually played an impact? This won't necessarily happen for all swings. Sometimes you're just swinging at space.
        private bool m_ImpactPlayed;

        /// <summary>
        /// When we detect if our original target is still around, we use a bit of padding on the range check.
        /// </summary>
        private const float k_RangePadding = 3f;

        /// <summary>
        /// List of active special graphics playing on the target.
        /// </summary>
        private List<SpecialFXGraphic> m_SpawnedGraphics = null;

        public override bool OnStartClient(ClientCharacter clientCharacter)
        {
            base.OnStartClient(clientCharacter);
            // base.OnStartClient drives the swing animation on every client (needed on the
            // headless dedicated server, where the server-side NetworkAnimator trigger never
            // reaches clients).

            // we can optionally have special particles that should play on the target. If so, add them now.
            // (don't wait until impact, because the particles need to start sooner!)
            var targetNetworkObj = Data.TargetIds != null && Data.TargetIds.Length > 0
                ? NetworkIdentityUtils.FindByNetId((uint)Data.TargetIds[0])
                : null;
            if (targetNetworkObj != null)
            {
                float padRange = Config.Range + k_RangePadding;

                Vector3 targetPosition;
                if (PhysicsWrapper.TryGetPhysicsWrapper(Data.TargetIds[0], out var physicsWrapper))
                {
                    targetPosition = physicsWrapper.Transform.position;
                }
                else
                {
                    targetPosition = targetNetworkObj.transform.position;
                }

                if ((clientCharacter.transform.position - targetPosition).sqrMagnitude < (padRange * padRange))
                {
                    // target is in range! Play the graphics
                    m_SpawnedGraphics = InstantiateSpecialFXGraphics(physicsWrapper ? physicsWrapper.Transform : targetNetworkObj.transform, true);
                }
            }

            return true;
        }

        public override bool OnUpdateClient(ClientCharacter clientCharacter)
        {
            return ActionConclusion.Continue;
        }

        public override void OnAnimEventClient(ClientCharacter clientCharacter, string id)
        {
            if (id == "impact" && !m_ImpactPlayed)
            {
                PlayHitReact(clientCharacter);
            }
        }

        public override void EndClient(ClientCharacter clientCharacter)
        {
            //if this didn't already happen, make sure it gets a chance to run. This could have failed to run because
            //our animationclip didn't have the "impact" event properly configured (as one possibility).
            PlayHitReact(clientCharacter);
            base.EndClient(clientCharacter);
        }

        public override void CancelClient(ClientCharacter clientCharacter)
        {
            // if we had any special target graphics, tell them we're done
            if (m_SpawnedGraphics != null)
            {
                foreach (var spawnedGraphic in m_SpawnedGraphics)
                {
                    if (spawnedGraphic)
                    {
                        spawnedGraphic.Shutdown();
                    }
                }
            }
        }

        void PlayHitReact(ClientCharacter parent)
        {
            if (m_ImpactPlayed) { return; }

            m_ImpactPlayed = true;

            if (NetworkServer.active)
            {
                return;
            }

            // Everything below is a cosmetic flinch on somebody else's model. It runs from an
            // animation event, which means it fires a frame or two AFTER the swing was decided —
            // and the most common thing to swing at is something that is now dying. Every lookup
            // here can therefore come back holding a corpse, so each one is checked and the whole
            // thing gives up quietly rather than throwing. On desktop the throw was a red line in
            // the console; under IL2CPP on Android the same dereference closed the game.
            if (parent == null)
            {
                return;
            }

            //Is my original target still in range? Then definitely get him!
            if (Data.TargetIds != null && Data.TargetIds.Length > 0)
            {
                var targetNetworkObj = NetworkIdentityUtils.FindByNetId((uint)Data.TargetIds[0]);
                if (targetNetworkObj != null)
                {
                    float padRange = Config.Range + k_RangePadding;

                    // The wrapper registry can still hand back an entry for an object that has
                    // just been despawned — which is exactly what a killing blow produces — and its
                    // Transform is gone by the time this reads it. This was the crash.
                    Vector3 targetPosition;
                    if (PhysicsWrapper.TryGetPhysicsWrapper(Data.TargetIds[0], out var movementContainer)
                        && movementContainer != null && movementContainer.Transform != null)
                    {
                        targetPosition = movementContainer.Transform.position;
                    }
                    else
                    {
                        targetPosition = targetNetworkObj.transform.position;
                    }

                    if ((parent.transform.position - targetPosition).sqrMagnitude < (padRange * padRange))
                    {
                        // TryGetComponent, not GetComponent: a ClientCharacter whose identity has
                        // already been torn down returns null here, and .netId on it is the other
                        // way this method threw.
                        if (parent.TryGetComponent<NetworkIdentity>(out var parentIdentity)
                            && (ulong)(uint)targetNetworkObj.netId != (ulong)(uint)parentIdentity.netId)
                        {
                            string hitAnim = Config.ReactAnim;
                            if (string.IsNullOrEmpty(hitAnim)) { hitAnim = k_DefaultHitReact; }

                            if (targetNetworkObj.TryGetComponent<ServerCharacter>(out var serverCharacter)
                                && serverCharacter.clientCharacter != null
                                && serverCharacter.clientCharacter.OurAnimator)
                            {
                                serverCharacter.clientCharacter.OurAnimator.SetTrigger(hitAnim);
                            }
                        }
                    }
                }
            }

            //in the future we may do another physics check to handle the case where a target "ran under our weapon".
            //But for now, if the original target is no longer present, then we just don't play our hit react on anything.
        }
    }
}
