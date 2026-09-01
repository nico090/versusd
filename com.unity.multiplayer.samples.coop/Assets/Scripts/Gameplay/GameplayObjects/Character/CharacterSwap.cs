using System;
using System.Collections.Generic;
using Unity.BossRoom.Gameplay.GameplayObjects.AnimationCallbacks;
using UnityEngine;

namespace Unity.BossRoom.Gameplay.GameplayObjects.Character
{
    /// <summary>
    /// Responsible for storing of all of the pieces of a character, and swapping the pieces out en masse when asked to.
    /// </summary>
    public class CharacterSwap : MonoBehaviour
    {
        [System.Serializable]
        public class CharacterModelSet
        {
            public GameObject ears;
            public GameObject head;
            public GameObject mouth;
            public GameObject hair;
            public GameObject eyes;
            public GameObject torso;
            public GameObject gearRightHand;
            public GameObject gearLeftHand;
            public GameObject handRight;
            public GameObject handLeft;
            public GameObject shoulderRight;
            public GameObject shoulderLeft;
            public GameObject handSocket;
            public AnimatorTriggeredSpecialFX specialFx;
            public AnimatorOverrideController animatorOverrides; // references a separate stand-alone object in the project
            private List<Renderer> m_CachedRenderers;

            static readonly List<Renderer> s_RendererScratch = new List<Renderer>();

            public void SetFullActive(bool isActive)
            {
                ears.SetActive(isActive);
                head.SetActive(isActive);
                mouth.SetActive(isActive);
                hair.SetActive(isActive);
                eyes.SetActive(isActive);
                torso.SetActive(isActive);
                gearLeftHand.SetActive(isActive);
                gearRightHand.SetActive(isActive);
                handRight.SetActive(isActive);
                handLeft.SetActive(isActive);
                shoulderRight.SetActive(isActive);
                shoulderLeft.SetActive(isActive);
            }

            public List<Renderer> GetAllBodyParts()
            {
                if (m_CachedRenderers == null)
                {
                    m_CachedRenderers = new List<Renderer>();
                    AddRenderer(ref m_CachedRenderers, ears);
                    AddRenderer(ref m_CachedRenderers, head);
                    AddRenderer(ref m_CachedRenderers, mouth);
                    AddRenderer(ref m_CachedRenderers, hair);
                    AddRenderer(ref m_CachedRenderers, eyes);
                    AddRenderer(ref m_CachedRenderers, torso);
                    AddRenderer(ref m_CachedRenderers, gearRightHand);
                    AddRenderer(ref m_CachedRenderers, gearLeftHand);
                    AddRenderer(ref m_CachedRenderers, handRight);
                    AddRenderer(ref m_CachedRenderers, handLeft);
                    AddRenderer(ref m_CachedRenderers, shoulderRight);
                    AddRenderer(ref m_CachedRenderers, shoulderLeft);
                }
                return m_CachedRenderers;
            }

            private void AddRenderer(ref List<Renderer> rendererList, GameObject bodypartGO)
            {
                if (!bodypartGO) { return; }

                // we grab the renderers of the children too: some body parts (like the swapped-out heads) are
                // authored as a child object of the original body part, so a plain GetComponent would miss them.
                bodypartGO.GetComponentsInChildren<Renderer>(true, s_RendererScratch);
                foreach (var bodyPartRenderer in s_RendererScratch)
                {
                    // only mesh-based renderers: skip trails/particles/etc. that may be parented under a body part
                    if (bodyPartRenderer is MeshRenderer || bodyPartRenderer is SkinnedMeshRenderer)
                    {
                        if (!rendererList.Contains(bodyPartRenderer))
                        {
                            rendererList.Add(bodyPartRenderer);
                        }
                    }
                }
            }

        }

        [SerializeField]
        CharacterModelSet m_CharacterModel;

        public CharacterModelSet CharacterModel => m_CharacterModel;

        /// <summary>
        /// Reference to our shared-characters' animator.
        /// Can be null, but if so, animator overrides are not supported!
        /// </summary>
        [SerializeField]
        private Animator m_Animator;

        /// <summary>
        /// Reference to the original controller in our Animator.
        /// We switch back to this whenever we don't have an Override.
        /// </summary>
        private RuntimeAnimatorController m_OriginalController;

