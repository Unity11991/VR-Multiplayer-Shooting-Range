using System;
using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Targets
{
    public abstract class TargetEntity : MonoBehaviour, ITargetEntity
    {
        [Header("Target Health & Scoring")]
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private float _currentHealth = 100f;
        [SerializeField] private float _resetDelaySeconds = 3.5f;

        [Header("Physical Hit & Knockdown Reaction")]
        [SerializeField] protected Transform _visualMeshTransform;
        [SerializeField] protected float _maxWobbleDegrees = 14.0f;
        [SerializeField] protected float _knockdownAngleTarget = 85.0f;
        [SerializeField] protected float _knockdownSpeed = 320.0f;

        [Header("Target State")]
        [SerializeField] protected int _targetId;
        [SerializeField] protected bool _isActive = true;
        [SerializeField] protected bool _isDestroyed = false;
        [SerializeField] protected bool _isNetworkControlled = false;
        [SerializeField] protected Vector3 _spawnPosition;
        [SerializeField] protected Quaternion _spawnRotation = Quaternion.identity;

        // Network synchronization targets for client-side interpolation
        private Vector3 _networkTargetPos;
        private Quaternion _networkTargetRot = Quaternion.identity;

        // Physical spring-damper wobble
        private float _wobbleTimer = 99f;
        private float _wobbleImpulse;
        private Vector3 _wobbleAxis = Vector3.right;

        // Knockdown angle
        protected float _currentKnockdownAngle;
        protected float _targetKnockdownAngle;
        private float _resetTimer;

        // Cached original transform
        private Vector3 _originalMeshLocalPos;
        private Quaternion _originalMeshLocalRot = Quaternion.identity;

        // Cached colliders & renderers for zero-alloc reaction
        private Collider[] _colliders;
        private Renderer[] _renderers;
        private float _hitFlashTimer;

        private static MaterialPropertyBlock s_PropBlock;
        private static readonly int s_EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public int TargetId => _targetId;
        public bool IsActive => _isActive;
        public Transform Transform => transform;
        public float Health => _currentHealth;
        public float MaxHealth => _maxHealth;
        public bool IsDestroyed => _isDestroyed;
        public bool IsNetworkControlled
        {
            get => _isNetworkControlled;
            set => _isNetworkControlled = value;
        }

        public event Action<ITargetEntity, float, HitZone, ulong> OnTargetHit;
        public event Action<ITargetEntity, ulong> OnTargetDestroyed;

        protected virtual void Awake()
        {
            if (_visualMeshTransform == null) _visualMeshTransform = transform;
            _originalMeshLocalPos = _visualMeshTransform.localPosition;
            _originalMeshLocalRot = _visualMeshTransform.localRotation;
            _currentHealth = _maxHealth;

            _colliders = GetComponentsInChildren<Collider>(true);
            _renderers = GetComponentsInChildren<Renderer>(true);

            if (s_PropBlock == null)
            {
                s_PropBlock = new MaterialPropertyBlock();
            }

            // Ensure spawn pose is always cached from the scene transform
            if (_spawnPosition == Vector3.zero && transform.position != Vector3.zero)
            {
                _spawnPosition = transform.position;
                _spawnRotation = transform.rotation;
            }
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (_visualMeshTransform == null) _visualMeshTransform = transform;
            if (_spawnPosition == Vector3.zero && transform.position != Vector3.zero)
            {
                _spawnPosition = transform.position;
                _spawnRotation = transform.rotation;
            }
        }
#endif

        public virtual void Initialize(int id, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            _targetId = id;
            _spawnPosition = spawnPosition;
            _spawnRotation = spawnRotation;
            _networkTargetPos = spawnPosition;
            _networkTargetRot = spawnRotation;
            transform.position = spawnPosition;
            transform.rotation = spawnRotation;
            ResetTarget();
        }

        // Stepped strictly by TargetManager. Zero per-target Update() calls!
        public virtual void Step(float deltaTime)
        {
            if (!_isActive) return;

            // 1. Physical Spring-Damper Wobble
            float currentWobble = 0f;
            if (_wobbleTimer < 1.0f)
            {
                _wobbleTimer += deltaTime;
                currentWobble = _wobbleImpulse * Mathf.Sin(_wobbleTimer * 24.0f) * Mathf.Exp(-_wobbleTimer * 7.5f);
            }

            // 2. Knockdown / Stand-up Transition
            _currentKnockdownAngle = Mathf.MoveTowards(_currentKnockdownAngle, _targetKnockdownAngle, deltaTime * _knockdownSpeed);

            // Apply combined rotational reaction to visual mesh
            if (_visualMeshTransform != null)
            {
                Quaternion wobbleRot = Quaternion.AngleAxis(currentWobble + _currentKnockdownAngle, _wobbleAxis);
                _visualMeshTransform.localRotation = _originalMeshLocalRot * wobbleRot;
            }

            // 3. Material Hit Flash Decay via MaterialPropertyBlock (Zero GC allocations!)
            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= deltaTime;
                float flashIntensity = Mathf.Clamp01(_hitFlashTimer * 8.0f);
                Color flashColor = Color.white * (flashIntensity * 1.8f);

                if (_renderers != null)
                {
                    for (int i = 0; i < _renderers.Length; i++)
                    {
                        if (_renderers[i] != null)
                        {
                            _renderers[i].GetPropertyBlock(s_PropBlock);
                            s_PropBlock.SetColor(s_EmissionColorId, flashColor);
                            _renderers[i].SetPropertyBlock(s_PropBlock);
                        }
                    }
                }
            }

            // 4. Handle Destroyed State & In-Place Reset Timer
            if (_isDestroyed)
            {
                // Only host/offline runs the autonomous reset timer; network clients follow server's state
                if (!_isNetworkControlled)
                {
                    _resetTimer -= deltaTime;
                    if (_resetTimer <= 0f)
                    {
                        ResetTarget();
                    }
                }
                return; // Do not execute movement while downed
            }

            // 5. Execute Custom Target Movement or Interpolate Server-Authoritative State
            if (_isNetworkControlled)
            {
                if (_networkTargetPos != Vector3.zero)
                {
                    transform.position = Vector3.Lerp(transform.position, _networkTargetPos, deltaTime * 25.0f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, _networkTargetRot, deltaTime * 25.0f);
                }
            }
            else
            {
                StepMovement(deltaTime);
            }
        }

        protected abstract void StepMovement(float deltaTime);

        public virtual void ApplyDamage(float amount, HitZone zone, Vector3 hitPoint, Vector3 hitNormal, ulong attackerClientId)
        {
            if (!_isActive || _isDestroyed) return;

            _currentHealth -= amount;

            // Physical reaction: compute tilt axis perpendicular to impact normal
            Vector3 cross = Vector3.Cross(transform.up, -hitNormal).normalized;
            _wobbleAxis = cross.sqrMagnitude > 0.05f ? cross : transform.right;
            _wobbleImpulse = Mathf.Clamp(amount * 0.16f, 3.5f, _maxWobbleDegrees);
            _wobbleTimer = 0f;

            // Hit flash
            _hitFlashTimer = 0.12f;

            OnTargetHit?.Invoke(this, amount, zone, attackerClientId);

            if (_currentHealth <= 0f)
            {
                _currentHealth = 0f;
                _isDestroyed = true;
                _resetTimer = _resetDelaySeconds;
                OnDestroyedReaction();
                OnTargetDestroyed?.Invoke(this, attackerClientId);
            }
        }

        public virtual void ApplySynchronizedState(float health, bool isDestroyed, Vector3 position)
        {
            ApplySynchronizedState(health, isDestroyed, position, transform.rotation);
        }

        public virtual void ApplySynchronizedState(float health, bool isDestroyed, Vector3 position, Quaternion rotation)
        {
            _currentHealth = health;
            if (isDestroyed && !_isDestroyed)
            {
                _isDestroyed = true;
                OnDestroyedReaction();
            }
            else if (!isDestroyed && _isDestroyed)
            {
                ResetTarget();
                _currentHealth = health;
            }

            if (position != Vector3.zero)
            {
                _networkTargetPos = position;
                _networkTargetRot = rotation;

                // Snap immediately if distance error is large (> 2.5m, e.g. on initial spawn or teleport)
                if (Vector3.Distance(transform.position, position) > 2.5f)
                {
                    transform.position = position;
                    transform.rotation = rotation;
                }
            }
        }

        public virtual void TriggerNetworkHitReaction(float amount, HitZone zone, Vector3 hitPoint)
        {
            Vector3 hitNormal = (hitPoint - transform.position).normalized;
            Vector3 cross = Vector3.Cross(transform.up, -hitNormal).normalized;
            _wobbleAxis = cross.sqrMagnitude > 0.05f ? cross : transform.right;
            _wobbleImpulse = Mathf.Clamp(amount * 0.16f, 3.5f, _maxWobbleDegrees);
            _wobbleTimer = 0f;

            _hitFlashTimer = 0.12f;
        }

        protected virtual void OnDestroyedReaction()
        {
            // Knockdown: fold down and disable hit colliders while downed
            _targetKnockdownAngle = _knockdownAngleTarget;
            SetCollidersActive(false);
        }

        public virtual void ResetTarget()
        {
            _currentHealth = _maxHealth;
            _isDestroyed = false;
            _isActive = true;
            _wobbleTimer = 99f;
            _targetKnockdownAngle = 0f;
            _currentKnockdownAngle = 0f;

            SetCollidersActive(true);

            if (_visualMeshTransform != null)
            {
                _visualMeshTransform.localPosition = _originalMeshLocalPos;
                _visualMeshTransform.localRotation = _originalMeshLocalRot;
            }

            // Clear any lingering hit flash
            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null)
                    {
                        _renderers[i].GetPropertyBlock(s_PropBlock);
                        s_PropBlock.SetColor(s_EmissionColorId, Color.black);
                        _renderers[i].SetPropertyBlock(s_PropBlock);
                    }
                }
            }

            if (_spawnPosition != Vector3.zero)
            {
                transform.position = _spawnPosition;
                transform.rotation = _spawnRotation;
            }
        }

        protected void SetCollidersActive(bool enabled)
        {
            if (_colliders == null) return;
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                {
                    _colliders[i].enabled = enabled;
                }
            }
        }
    }
}

