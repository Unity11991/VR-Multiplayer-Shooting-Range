using UnityEngine;
using UnityEngine.UI;
using ShootingRange.Core;

namespace ShootingRange.UI
{
    /// <summary>
    /// Controller ray pointer for interacting with world-space UI and physical poke buttons.
    /// In VR, casts from the physical controller.
    /// In Desktop/Simulator mode, smoothly converges from the hand towards the crosshair gaze point.
    /// Casts an aiming ray and triggers clicks on Canvas buttons, toggles, sliders, and VRPokeInteractable objects.
    /// </summary>
    public class VRRayPointer : MonoBehaviour
    {
        [Header("Setup")]
        [SerializeField] private Handedness _hand = Handedness.Right;
        [SerializeField] private float _maxRayDistance = 6.0f;
        [SerializeField] private LayerMask _uiLayer = ~0;
        [SerializeField] private LineRenderer _lineRenderer;

        private IControllerInputProvider _input;

        private void Awake()
        {
            if (_lineRenderer == null)
            {
                _lineRenderer = gameObject.AddComponent<LineRenderer>();
                _lineRenderer.startWidth = 0.008f;
                _lineRenderer.endWidth = 0.003f;
                _lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
                _lineRenderer.startColor = new Color(0.2f, 0.85f, 1f, 0.90f);
                _lineRenderer.endColor = new Color(0.1f, 0.70f, 1f, 0.20f);
            }
        }

        private void Update()
        {
            if (_input == null && ShootingRangeCompositionRoot.Container != null)
            {
                ShootingRangeCompositionRoot.Container.TryResolve(out _input);
            }

            // If in desktop mode and holding a weapon with either hand, or in VR holding with this hand, hide the ray pointer line
            bool isDesktopMode = !UnityEngine.XR.XRSettings.isDeviceActive;
            var interactor = GetComponent<IHandInteractor>();
            bool isHolding = interactor != null && interactor.IsHoldingObject;

            if (isDesktopMode)
            {
                var interactors = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var mb in interactors)
                {
                    if (mb is IHandInteractor hi && hi.IsHoldingObject)
                    {
                        isHolding = true;
                        break;
                    }
                }
            }
            else if (!isHolding)
            {
                var twoHandedRifles = FindObjectsByType<ShootingRange.Interaction.TwoHandedRifleGrip>(FindObjectsSortMode.None);
                foreach (var rifle in twoHandedRifles)
                {
                    if (rifle != null && rifle.IsSecondaryGrabbed && rifle.SecondaryHand == _hand)
                    {
                        isHolding = true;
                        break;
                    }
                }
            }

            if (isHolding)
            {
                if (_lineRenderer != null) _lineRenderer.enabled = false;
                return;
            }

            if (_lineRenderer != null) _lineRenderer.enabled = true;

            Vector3 origin = transform.position;
            Vector3 direction = transform.forward;

            // In desktop simulator mode (or when no XR headset is active), converge hand rays towards camera crosshair gaze
            var cam = Camera.main;
            if (isDesktopMode && cam != null)
            {
                Ray camGaze = new Ray(cam.transform.position, cam.transform.forward);
                Vector3 gazeTarget = cam.transform.position + cam.transform.forward * _maxRayDistance;
                if (Physics.Raycast(camGaze, out RaycastHit camHit, _maxRayDistance, _uiLayer))
                {
                    gazeTarget = camHit.point;
                }
                direction = (gazeTarget - origin).normalized;
            }

            Vector3 endPos = origin + direction * _maxRayDistance;

            bool triggerClicked = (_input != null && _input.GetTriggerDown(_hand))
                               || (isDesktopMode && (UnityEngine.Input.GetMouseButtonDown(0) || UnityEngine.Input.GetKeyDown(KeyCode.E)));
            bool triggerHeld = (_input != null && _input.GetTrigger(_hand) > 0.5f)
                            || (isDesktopMode && UnityEngine.Input.GetMouseButton(0));

            Ray ray = new Ray(origin, direction);
            if (Physics.Raycast(ray, out RaycastHit hit, _maxRayDistance, _uiLayer))
            {
                endPos = hit.point;

                // 1. Check for physical 3D Poke Button
                var poke = hit.collider.GetComponentInParent<VRPokeInteractable>();
                if (poke != null)
                {
                    if (triggerClicked)
                    {
                        poke.SimulateClick();
                        _input?.TriggerHaptic(_hand, 0.5f, 0.05f);
                    }
                    if (_lineRenderer != null)
                    {
                        _lineRenderer.SetPosition(0, origin);
                        _lineRenderer.SetPosition(1, endPos);
                    }
                    return;
                }

                // 2. Check for uGUI Canvas Button
                var button = hit.collider.GetComponentInParent<Button>();
                if (button != null && button.interactable)
                {
                    if (triggerClicked)
                    {
                        button.onClick.Invoke();
                        _input?.TriggerHaptic(_hand, 0.4f, 0.04f);
                    }
                    if (_lineRenderer != null)
                    {
                        _lineRenderer.SetPosition(0, origin);
                        _lineRenderer.SetPosition(1, endPos);
                    }
                    return;
                }

                // 3. Check for uGUI Slider
                var slider = hit.collider.GetComponentInParent<Slider>();
                if (slider != null && slider.interactable)
                {
                    if (triggerHeld)
                    {
                        RectTransform sliderRect = slider.GetComponent<RectTransform>();
                        Vector3 localHit = sliderRect.InverseTransformPoint(hit.point);
                        float width = sliderRect.rect.width;
                        if (width > 0.001f)
                        {
                            float norm = Mathf.Clamp01((localHit.x + width * 0.5f) / width);
                            slider.value = Mathf.Lerp(slider.minValue, slider.maxValue, norm);
                            _input?.TriggerHaptic(_hand, 0.2f, 0.02f);
                        }
                    }
                    if (_lineRenderer != null)
                    {
                        _lineRenderer.SetPosition(0, origin);
                        _lineRenderer.SetPosition(1, endPos);
                    }
                    return;
                }

                // 4. Check for uGUI Toggle
                var toggle = hit.collider.GetComponentInParent<Toggle>();
                if (toggle != null && toggle.interactable)
                {
                    if (triggerClicked)
                    {
                        toggle.isOn = !toggle.isOn;
                        _input?.TriggerHaptic(_hand, 0.35f, 0.03f);
                    }
                    if (_lineRenderer != null)
                    {
                        _lineRenderer.SetPosition(0, origin);
                        _lineRenderer.SetPosition(1, endPos);
                    }
                    return;
                }
            }

            if (_lineRenderer != null)
            {
                _lineRenderer.SetPosition(0, origin);
                _lineRenderer.SetPosition(1, endPos);
            }
        }
    }
}