        [SerializeField]
        [Tooltip("Special Material we plug in when the local player is \"stealthy\"")]
        private Material m_StealthySelfMaterial;

        [SerializeField]
        [Tooltip("Special Material we plug in when another player is \"stealthy\"")]
        private Material m_StealthyOtherMaterial;

        public enum SpecialMaterialMode
        {
            None,
            StealthySelf,
            StealthyOther,
        }

        /// <summary>
        /// When we swap all our Materials out for a special material,
        /// we keep the old references here, so we can swap them back.
        /// </summary>
        private Dictionary<Renderer, Material[]> m_OriginalMaterials = new Dictionary<Renderer, Material[]>();

        ClientCharacter m_ClientCharacter;

        void Awake()
        {
            // Careful: this same graphics prefab is also instantiated bare in the character-select
            // screen (see ClientCharSelectState.GetCharacterGraphics), where there is no
            // ClientCharacter above us. Everything below has to survive that.
            m_ClientCharacter = GetComponentInParent<ClientCharacter>();

            if (m_ClientCharacter != null && m_ClientCharacter.OurAnimator != null)
            {
                m_Animator = m_ClientCharacter.OurAnimator;
            }
            else if (m_Animator == null)
            {
                // Standalone preview (or a prefab whose Animator wasn't wired in the inspector):
                // fall back to whatever Animator lives on us or below us.
                m_Animator = GetComponentInChildren<Animator>();
            }

            if (m_Animator != null)
            {
                m_OriginalController = m_Animator.runtimeAnimatorController;
            }
        }

        private void OnDisable()
        {
            // It's important that the original Materials that we pulled out of the renderers are put back.
            // Otherwise nothing will Destroy() them and they will leak! (Alternatively we could manually
            // Destroy() these in our OnDestroy(), but in this case it makes more sense just to put them back.)
            ClearOverrideMaterial();
        }

        /// <summary>
        /// Swap the visuals of the character to the index passed in.
        /// </summary>
        /// <param name="specialMaterialMode">Special Material to apply to all body parts</param>
        public void SwapToModel(SpecialMaterialMode specialMaterialMode = SpecialMaterialMode.None)
        {
            ClearOverrideMaterial();

            if (m_CharacterModel.specialFx)
            {
                m_CharacterModel.specialFx.enabled = true;
            }

            if (m_Animator)
            {
                // plug in the correct animator override... or plug the original non - overridden version back in!
                if (m_CharacterModel.animatorOverrides)
                {
                    m_Animator.runtimeAnimatorController = m_CharacterModel.animatorOverrides;
                }
                else
                {
                    m_Animator.runtimeAnimatorController = m_OriginalController;
                }
            }

            // lastly, now that we're all assembled, apply any override material.
            switch (specialMaterialMode)
            {
                case SpecialMaterialMode.StealthySelf:
                    SetOverrideMaterial(m_StealthySelfMaterial);
                    break;
                case SpecialMaterialMode.StealthyOther:
                    SetOverrideMaterial(m_StealthyOtherMaterial);
                    break;
            }
        }

        private void ClearOverrideMaterial()
        {
            foreach (var entry in m_OriginalMaterials)
            {
                if (entry.Key)
                {
                    entry.Key.sharedMaterials = entry.Value;
                }
            }
            m_OriginalMaterials.Clear();
        }

        private void SetOverrideMaterial(Material overrideMaterial)
        {
            ClearOverrideMaterial(); // just sanity-checking; this should already have been called!
            foreach (var bodyPart in m_CharacterModel.GetAllBodyParts())
            {
                if (bodyPart)
                {
                    // we swap every material slot, not just the first one: some models (like the swapped-in heads)
                    // use several submeshes, and any slot we skipped would stay fully visible while stealthed.
                    var originalMaterials = bodyPart.sharedMaterials;
                    m_OriginalMaterials[bodyPart] = originalMaterials;

                    var overrideMaterials = new Material[originalMaterials.Length];
                    for (int i = 0; i < overrideMaterials.Length; ++i)
                    {
                        overrideMaterials[i] = overrideMaterial;
                    }
                    bodyPart.sharedMaterials = overrideMaterials;
                }
            }
        }
    }
}
