using System;
using UnityEngine;
using UnityEngine.UI;
using ShootingRange.Core;
using ShootingRange.Save;
using ShootingRange.Replay;
using ShootingRange.Targets;

namespace ShootingRange.UI
{
    /// <summary>
    /// World-space settings and scoreboard kiosk UI.
    /// Manages comfort settings (snap/smooth turn, comfort vignette, handedness),
    /// live round score & all-time high scores, and 30-second replay ghost playback.
    /// Interacted with via controller ray pointers or direct physical poke buttons.
    /// </summary>
    public class WorldSpaceSettingsUI : MonoBehaviour
    {
        [Header("Scoreboard Displays")]
        [SerializeField] private Text _currentScoreText;
        [SerializeField] private Text _highScoreText;
        [SerializeField] private Text _statsText;
        [SerializeField] private Button _resetScoreButton;

        [Header("Target Count Controls")]
        [SerializeField] private Text _targetCountText;
        [SerializeField] private Slider _targetCountSlider;
        [SerializeField] private Button _decreaseTargetButton;
        [SerializeField] private Button _increaseTargetButton;

        [Header("Comfort & Movement Controls")]
        [SerializeField] private Button _turnModeButton;
        [SerializeField] private Text _turnModeText;
        [SerializeField] private Slider _snapAngleSlider;
        [SerializeField] private Text _snapAngleText;
        [SerializeField] private Slider _smoothSpeedSlider;
        [SerializeField] private Text _smoothSpeedText;
        [SerializeField] private Button _vignetteButton;
        [SerializeField] private Text _vignetteText;
        [SerializeField] private Button _handednessButton;
        [SerializeField] private Text _handednessText;
        [SerializeField] private Button _stanceButton;
        [SerializeField] private Text _stanceText;

        [Header("Replay Controls")]
        [SerializeField] private Button _playReplayButton;
        [SerializeField] private Text _playReplayButtonText;
        [SerializeField] private Button _stopReplayButton;
        [SerializeField] private Slider _replayScrubber;
        [SerializeField] private Text _replayTelemetryText;

        [Header("Physical Poke Buttons")]
        [SerializeField] private VRPokeInteractable _pokePlayReplay;
        [SerializeField] private VRPokeInteractable _pokeTurnMode;
        [SerializeField] private VRPokeInteractable _pokeVignette;
        [SerializeField] private VRPokeInteractable _pokeResetScore;

        private GameSettingsData _settings;
        private ITrackingProvider _tracking;
        private IReplayRecorder _replayRecorder;
        private TargetManager _targetManager;

        private int _currentScore = 0;
        private int _currentHits = 0;
        private int _currentShots = 0;

        private void Start()
        {
            _settings = SaveManager.Load();
            ResolveDependencies();
            BindUI();
            UpdateAllDisplays();
        }

        private void ResolveDependencies()
        {
            if (ShootingRangeCompositionRoot.Container != null)
            {
                if (_tracking == null) ShootingRangeCompositionRoot.Container.TryResolve(out _tracking);
                if (_replayRecorder == null) ShootingRangeCompositionRoot.Container.TryResolve(out _replayRecorder);
            }

            if (_tracking == null)
            {
                var tp = FindFirstObjectByType<MonoBehaviour>() as ITrackingProvider;
                if (tp != null) _tracking = tp;
            }

            if (_replayRecorder == null)
            {
                _replayRecorder = FindFirstObjectByType<ReplayManager>();
            }

            if (_targetManager == null)
            {
                _targetManager = FindFirstObjectByType<TargetManager>();
            }

            if (_targetManager != null)
            {
                _targetManager.OnTargetHitEvent += HandleTargetHit;
                _targetManager.OnActiveTargetCountChanged += HandleTargetCountChanged;
            }

            var replayMgr = _replayRecorder as ReplayManager;
            if (replayMgr != null)
            {
                replayMgr.OnPlaybackStarted += HandlePlaybackStarted;
                replayMgr.OnPlaybackStopped += HandlePlaybackStopped;
                replayMgr.OnPlaybackProgress += HandlePlaybackProgress;
            }
        }

