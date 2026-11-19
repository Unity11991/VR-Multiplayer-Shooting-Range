using System;
using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Interaction
{
    public class VRHandInteractor : MonoBehaviour, IHandInteractor
    {
        [Header("Hand Setup")]
        [SerializeField] private Handedness _hand = Handedness.Right;
        [SerializeField] private Transform _attachTransform;
        [SerializeField] private float _grabRadius = 0.25f;
        [SerializeField] private float _extendedReachMeters = 3.5f;
        [SerializeField] private LayerMask _interactableLayer = ~0;

        [Header("Debug")]
        [SerializeField] private bool _drawGizmos = true;

        private ITrackingProvider _tracking;
        private IControllerInputProvider _input;
        private IGrabbable _heldObject;
        private TwoHandedRifleGrip _heldSecondaryRifle;
        private VRHandInteractor _otherHandInteractor;

        private readonly Collider[] _overlapColliders = new Collider[32];

        public Handedness Hand => _hand;
        public Transform AttachTransform => _attachTransform != null ? _attachTransform : transform;
        public IGrabbable HeldObject => _heldObject;
        public bool IsHoldingObject => _heldObject != null;

        public event Action<IGrabbable> OnGrabbedObject;
        public event Action<IGrabbable> OnReleasedObject;

        private void Start()
        {
            if (_attachTransform == null) _attachTransform = transform;
            FindOtherHand();
            ResolveDependencies();
        }

        private void FindOtherHand()
        {
            var interactors = FindObjectsByType<VRHandInteractor>(FindObjectsSortMode.None);
            foreach (var interactor in interactors)
            {
                if (interactor != this && interactor.Hand != _hand)
                {
                    _otherHandInteractor = interactor;
                    break;
                }
            }
        }

        public void SetDependencies(ITrackingProvider tracking, IControllerInputProvider input)
        {
            _tracking = tracking;
            _input = input;
        }

        private void Update()
        {
            ResolveDependencies();
            if (_heldObject != null && !_heldObject.IsGrabbed)
            {
                _heldObject = null;
            }

            if (_input == null || _tracking == null) return;

            bool gripDown = _input.GetGripDown(_hand);
            bool gripUp = _input.GetGripUp(_hand);

            if (gripDown)
            {
                TryGrabNearest();
            }
            else if (gripUp)
            {
                TryRelease();
            }
        }

        private void ResolveDependencies()
        {
            if (ShootingRangeCompositionRoot.Container != null)
            {
                if (_tracking == null) ShootingRangeCompositionRoot.Container.TryResolve(out _tracking);
                if (_input == null) ShootingRangeCompositionRoot.Container.TryResolve(out _input);
            }

            // Direct fallbacks if container not yet resolved
            if (_tracking == null)
            {
                _tracking = GetComponentInParent<ITrackingProvider>();
                if (_tracking == null)
                {
                    var provs = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                    foreach (var p in provs)
                    {
                        if (p is ITrackingProvider tp) { _tracking = tp; break; }
                    }
                }
            }

            if (_input == null)
            {
                _input = GetComponentInParent<IControllerInputProvider>();
                if (_input == null)
                {
                    var provs = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                    foreach (var p in provs)
                    {
                        if (p is IControllerInputProvider ip) { _input = ip; break; }
                    }
                }
            }

            if (_otherHandInteractor == null)
            {
                FindOtherHand();
            }
        }

        public void TryGrabNearest()
        {
            if (_heldObject != null || _heldSecondaryRifle != null) return;

            Vector3 searchPos = AttachTransform.position;
            int count = Physics.OverlapSphereNonAlloc(searchPos, _grabRadius, _overlapColliders, _interactableLayer, QueryTriggerInteraction.Ignore);

            IGrabbable bestGrabbable = null;
            TwoHandedRifleGrip bestSecondaryRifle = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var col = _overlapColliders[i];
                if (col == null) continue;

                // Check for two-handed foregrip grab first
                var rifle = col.GetComponentInParent<TwoHandedRifleGrip>();
                if (rifle != null && rifle.IsGrabbed && rifle.CurrentGrabbingHand != _hand && !rifle.IsSecondaryGrabbed)
                {
                    if (rifle.SecondaryGrabPoint != null && rifle.SecondaryGrabPoint.CanBeGrabbedBy(_hand))
                    {
                        float distToSecondary = Vector3.Distance(searchPos, rifle.SecondaryGrabPoint.transform.position);
                        if (distToSecondary <= rifle.SecondaryGrabPoint.GrabRadius && distToSecondary < bestDistance)
                        {
                            bestSecondaryRifle = rifle;
                            bestDistance = distToSecondary;
                            continue;
                        }
                    }
                }

                // Check for primary grabbable
                var grabbable = col.GetComponentInParent<IGrabbable>();
                if (grabbable != null && !grabbable.IsGrabbed)
                {
                    float dist = Vector3.Distance(searchPos, grabbable.Transform.position);
                    if (dist < bestDistance)
                    {
                        bestGrabbable = grabbable;
                        bestSecondaryRifle = null;
                        bestDistance = dist;
                    }
                }
            }

            // Extended reach check: If nothing in close hand radius, cast a wide forward cone from camera/hand
            if (bestGrabbable == null && bestSecondaryRifle == null)
            {
                Vector3 rayOrigin = searchPos;
                Vector3 rayDir = AttachTransform.forward;
                if (Camera.main != null)
                {
                    rayOrigin = Camera.main.transform.position;
                    rayDir = Camera.main.transform.forward;
                }

                var hits = Physics.SphereCastAll(rayOrigin, 0.35f, rayDir, _extendedReachMeters, _interactableLayer, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                foreach (var hit in hits)
                {
                    var rifle = hit.collider.GetComponentInParent<TwoHandedRifleGrip>();
                    if (rifle != null && rifle.IsGrabbed && rifle.CurrentGrabbingHand != _hand && !rifle.IsSecondaryGrabbed)
                    {
                        bestSecondaryRifle = rifle;
                        break;
                    }

                    var grabbable = hit.collider.GetComponentInParent<IGrabbable>();
                    if (grabbable != null && !grabbable.IsGrabbed)
                    {
                        bestGrabbable = grabbable;
                        break;
                    }
                }
            }

            // Grab secondary foregrip if valid
            if (bestSecondaryRifle != null)
            {
                _heldSecondaryRifle = bestSecondaryRifle;
                _heldSecondaryRifle.OnSecondaryGrabbed(_hand, AttachTransform);
                _input?.TriggerHaptic(_hand, 0.4f, 0.05f);
                return;
            }

            // Grab primary object
            if (bestGrabbable != null)
            {
                GrabObject(bestGrabbable);
            }
        }

        public void GrabObject(IGrabbable grabbable)
        {
            if (grabbable == null) return;

            // Hand-to-hand transfer: if other hand holds it, transfer cleanly
            if (grabbable.IsGrabbed && grabbable.CurrentGrabbingHand.HasValue)
            {
                Handedness fromHand = grabbable.CurrentGrabbingHand.Value;
                if (_otherHandInteractor != null && _otherHandInteractor.HeldObject == grabbable)
                {
                    _otherHandInteractor.TransferOut();
                }
                grabbable.OnHandTransfer(fromHand, _hand, AttachTransform);
            }
            else
            {
                Vector3 handVel = _tracking != null ? _tracking.GetHandVelocity(_hand) : Vector3.zero;
                Vector3 handAngVel = _tracking != null ? _tracking.GetHandAngularVelocity(_hand) : Vector3.zero;
                grabbable.OnGrabbed(_hand, AttachTransform, handVel, handAngVel);
            }

            _heldObject = grabbable;
            _input?.TriggerHaptic(_hand, 0.6f, 0.08f);
            OnGrabbedObject?.Invoke(grabbable);
        }

        public void TransferOut()
        {
            var transferred = _heldObject;
            _heldObject = null;
            if (transferred != null)
            {
                OnReleasedObject?.Invoke(transferred);
            }
        }

        public void TryRelease()
        {
            if (_heldSecondaryRifle != null)
            {
                _heldSecondaryRifle.OnSecondaryReleased(_hand);
                _heldSecondaryRifle = null;
                _input?.TriggerHaptic(_hand, 0.3f, 0.03f);
                return;
            }

            if (_heldObject == null) return;

            Vector3 releaseVel = _tracking != null ? _tracking.GetHandVelocity(_hand) : Vector3.zero;
            Vector3 releaseAngVel = _tracking != null ? _tracking.GetHandAngularVelocity(_hand) : Vector3.zero;

            var released = _heldObject;
            _heldObject.OnReleased(_hand, releaseVel, releaseAngVel);
            _heldObject = null;

            _input?.TriggerHaptic(_hand, 0.3f, 0.03f);
            OnReleasedObject?.Invoke(released);
        }

        private void OnDrawGizmos()
        {
            if (!_drawGizmos) return;
            Gizmos.color = _hand == Handedness.Right ? Color.blue : Color.red;
            Vector3 pos = AttachTransform != null ? AttachTransform.position : transform.position;
            Gizmos.DrawWireSphere(pos, _grabRadius);
        }
    }
}
