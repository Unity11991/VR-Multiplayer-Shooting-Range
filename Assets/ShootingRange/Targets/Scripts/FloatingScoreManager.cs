using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Targets
{
    /// <summary>
    /// High-performance, zero-allocation floating score points manager.
    /// Spawns crisp, floating damage/points indicators at bullet impact points that
    /// face the player camera, pop upward, and fade out smoothly over time.
    /// </summary>
    public class FloatingScoreManager : MonoBehaviour
    {
        public static FloatingScoreManager Instance { get; private set; }

        private const int PoolSize = 32;
        private readonly ScorePopupItem[] _pool = new ScorePopupItem[PoolSize];
        private int _poolIndex = 0;
        private Font _cachedFont;

        private class ScorePopupItem
        {
            public GameObject Root;
            public TextMesh Text;
            public TextMesh ShadowText;
            public Vector3 StartPos;
            public Vector3 Velocity;
            public float Timer;
            public float Lifetime;
            public Color MainColor;
            public float BaseScale;
            public bool IsActive;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            InitializePool();
        }

        private void InitializePool()
        {
            try { _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (_cachedFont == null) { try { _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }

            for (int i = 0; i < PoolSize; i++)
            {
                var root = new GameObject($"FloatingScore_{i}");
                root.transform.SetParent(transform, false);
                root.SetActive(false);

                // Dark shadow backing for high contrast against any background
                var shadowObj = new GameObject("Shadow");
                shadowObj.transform.SetParent(root.transform, false);
                shadowObj.transform.localPosition = new Vector3(0.002f, -0.002f, 0.002f);
                var shadowMesh = shadowObj.AddComponent<TextMesh>();
                shadowMesh.fontSize = 52;
                shadowMesh.characterSize = 0.024f;
                shadowMesh.fontStyle = FontStyle.Bold;
                shadowMesh.alignment = TextAlignment.Center;
                shadowMesh.anchor = TextAnchor.MiddleCenter;
                shadowMesh.color = new Color(0f, 0f, 0f, 0.95f);
                if (_cachedFont != null) shadowMesh.font = _cachedFont;

                // Main front text
                var mainObj = new GameObject("MainText");
                mainObj.transform.SetParent(root.transform, false);
                mainObj.transform.localPosition = Vector3.zero;
                var mainMesh = mainObj.AddComponent<TextMesh>();
                mainMesh.fontSize = 52;
                mainMesh.characterSize = 0.024f;
                mainMesh.fontStyle = FontStyle.Bold;
                mainMesh.alignment = TextAlignment.Center;
                mainMesh.anchor = TextAnchor.MiddleCenter;
                mainMesh.color = Color.white;
                if (_cachedFont != null) mainMesh.font = _cachedFont;

                _pool[i] = new ScorePopupItem
                {
                    Root = root,
                    Text = mainMesh,
                    ShadowText = shadowMesh,
                    IsActive = false
                };
            }
        }

        public static void SpawnScore(Vector3 worldPoint, HitZone zone, float damage)
        {
            EnsureInstance();
            Instance.SpawnScoreInternal(worldPoint, zone, damage);
        }

        public static void SpawnDestroyedBonus(Vector3 worldPoint)
        {
            EnsureInstance();
            Instance.SpawnDestroyedInternal(worldPoint);
        }

        private static void EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("FloatingScoreManager");
                Instance = go.AddComponent<FloatingScoreManager>();
            }
        }

        private void SpawnScoreInternal(Vector3 worldPoint, HitZone zone, float damage)
        {
            var item = _pool[_poolIndex];
            _poolIndex = (_poolIndex + 1) % PoolSize;

            string label;
            Color col;
            float scale;
            float lifetime = 0.85f;

            int baseScore = Mathf.RoundToInt(damage);
            switch (zone)
            {
                case HitZone.Head:
                    label = $"+{Mathf.Max(300, baseScore * 3)}\nHEADSHOT!";
                    col = new Color(1.0f, 0.28f, 0.22f); // Neon Crimson
                    scale = 1.35f;
                    lifetime = 0.95f;
                    break;

                case HitZone.WeakPoint:
                    label = $"+{Mathf.Max(400, baseScore * 4)}\nCRITICAL!";
                    col = new Color(1.0f, 0.85f, 0.12f); // Gold / Amber
                    scale = 1.40f;
                    lifetime = 1.05f;
                    break;

                case HitZone.Body:
                    label = $"+{Mathf.Max(100, baseScore)}";
                    col = new Color(0.2f, 0.9f, 1.0f); // Bright Laser Cyan
                    scale = 1.15f;
                    break;

                case HitZone.Limb:
                    label = $"+{Mathf.Max(25, Mathf.RoundToInt(baseScore * 0.8f))}";
                    col = new Color(0.65f, 1.0f, 0.65f); // Soft Green
                    scale = 0.95f;
                    break;

                default:
                    label = $"+{Mathf.Max(50, baseScore)}";
                    col = Color.white;
                    scale = 1.05f;
                    break;
            }

            // Offset slightly towards camera so it never clips into target geometry
            Vector3 camPos = Camera.main != null ? Camera.main.transform.position : worldPoint + Vector3.back;
            Vector3 toCam = (camPos - worldPoint).normalized;
            Vector3 spawnPos = worldPoint + toCam * 0.08f;

            item.StartPos = spawnPos;
            item.Velocity = Vector3.up * 0.65f + Random.insideUnitSphere * 0.08f;
            item.Velocity.y = Mathf.Abs(item.Velocity.y); // Always rise upwards
            item.Timer = 0f;
            item.Lifetime = lifetime;
            item.MainColor = col;
            item.BaseScale = scale;
            item.IsActive = true;

            item.Text.text = label;
            item.Text.color = col;
            item.ShadowText.text = label;
            item.ShadowText.color = new Color(0f, 0f, 0f, 0.90f);

            item.Root.transform.position = spawnPos;
            item.Root.transform.localScale = Vector3.one * (scale * 0.5f);
            item.Root.SetActive(true);
        }

        private void SpawnDestroyedInternal(Vector3 worldPoint)
        {
            var item = _pool[_poolIndex];
            _poolIndex = (_poolIndex + 1) % PoolSize;

            Vector3 camPos = Camera.main != null ? Camera.main.transform.position : worldPoint + Vector3.back;
            Vector3 toCam = (camPos - worldPoint).normalized;
            Vector3 spawnPos = worldPoint + toCam * 0.12f;

            Color gold = new Color(1.0f, 0.92f, 0.25f);
            item.StartPos = spawnPos;
            item.Velocity = Vector3.up * 0.85f;
            item.Timer = 0f;
            item.Lifetime = 1.15f;
            item.MainColor = gold;
            item.BaseScale = 1.45f;
            item.IsActive = true;

            string label = "+250\nTARGET DESTROYED!";
            item.Text.text = label;
            item.Text.color = gold;
            item.ShadowText.text = label;
            item.ShadowText.color = new Color(0f, 0f, 0f, 0.95f);

            item.Root.transform.position = spawnPos;
            item.Root.transform.localScale = Vector3.one * 0.8f;
            item.Root.SetActive(true);
        }

        private void Update()
        {
            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;

            for (int i = 0; i < PoolSize; i++)
            {
                var item = _pool[i];
                if (!item.IsActive) continue;

                item.Timer += Time.deltaTime;
                float progress = item.Timer / item.Lifetime;

                if (progress >= 1.0f)
                {
                    item.IsActive = false;
                    item.Root.SetActive(false);
                    continue;
                }

                // 1. Upward floating movement with gentle deceleration
                float upwardOffset = Mathf.Sin(progress * Mathf.PI * 0.5f) * 0.55f;
                item.Root.transform.position = item.StartPos + Vector3.up * upwardOffset + (item.Velocity * item.Timer * 0.4f);

                // 2. Billboard face camera in stereo VR
                if (cam != null)
                {
                    Vector3 lookDir = item.Root.transform.position - camPos;
                    if (lookDir.sqrMagnitude > 0.001f)
                    {
                        item.Root.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                    }
                }

                // 3. Elastic scale pop on spawn
                float scaleNorm;
                if (progress < 0.15f)
                {
                    // Rapid punch expansion (0.6x to 1.25x)
                    scaleNorm = Mathf.Lerp(0.6f, 1.25f, progress / 0.15f);
                }
                else if (progress < 0.35f)
                {
                    // Settle back to standard size (1.25x to 1.0x)
                    scaleNorm = Mathf.Lerp(1.25f, 1.0f, (progress - 0.15f) / 0.20f);
                }
                else
                {
                    // Gentle shrink as it fades away
                    scaleNorm = Mathf.Lerp(1.0f, 0.85f, (progress - 0.35f) / 0.65f);
                }
                item.Root.transform.localScale = Vector3.one * (item.BaseScale * scaleNorm);

                // 4. Smooth alpha fadeout in second half of life
                float alpha = 1.0f;
                if (progress > 0.45f)
                {
                    alpha = Mathf.Lerp(1.0f, 0.0f, (progress - 0.45f) / 0.55f);
                }

                Color c = item.MainColor;
                c.a = alpha;
                item.Text.color = c;

                Color sc = Color.black;
                sc.a = alpha * 0.90f;
                item.ShadowText.color = sc;
            }
        }
    }
}