        private void OnDestroy()
        {
            if (_targetManager != null)
            {
                _targetManager.OnTargetHitEvent -= HandleTargetHit;
                _targetManager.OnActiveTargetCountChanged -= HandleTargetCountChanged;
            }

            var replayMgr = _replayRecorder as ReplayManager;
            if (replayMgr != null)
            {
                replayMgr.OnPlaybackStarted -= HandlePlaybackStarted;
                replayMgr.OnPlaybackStopped -= HandlePlaybackStopped;
                replayMgr.OnPlaybackProgress -= HandlePlaybackProgress;
            }
        }

        private void BindUI()
        {
            // Reset Score UI Button
            if (_resetScoreButton != null)
            {
                _resetScoreButton.onClick.AddListener(ResetScore);
            }

            // Target Count Controls
            if (_targetCountSlider != null)
            {
                _targetCountSlider.minValue = 4f;
                _targetCountSlider.maxValue = 300f;
                _targetCountSlider.wholeNumbers = true;
                _targetCountSlider.SetValueWithoutNotify(_targetManager != null ? _targetManager.ActiveTargetLimit : 8);
                _targetCountSlider.onValueChanged.AddListener(OnTargetSliderChanged);
            }

            if (_decreaseTargetButton != null)
            {
                _decreaseTargetButton.onClick.AddListener(OnDecreaseTargetClicked);
            }

            if (_increaseTargetButton != null)
            {
                _increaseTargetButton.onClick.AddListener(OnIncreaseTargetClicked);
            }

            // Turn Mode Button
            if (_turnModeButton != null)
            {
                _turnModeButton.onClick.AddListener(ToggleSnapTurn);
            }

            // Snap Angle Slider
            if (_snapAngleSlider != null)
            {
                _snapAngleSlider.minValue = 15f;
                _snapAngleSlider.maxValue = 90f;
                _snapAngleSlider.value = _settings.SnapTurnAngle;
                _snapAngleSlider.onValueChanged.AddListener(val =>
                {
                    _settings.SnapTurnAngle = val;
                    ApplySettings();
                });
            }

            // Smooth Speed Slider
            if (_smoothSpeedSlider != null)
            {
                _smoothSpeedSlider.minValue = 30f;
                _smoothSpeedSlider.maxValue = 180f;
                _smoothSpeedSlider.value = _settings.SmoothTurnSpeed;
                _smoothSpeedSlider.onValueChanged.AddListener(val =>
                {
                    _settings.SmoothTurnSpeed = val;
                    ApplySettings();
                });
            }

            // Comfort Vignette Button
            if (_vignetteButton != null)
            {
                _vignetteButton.onClick.AddListener(ToggleComfortVignette);
            }

            // Handedness Button
            if (_handednessButton != null)
            {
                _handednessButton.onClick.AddListener(ToggleHandedness);
            }

            // Stance Button
            if (_stanceButton != null)
            {
                _stanceButton.onClick.AddListener(ToggleSeatedMode);
            }

            // Replay Buttons
            if (_playReplayButton != null)
            {
                _playReplayButton.onClick.AddListener(ToggleReplay);
            }

            if (_stopReplayButton != null)
            {
                _stopReplayButton.onClick.AddListener(StopReplay);
            }

            if (_replayScrubber != null)
            {
                _replayScrubber.onValueChanged.AddListener(val =>
                {
                    if (_replayRecorder != null && _replayRecorder.IsPlayingBack)
                    {
                        _replayRecorder.Scrub(val);
                    }
                });
            }

            // Physical Poke Buttons
            if (_pokePlayReplay != null)
            {
                _pokePlayReplay.OnPressed.AddListener(ToggleReplay);
            }

            if (_pokeTurnMode != null)
            {
                _pokeTurnMode.OnPressed.AddListener(ToggleSnapTurn);
            }

            if (_pokeVignette != null)
            {
                _pokeVignette.OnPressed.AddListener(ToggleComfortVignette);
            }

            if (_pokeResetScore != null)
            {
                _pokeResetScore.OnPressed.AddListener(ResetScore);
            }

            ApplySettings();
        }

        private void Update()
        {
            UpdateReplayTelemetry();
        }

