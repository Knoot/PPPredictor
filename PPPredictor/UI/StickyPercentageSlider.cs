using System;
using BeatSaberMarkupLanguage.Components.Settings;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PPPredictor.UI
{
    internal sealed class StickyPercentageSlider : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private SliderSetting setting;
        private Func<float> getValue;
        private Action<float> applyValue;
        private bool pressed, pending;
        private float current, target, pressedAt;

        internal void Initialize(SliderSetting slider, Func<float> readValue, Action<float> writeValue)
        {
            setting = slider;
            getValue = readValue;
            applyValue = writeValue;
            current = target = readValue();
        }

        internal void SetTarget(float value)
        {
            if (setting == null || float.IsNaN(value) || float.IsInfinity(value)) return;
            target = Mathf.Clamp(value, setting.Slider.minValue, setting.Slider.maxValue);
            pending = true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (setting == null || !setting.Slider.IsInteractable() || eventData.button != PointerEventData.InputButton.Left) return;
            current = getValue();
            // The native slider may deliver its value change before this pointer handler.
            if (!pending) target = current;
            pressed = true;
            pressedAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) CancelDrag();
        }

        private void LateUpdate()
        {
            if (setting == null || applyValue == null || (!pressed && !pending)) return;
            if (!setting.Slider.IsInteractable()) { CancelDrag(); return; }
            if (pressed)
            {
                float ramp = Mathf.Clamp01((Time.unscaledTime - pressedAt) / 2f);
                float blend = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime * ramp);
                current = Mathf.Lerp(current, target, blend);
            }
            else
            {
                current = target; // Native arrow clicks retain their exact discrete step.
            }
            pending = false;
            applyValue(current);
        }

        internal void CancelDrag()
        {
            bool wasChanging = pressed || pending;
            pressed = pending = false;
            if (getValue == null) return;
            current = target = getValue();
            if (wasChanging && setting != null) applyValue?.Invoke(current);
        }

        internal void Detach()
        {
            CancelDrag();
            setting = null;
            getValue = null;
            applyValue = null;
        }

        private void OnDisable() => CancelDrag();
        private void OnDestroy() => Detach();
    }
}
