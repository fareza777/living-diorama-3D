using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LivingDiorama.Audio
{
    /// <summary>
    /// All sound in one place: a music bed, a biome ambience layer that crossfades, a
    /// narrator channel that ducks everything else, and a small pool of one-shots.
    ///
    /// The narrator is the reason this exists. A voice line that fights the ambience
    /// sounds cheap, so anything the narrator says pulls the rest of the mix down and
    /// releases it afterwards -- the same trick a radio desk uses, and the difference
    /// between "a game with audio" and a game that sounds directed.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        const string MusicVolumeKey = "audio.music";
        const string SfxVolumeKey = "audio.sfx";
        const string VoiceVolumeKey = "audio.voice";

        /// <summary>Ambience rides below the music bed rather than alongside it.</summary>
        const float AmbienceLevel = 0.8f;

        const float DuckLevel = 0.28f;
        const float DuckTime = 0.25f;
        const float ReleaseTime = 0.7f;

        readonly Dictionary<string, AudioClip> _cache = new(48);
        readonly List<AudioSource> _sfxPool = new(8);

        AudioSource _ambienceA, _ambienceB, _music, _voice;
        bool _ambienceUsingA = true;
        Coroutine _duckRoutine, _fadeRoutine;
        string _currentAmbience;

        public static AudioDirector Instance { get; private set; }

        /// <summary>Fired with the spoken text so the subtitle band can follow along.
        /// Subtitles are on by default: plenty of people play with the sound off.</summary>
        public event Action<string, float> NarrationStarted;
        public event Action NarrationFinished;

        public float MusicVolume { get; private set; } = 0.55f;
        public float SfxVolume { get; private set; } = 0.9f;
        public float VoiceVolume { get; private set; } = 1f;
        public bool IsNarrating => _voice != null && _voice.isPlaying;

        public static AudioDirector Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var director = go.AddComponent<AudioDirector>();
            director.Build();
            return director;
        }

        void Build()
        {
            Instance = this;

            MusicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 0.55f);
            SfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 0.9f);
            VoiceVolume = PlayerPrefs.GetFloat(VoiceVolumeKey, 1f);

            _music = MakeSource("Music", loop: true);
            _ambienceA = MakeSource("AmbienceA", loop: true);
            _ambienceB = MakeSource("AmbienceB", loop: true);
            _voice = MakeSource("Voice", loop: false);

            for (int i = 0; i < 8; i++) _sfxPool.Add(MakeSource($"Sfx{i}", loop: false));
        }

        AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f;      // the diorama is small; everything is "in the room"
            source.volume = 0f;
            return source;
        }

        // ---- clip loading ---------------------------------------------------

        AudioClip Load(string folder, string key)
        {
            string path = $"Audio/{folder}/{key}";
            if (_cache.TryGetValue(path, out AudioClip cached)) return cached;

            var clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                // Missing audio must never be fatal: the generation pipeline is a separate
                // step, and the game has to be playable before it has been run.
                Debug.LogWarning($"[AudioDirector] missing clip '{path}'");
            }

            _cache[path] = clip;
            return clip;
        }

        // ---- one-shots ------------------------------------------------------

        public void PlaySfx(string key, float volumeScale = 1f, float pitchJitter = 0.06f)
        {
            AudioClip clip = Load("SFX", key);
            if (clip == null) return;

            AudioSource source = NextFreeSource();
            source.clip = clip;
            source.volume = SfxVolume * volumeScale;
            // A touch of pitch variation stops repeated taps sounding like a machine.
            source.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            source.Play();
        }

        AudioSource NextFreeSource()
        {
            for (int i = 0; i < _sfxPool.Count; i++)
            {
                if (!_sfxPool[i].isPlaying) return _sfxPool[i];
            }
            // All busy: steal the oldest rather than dropping the cue.
            return _sfxPool[0];
        }

        // ---- narration ------------------------------------------------------

        /// <summary>Speak a line, ducking the bed underneath it.</summary>
        public void Narrate(string key, string subtitle = null, Action onFinished = null)
        {
            AudioClip clip = Load("VO", key);

            if (clip == null)
            {
                // No voice asset: still surface the subtitle so the beat is not lost, and
                // hold it long enough to read.
                float readingTime = subtitle != null ? Mathf.Clamp(subtitle.Length * 0.055f, 1.8f, 7f) : 0f;
                if (!string.IsNullOrEmpty(subtitle)) NarrationStarted?.Invoke(subtitle, readingTime);
                StartCoroutine(FinishAfter(readingTime, onFinished));
                return;
            }

            _voice.Stop();
            _voice.clip = clip;
            _voice.volume = VoiceVolume;
            _voice.Play();

            if (!string.IsNullOrEmpty(subtitle)) NarrationStarted?.Invoke(subtitle, clip.length);

            Duck(true);
            StartCoroutine(FinishAfter(clip.length + 0.15f, () =>
            {
                Duck(false);
                NarrationFinished?.Invoke();
                onFinished?.Invoke();
            }));
        }

        public void StopNarration()
        {
            _voice.Stop();
            Duck(false);
            NarrationFinished?.Invoke();
        }

        IEnumerator FinishAfter(float seconds, Action action)
        {
            if (seconds > 0f) yield return new WaitForSecondsRealtime(seconds);
            action?.Invoke();
        }

        void Duck(bool down)
        {
            if (_duckRoutine != null) StopCoroutine(_duckRoutine);
            _duckRoutine = StartCoroutine(DuckRoutine(down));
        }

        IEnumerator DuckRoutine(bool down)
        {
            float target = down ? DuckLevel : 1f;
            float duration = down ? DuckTime : ReleaseTime;

            float startMusic = _music.volume;
            float startAmbience = ActiveAmbience.volume;
            float musicTarget = MusicVolume * target;
            // Ambience sits under the music at 0.8; releasing the duck has to return it to
            // that level, not to full, or every narration would leave the bed a little louder.
            float ambienceTarget = MusicVolume * AmbienceLevel * target;

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                _music.volume = Mathf.Lerp(startMusic, musicTarget, k);
                ActiveAmbience.volume = Mathf.Lerp(startAmbience, ambienceTarget, k);
                yield return null;
            }

            _music.volume = musicTarget;
            ActiveAmbience.volume = ambienceTarget;
        }

        // ---- beds -----------------------------------------------------------

        AudioSource ActiveAmbience => _ambienceUsingA ? _ambienceA : _ambienceB;
        AudioSource IdleAmbience => _ambienceUsingA ? _ambienceB : _ambienceA;

        public void PlayMusic(string key, float fadeSeconds = 1.5f)
        {
            AudioClip clip = Load("Ambience", key);
            if (clip == null || _music.clip == clip) return;

            _music.clip = clip;
            _music.Play();
            StartCoroutine(FadeSource(_music, MusicVolume, fadeSeconds));
        }

        /// <summary>Crossfade to a new ambience bed. Called when the dominant biome or the
        /// time of day changes, so dusk in the forest actually sounds different.</summary>
        public void SetAmbience(string key, float fadeSeconds = 2.5f)
        {
            if (_currentAmbience == key) return;
            _currentAmbience = key;

            AudioClip clip = Load("Ambience", key);
            if (clip == null) return;

            AudioSource incoming = IdleAmbience;
            AudioSource outgoing = ActiveAmbience;

            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.Play();

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(Crossfade(outgoing, incoming, fadeSeconds));
            _ambienceUsingA = !_ambienceUsingA;
        }

        IEnumerator Crossfade(AudioSource outgoing, AudioSource incoming, float duration)
        {
            float startOut = outgoing.volume;
            float targetIn = MusicVolume * AmbienceLevel;
            float t = 0f;

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                outgoing.volume = Mathf.Lerp(startOut, 0f, k);
                incoming.volume = Mathf.Lerp(0f, targetIn, k);
                yield return null;
            }

            outgoing.Stop();
            incoming.volume = targetIn;
        }

        IEnumerator FadeSource(AudioSource source, float target, float duration)
        {
            float start = source.volume;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(start, target, t / duration);
                yield return null;
            }
            source.volume = target;
        }

        // ---- settings -------------------------------------------------------

        public void SetMusicVolume(float value)
        {
            MusicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicVolumeKey, MusicVolume);
            _music.volume = MusicVolume;
            ActiveAmbience.volume = MusicVolume * AmbienceLevel;
        }

        public void SetSfxVolume(float value)
        {
            SfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
        }

        public void SetVoiceVolume(float value)
        {
            VoiceVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VoiceVolumeKey, VoiceVolume);
            _voice.volume = VoiceVolume;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            PlayerPrefs.Save();
        }
    }
}