        private void UpdateReplayTelemetry()
        {
            if (_replayTelemetryText == null) return;

            if (_replayRecorder != null)
            {
                if (_replayRecorder.IsPlayingBack)
                {
                    var replayMgr = _replayRecorder as ReplayManager;
                    float curr = replayMgr != null ? replayMgr.PlaybackTime : 0f;
                    float total = _replayRecorder.CurrentDuration;
                    _replayTelemetryText.text = $"<color=#00FFFF>PLAYING 30s GHOST REPLAY [{curr:F1}s / {total:F1}s]</color>\nCompact Bitrate: ~1.47 KB/s | 1mm Spatial Quantization";
                }
                else
                {
                    var stats = _replayRecorder.GetStats();
                    _replayTelemetryText.text = $"30s Rolling Buffer: <color=#00FF88>RECORDING</color>\nBuffer: {stats.DurationSeconds:F1}s | Size: {stats.TotalSizeBytes / 1024f:F2} KB (~{stats.AverageKbPerSecond:F2} KB/s)";
                }
            }
        }

        // ===================================
        // COMFORT & TURN CONTROLS
        // ===================================

        public void ToggleSnapTurn()
        {
            _settings.UseSnapTurn = !_settings.UseSnapTurn;
            ApplySettings();
            UpdateAllDisplays();
        }

        public void ToggleComfortVignette()
        {
            _settings.ComfortVignetteEnabled = !_settings.ComfortVignetteEnabled;
            ApplySettings();
            UpdateAllDisplays();
        }

        public void ToggleHandedness()
        {
            _settings.IsLeftHanded = !_settings.IsLeftHanded;
            ApplySettings();
            UpdateAllDisplays();
        }

        public void ToggleSeatedMode()
        {
            _settings.IsSeatedMode = !_settings.IsSeatedMode;
            ApplySettings();
            UpdateAllDisplays();
        }

        public void ApplySettings()
        {
            if (_tracking == null && ShootingRangeCompositionRoot.Container != null)
            {
                ShootingRangeCompositionRoot.Container.TryResolve(out _tracking);
            }

            if (_tracking != null)
            {
                _tracking.IsLeftHanded = _settings.IsLeftHanded;
                _tracking.IsSeatedMode = _settings.IsSeatedMode;
                _tracking.PlayerHeightOffset = _settings.PlayerHeightOffset;
            }

            var turnCtrl = FindFirstObjectByType<SnapSmoothTurnController>();
            if (turnCtrl != null) turnCtrl.UpdateSettings(_settings);

            var vignette = FindFirstObjectByType<ComfortVignette>();
            if (vignette != null) vignette.IsEnabled = _settings.ComfortVignetteEnabled;

            SaveManager.Save(_settings);
            UpdateAllDisplays();
        }

        // ===================================
        // REPLAY CONTROLS
        // ===================================

        public void ToggleReplay()
        {
            if (_replayRecorder == null) ResolveDependencies();
            if (_replayRecorder != null)
            {
                if (_replayRecorder.IsPlayingBack)
                {
                    _replayRecorder.StopPlayback();
                }
                else
                {
                    _replayRecorder.StartPlayback();
                }
            }
        }

        public void PlayReplay()
        {
            if (_replayRecorder == null) ResolveDependencies();
            if (_replayRecorder != null && !_replayRecorder.IsPlayingBack)
            {
                _replayRecorder.StartPlayback();
            }
        }

        public void StopReplay()
        {
            if (_replayRecorder != null && _replayRecorder.IsPlayingBack)
            {
                _replayRecorder.StopPlayback();
            }
        }

        private void HandlePlaybackStarted()
        {
            if (_playReplayButtonText != null) _playReplayButtonText.text = "⏹ STOP REPLAY";
        }

        private void HandlePlaybackStopped()
        {
            if (_playReplayButtonText != null) _playReplayButtonText.text = "▶ PLAY 30s REPLAY";
            if (_replayScrubber != null) _replayScrubber.value = 0f;
        }

        private void HandlePlaybackProgress(float current, float total)
        {
            if (_replayScrubber != null && total > 0.001f)
            {
                _replayScrubber.SetValueWithoutNotify(current / total);
            }
        }

        // ===================================
        // SCOREBOARD & STATS
        // ===================================

        private void HandleTargetHit(ITargetEntity target, float damage, HitZone zone, ulong shooterId)
        {
            int pts = 100;
            switch (zone)
            {
                case HitZone.Head: pts = 250; break;
                case HitZone.Body: pts = 100; break;
                case HitZone.Limb: pts = 50; break;
                case HitZone.WeakPoint: pts = 500; break;
            }
            AddScore(pts);
        }

