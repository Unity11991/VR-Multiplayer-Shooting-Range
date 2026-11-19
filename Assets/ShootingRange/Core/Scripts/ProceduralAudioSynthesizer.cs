using UnityEngine;

namespace ShootingRange.Core
{
    /// <summary>
    /// Pure C# procedural audio synthesizer that generates punchy, realistic gunshots,
    /// metallic slide racks, magazine clicks, and target impact pings in real-time.
    /// Operates without external audio assets or third-party audio middleware.
    /// </summary>
    public static class ProceduralAudioSynthesizer
    {
        private static AudioClip _pistolShotClip;
        private static AudioClip _rifleShotClip;
        private static AudioClip _dryFireClip;
        private static AudioClip _slideRackClip;
        private static AudioClip _magEjectClip;
        private static AudioClip _magInsertClip;
        private static AudioClip _magGuideClip;
        private static AudioClip _steelPingClip;
        private static AudioClip _impactThudClip;
        private static AudioClip _ricochetWhineClip;

        public static AudioClip GetGunshotClip(string name, bool isRifle)
        {
            if (isRifle)
            {
                if (_rifleShotClip == null) _rifleShotClip = GenerateGunshot(44100, 0.32f, 180f, 40f, 15f, 25f, "RifleShot_Procedural");
                return _rifleShotClip;
            }
            else
            {
                if (_pistolShotClip == null) _pistolShotClip = GenerateGunshot(44100, 0.22f, 140f, 50f, 22f, 35f, "PistolShot_Procedural");
                return _pistolShotClip;
            }
        }

        public static AudioClip GetDryFireClip()
        {
            if (_dryFireClip == null)
            {
                _dryFireClip = GenerateToneNoise(44100, 0.05f, 2200f, 80f, 0.25f, "DryFire_Procedural");
            }
            return _dryFireClip;
        }

        public static AudioClip GetSlideRackClip()
        {
            if (_slideRackClip == null)
            {
                _slideRackClip = GenerateDoubleClack(44100, 0.18f, "SlideRack_Procedural");
            }
            return _slideRackClip;
        }

        public static AudioClip GetMagEjectClip()
        {
            if (_magEjectClip == null)
            {
                _magEjectClip = GenerateToneNoise(44100, 0.08f, 1400f, 40f, 0.3f, "MagEject_Procedural");
            }
            return _magEjectClip;
        }

        public static AudioClip GetMagInsertClip()
        {
            if (_magInsertClip == null)
            {
                _magInsertClip = GenerateToneNoise(44100, 0.12f, 950f, 30f, 0.4f, "MagInsert_Procedural");
            }
            return _magInsertClip;
        }

        public static AudioClip GetMagSeatClip() => GetMagInsertClip();

        public static AudioClip GetMagGuideClip()
        {
            if (_magGuideClip == null)
            {
                _magGuideClip = GenerateToneNoise(44100, 0.08f, 620f, 25f, 0.22f, "MagGuide_Procedural");
            }
            return _magGuideClip;
        }

        public static AudioClip GetSteelPingClip()
        {
            if (_steelPingClip == null)
            {
                _steelPingClip = GenerateResonantBell(44100, 0.45f, 1650f, 3300f, "SteelPing_Procedural");
            }
            return _steelPingClip;
        }

        public static AudioClip GetImpactThudClip()
        {
            if (_impactThudClip == null)
            {
                _impactThudClip = GenerateThud(44100, 0.09f, 90f, "Thud_Procedural");
            }
            return _impactThudClip;
        }

        public static AudioClip GetRicochetWhineClip()
        {
            if (_ricochetWhineClip == null)
            {
                _ricochetWhineClip = GenerateRicochet(44100, 0.28f, "Ricochet_Procedural");
            }
            return _ricochetWhineClip;
        }

        private static AudioClip GenerateGunshot(int sampleRate, float duration, float startFreq, float endFreq, float lowDecay, float noiseDecay, string clipName)
        {
            int count = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;

                // Transient pop (initial 3ms)
                float transient = UnityEngine.Random.Range(-1f, 1f) * Mathf.Exp(-t * 120f);

                // Low punch frequency sweep
                float freq = Mathf.Lerp(startFreq, endFreq, Mathf.Clamp01(t * 12f));
                float punch = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * lowDecay);

                // Muzzle blast crack
                float blast = UnityEngine.Random.Range(-0.8f, 0.8f) * Mathf.Exp(-t * noiseDecay);

                float mix = (transient * 0.7f + punch * 0.75f + blast * 0.45f);
                samples[i] = Mathf.Clamp(mix, -1f, 1f);
            }

            var clip = AudioClip.Create(clipName, count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip GenerateResonantBell(int sampleRate, float duration, float freq1, float freq2, string clipName)
        {
            int count = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                float impact = UnityEngine.Random.Range(-0.5f, 0.5f) * Mathf.Exp(-t * 200f);
                float tone1 = Mathf.Sin(2f * Mathf.PI * freq1 * t) * Mathf.Exp(-t * 9f);
                float tone2 = Mathf.Sin(2f * Mathf.PI * freq2 * t) * Mathf.Exp(-t * 14f) * 0.5f;

                samples[i] = Mathf.Clamp((impact * 0.4f + tone1 * 0.6f + tone2 * 0.3f), -1f, 1f);
            }

            var clip = AudioClip.Create(clipName, count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip GenerateThud(int sampleRate, float duration, float freq, string clipName)
        {
            int count = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                float pop = UnityEngine.Random.Range(-0.4f, 0.4f) * Mathf.Exp(-t * 90f);
                float bass = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 30f);
                samples[i] = Mathf.Clamp((pop * 0.5f + bass * 0.5f), -1f, 1f);
            }

            var clip = AudioClip.Create(clipName, count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip GenerateDoubleClack(int sampleRate, float duration, string clipName)
        {
            int count = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                // First click at t = 0
                float click1 = (UnityEngine.Random.Range(-0.6f, 0.6f) + Mathf.Sin(2f * Mathf.PI * 2400f * t)) * Mathf.Exp(-t * 110f);
                // Second click at t = 0.08
                float t2 = t - 0.08f;
                float click2 = 0f;
                if (t2 > 0f)
                {
                    click2 = (UnityEngine.Random.Range(-0.6f, 0.6f) + Mathf.Sin(2f * Mathf.PI * 1800f * t2)) * Mathf.Exp(-t2 * 110f);
                }

                samples[i] = Mathf.Clamp((click1 + click2) * 0.6f, -1f, 1f);
            }

            var clip = AudioClip.Create(clipName, count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip GenerateToneNoise(int sampleRate, float duration, float freq, float decay, float volume, string clipName)
        {
            int count = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                float click = (UnityEngine.Random.Range(-0.4f, 0.4f) + Mathf.Sin(2f * Mathf.PI * freq * t)) * Mathf.Exp(-t * decay);
                samples[i] = Mathf.Clamp(click * volume, -1f, 1f);
            }

            var clip = AudioClip.Create(clipName, count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip GenerateRicochet(int sampleRate, float duration, string clipName)
        {
            int count = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                float freq = Mathf.Lerp(4500f, 900f, Mathf.Pow(t / duration, 0.5f));
                float whine = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 8f);
                float noise = UnityEngine.Random.Range(-0.2f, 0.2f) * Mathf.Exp(-t * 40f);
                samples[i] = Mathf.Clamp((whine * 0.7f + noise * 0.3f), -1f, 1f);
            }

            var clip = AudioClip.Create(clipName, count, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

