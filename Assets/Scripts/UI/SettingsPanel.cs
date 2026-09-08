using System;
using LivingDiorama.Audio;
using LivingDiorama.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// Settings, and the only destructive action in the game.
    ///
    /// Erasing a save is guarded by a confirm-within-five-seconds tap rather than a
    /// dialog: it is one fewer modal, it cannot be dismissed by accident, and a stray
    /// tap on the wrong button does nothing at all.
    /// </summary>
    public sealed class SettingsPanel
    {
        const string SubtitlesKey = "ui.subtitles";
        const float ConfirmWindowSeconds = 5f;

        readonly VisualElement _root;
        readonly AudioDirector _audio;
        readonly Label _resetConfirm;
        readonly Button _resetButton;

        DioramaCamera _camera;
        Action _onClose;
        Action _onEraseSave;
        double _resetArmedAt = double.NegativeInfinity;
        double _lastSfxPreview = double.NegativeInfinity;

        public VisualElement Root => _root;

        public static bool SubtitlesEnabled
        {
            get => PlayerPrefs.GetInt(SubtitlesKey, 1) == 1;
            set => PlayerPrefs.SetInt(SubtitlesKey, value ? 1 : 0);
        }

        public SettingsPanel(VisualElement root, AudioDirector audio)
        {
            _root = root;
            _audio = audio;

            var music = root.Q<Slider>("slider-music");
            var sfx = root.Q<Slider>("slider-sfx");
            var voice = root.Q<Slider>("slider-voice");
            var subtitles = root.Q<Toggle>("toggle-subtitles");
            var turntable = root.Q<Toggle>("toggle-turntable");

            music.value = audio.MusicVolume;
            sfx.value = audio.SfxVolume;
            voice.value = audio.VoiceVolume;
            subtitles.value = SubtitlesEnabled;

            music.RegisterValueChangedCallback(e => audio.SetMusicVolume(e.newValue));
            sfx.RegisterValueChangedCallback(e =>
            {
                audio.SetSfxVolume(e.newValue);

                // Preview the new level, but not on every pixel of a drag -- firing a
                // click per frame is exactly the noise the slider is meant to control.
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - _lastSfxPreview < 0.18) return;

                _lastSfxPreview = now;
                audio.PlaySfx("ui_tap");
            });
            voice.RegisterValueChangedCallback(e => audio.SetVoiceVolume(e.newValue));
            subtitles.RegisterValueChangedCallback(e => SubtitlesEnabled = e.newValue);
            turntable.RegisterValueChangedCallback(e =>
            {
                if (_camera != null) _camera.Turntable = e.newValue;
            });

            _resetButton = root.Q<Button>("btn-reset-save");
            _resetConfirm = root.Q<Label>("reset-confirm");
            _resetButton.clicked += OnResetPressed;

            root.Q<Button>("btn-settings-close").clicked += () =>
            {
                audio.PlaySfx("ui_back");
                Disarm();
                _onClose?.Invoke();
            };
        }

        public void Bind(DioramaCamera camera, Action onClose, Action onEraseSave)
        {
            _camera = camera;
            _onClose = onClose;
            _onEraseSave = onEraseSave;

            var turntable = _root.Q<Toggle>("toggle-turntable");
            if (camera != null) turntable.SetValueWithoutNotify(camera.Turntable);
        }

        public void Refresh()
        {
            Disarm();
            _root.Q<Slider>("slider-music").SetValueWithoutNotify(_audio.MusicVolume);
            _root.Q<Slider>("slider-sfx").SetValueWithoutNotify(_audio.SfxVolume);
            _root.Q<Slider>("slider-voice").SetValueWithoutNotify(_audio.VoiceVolume);
            _root.Q<Toggle>("toggle-subtitles").SetValueWithoutNotify(SubtitlesEnabled);
            if (_camera != null) _root.Q<Toggle>("toggle-turntable").SetValueWithoutNotify(_camera.Turntable);
        }

        void OnResetPressed()
        {
            double now = Time.realtimeSinceStartupAsDouble;

            if (now - _resetArmedAt <= ConfirmWindowSeconds)
            {
                Disarm();
                _audio.PlaySfx("ui_back");
                _onEraseSave?.Invoke();
                return;
            }

            _resetArmedAt = now;
            _resetButton.text = "Tap again to erase";
            _resetConfirm.RemoveFromClassList("hidden");
            _audio.PlaySfx("ui_tap");
        }

        void Disarm()
        {
            _resetArmedAt = double.NegativeInfinity;
            _resetButton.text = "Erase everything";
            _resetConfirm.AddToClassList("hidden");
        }
    }
}