        public void AddScore(int points)
        {
            _currentScore += points;
            _currentHits++;

            if (_currentScore > _settings.HighScore)
            {
                _settings.HighScore = _currentScore;
                _settings.HighScoreDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
                SaveManager.Save(_settings);
            }

            UpdateAllDisplays();
        }

        public void ResetScore()
        {
            _currentScore = 0;
            _currentHits = 0;
            _currentShots = 0;
            UpdateAllDisplays();
        }

        public void UpdateAllDisplays()
        {
            if (_currentScoreText != null)
            {
                _currentScoreText.text = $"CURRENT SCORE\n<color=#00FFFF>{_currentScore:D6}</color>";
            }

            if (_highScoreText != null)
            {
                _highScoreText.text = $"ALL-TIME HIGH SCORE\n<color=#FFD700>{_settings.HighScore:D6}</color>";
            }

            if (_statsText != null)
            {
                float acc = _currentShots > 0 ? ((float)_currentHits / _currentShots) * 100f : 100f;
                int targets = _targetManager != null ? _targetManager.ActiveTargetCount : 8;
                _statsText.text = $"HITS: {_currentHits}  |  ACCURACY: {acc:F1}%  |  TARGETS: {targets}";
            }

            if (_targetCountText != null)
            {
                int targets = _targetManager != null ? _targetManager.ActiveTargetLimit : 8;
                _targetCountText.text = $"ACTIVE TARGETS: <color=#00FFFF>{targets}</color> / 300";
            }

            if (_turnModeText != null)
            {
                _turnModeText.text = _settings.UseSnapTurn
                    ? $"TURN MODE: <color=#00FF88>SNAP ({_settings.SnapTurnAngle:F0}°)</color>"
                    : $"TURN MODE: <color=#00FFFF>SMOOTH ({_settings.SmoothTurnSpeed:F0}°/s)</color>";
            }

            if (_snapAngleText != null)
            {
                _snapAngleText.text = $"Snap Angle: {_settings.SnapTurnAngle:F0}°";
            }

            if (_smoothSpeedText != null)
            {
                _smoothSpeedText.text = $"Smooth Speed: {_settings.SmoothTurnSpeed:F0}°/s";
            }

            if (_vignetteText != null)
            {
                _vignetteText.text = _settings.ComfortVignetteEnabled
                    ? "COMFORT VIGNETTE: <color=#00FF88>ENABLED</color>"
                    : "COMFORT VIGNETTE: <color=#FF4444>DISABLED</color>";
            }

            if (_handednessText != null)
            {
                _handednessText.text = _settings.IsLeftHanded ? "HAND: <color=#FFD700>LEFT</color>" : "HAND: <color=#00FFFF>RIGHT</color>";
            }

            if (_stanceText != null)
            {
                _stanceText.text = _settings.IsSeatedMode ? "STANCE: <color=#FFD700>SEATED</color>" : "STANCE: <color=#00FFFF>STANDING</color>";
            }
        }

        private void OnTargetSliderChanged(float val)
        {
            int count = Mathf.RoundToInt(val);
            if (_targetManager != null)
            {
                _targetManager.SetActiveTargetCount(count);
            }
            UpdateTargetCountDisplay(count);
        }

        private void OnDecreaseTargetClicked()
        {
            if (_targetManager != null)
            {
                _targetManager.DecreaseTargets(4);
                int count = _targetManager.ActiveTargetLimit;
                if (_targetCountSlider != null) _targetCountSlider.SetValueWithoutNotify(count);
                UpdateTargetCountDisplay(count);
            }
        }

        private void OnIncreaseTargetClicked()
        {
            if (_targetManager != null)
            {
                _targetManager.IncreaseTargets(4);
                int count = _targetManager.ActiveTargetLimit;
                if (_targetCountSlider != null) _targetCountSlider.SetValueWithoutNotify(count);
                UpdateTargetCountDisplay(count);
            }
        }

        private void HandleTargetCountChanged(int count)
        {
            if (_targetCountSlider != null)
            {
                _targetCountSlider.SetValueWithoutNotify(count);
            }
            UpdateTargetCountDisplay(count);
        }

        private void UpdateTargetCountDisplay(int count)
        {
            if (_targetCountText != null)
            {
                _targetCountText.text = $"ACTIVE TARGETS: <color=#00FFFF>{count}</color> / 300";
            }
            UpdateAllDisplays();
        }
    }
}
