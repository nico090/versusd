using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Unity.BossRoom.Gameplay.UI
{
    /// <summary>
    /// Provides logic for a UI HUD Button to slightly shrink scale on pointer down.
    /// Also has an optional code interface for receiving notifications about down/up events (instead of just on-click)
    /// </summary>
    public class UIHUDButton : Button
    {
        // We apply a uniform 95% scale to buttons when pressed
        static readonly Vector3 k_DownScale = new Vector3(0.95f, 0.95f, 0.95f);

        /// <summary>
        /// Called when the user clicks down on the button (but hasn't released the button yet)
        /// </summary>
        public Action OnPointerDownEvent;

        /// <summary>
        /// Called when the user clicks up on the button (completing a click event)
        /// </summary>
        public Action OnPointerUpEvent;

        // Radial "shadow" that covers the icon while its action is on cooldown, so it's obvious
        // at a glance whether a power can be used. Built at runtime (not part of the prefab) so
        // existing button prefabs don't need manual editing.
        Image m_CooldownOverlay;
        Coroutine m_CooldownRoutine;

        void EnsureCooldownOverlay()
        {
            if (m_CooldownOverlay != null)
            {
                return;
            }

            var overlayObject = new GameObject("CooldownOverlay", typeof(RectTransform), typeof(Image));
            var overlayTransform = (RectTransform)overlayObject.transform;
            overlayTransform.SetParent(transform, false);
            overlayTransform.anchorMin = Vector2.zero;
            overlayTransform.anchorMax = Vector2.one;
            overlayTransform.offsetMin = Vector2.zero;
            overlayTransform.offsetMax = Vector2.zero;
            overlayTransform.SetAsLastSibling();

            m_CooldownOverlay = overlayObject.GetComponent<Image>();
            m_CooldownOverlay.color = new Color(0f, 0f, 0f, 0.75f);
            m_CooldownOverlay.type = Image.Type.Filled;
            m_CooldownOverlay.fillMethod = Image.FillMethod.Radial360;
            m_CooldownOverlay.fillOrigin = (int)Image.Origin360.Top;
            m_CooldownOverlay.fillClockwise = false;
            m_CooldownOverlay.fillAmount = 0f;
            m_CooldownOverlay.raycastTarget = false;
        }

        /// <summary>
        /// Shows a radial shadow over the button's icon that sweeps away over <paramref name="duration"/>
        /// seconds, so it's clear when the power is still on cooldown vs. ready to use again.
        /// </summary>
        public void StartCooldown(float duration)
        {
            EnsureCooldownOverlay();

            if (m_CooldownRoutine != null)
            {
                StopCoroutine(m_CooldownRoutine);
                m_CooldownRoutine = null;
            }

            // Match the overlay's shape to whatever icon is currently on the button.
            m_CooldownOverlay.sprite = image != null ? image.sprite : null;

            if (duration <= 0f)
            {
                m_CooldownOverlay.fillAmount = 0f;
                return;
            }

            m_CooldownOverlay.fillAmount = 1f;
            m_CooldownRoutine = StartCoroutine(RunCooldown(duration));
        }

        IEnumerator RunCooldown(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                m_CooldownOverlay.fillAmount = Mathf.Clamp01(1f - elapsed / duration);
                yield return null;
            }

            m_CooldownOverlay.fillAmount = 0f;
            m_CooldownRoutine = null;
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable()) { return; }
            base.OnPointerDown(eventData);
            transform.localScale = k_DownScale;
            OnPointerDownEvent?.Invoke();
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            // NOTE: no IsInteractable() gate here, unlike OnPointerDown. A button can stop being
            // interactable *while* it is held down (cooldown starting, the action swapping out),
            // and swallowing the release then left charge-up skills — the Tank's Shield Aura in
            // particular — charging forever, which locks out every other input.
            base.OnPointerUp(eventData);
            transform.localScale = Vector3.one;
            OnPointerUpEvent?.Invoke();
        }
    }
}

