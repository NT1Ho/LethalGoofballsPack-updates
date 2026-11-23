using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace LethalGoofballsPack.WesleyZh
{
    internal sealed class MediaCaptions : IDisposable
    {
        private readonly CaptionCatalog catalog;
        private readonly CaptionHud view = new CaptionHud();
        private readonly Dictionary<AudioClip, CaptionTrack> audioTracks = new Dictionary<AudioClip, CaptionTrack>();
        private readonly List<AudioPlayback> audio = new List<AudioPlayback>();
        private readonly List<AudioSource> nativeAudio = new List<AudioSource>();
        private readonly List<VideoPlayback> videos = new List<VideoPlayback>();
        private readonly List<Scene> newScenes = new List<Scene>();
        private Terminal terminal;
        private object lastVoice;
        private object lastWriting;
        private float discoverAt;
        private float scanNativeAudioAt;

        internal static MediaCaptions Current { get; private set; }
        internal int SourceCount => catalog.SourceCount;

        internal MediaCaptions(CaptionCatalog catalog)
        {
            this.catalog = catalog;
            Current = this;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                SceneLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }

        public void Dispose()
        {
            if (Current == this) Current = null;
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            view.Dispose();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            newScenes.Add(scene);
            discoverAt = Time.unscaledTime + 0.25f;
        }

        private void SceneUnloaded(Scene scene)
        {
            audioTracks.Clear();
            audio.RemoveAll(playback => !playback.Source || playback.Source.gameObject.scene == scene);
            nativeAudio.RemoveAll(source => !source || source.gameObject.scene == scene);
            videos.RemoveAll(playback => !playback.Player || playback.Player.gameObject.scene == scene);
            newScenes.Remove(scene);
            view.Show(null, null);
        }

        private void DiscoverSceneMedia()
        {
            // Authored sources can be inactive until a player enters the listening area.
            // Retain only captioned playOnAwake sources so later native starts do not need a scene scan.
            foreach (var scene in newScenes)
            {
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!terminal) terminal = root.GetComponentInChildren<Terminal>();
                    foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                    {
                        if (!source.clip) continue;
                        if (source.playOnAwake && catalog.ForAudio(source.clip.name, source.clip.length) != null)
                            nativeAudio.Add(source);
                        if (source.isActiveAndEnabled && source.isPlaying && !audio.Exists(item => item.Source == source && !item.OneShot))
                            Play(source, source.clip, 0);
                    }
                    foreach (var player in root.GetComponentsInChildren<VideoPlayer>())
                    {
                        if (player.isPlaying || player.isPaused) StartVideo(player);
                    }
                }
            }
            newScenes.Clear();
        }

        private void StartVideo(VideoPlayer player)
        {
            if (!player || !player.clip) return;
            var track = catalog.ForVideo(player.clip.name, player.clip.frameCount);
            int i = videos.FindIndex(item => item.Player == player);
            if (i >= 0) videos.RemoveAt(i);
            if (track != null) videos.Add(new VideoPlayback(player, track));
        }

        private void Play(AudioSource source, AudioClip clip, double scheduled, bool oneShot = false, float volume = 1f)
        {
            if (!source || !clip) return;
            if (!audioTracks.TryGetValue(clip, out var track))
                audioTracks.Add(clip, track = catalog.ForAudio(clip.name, clip.length));
            if (track == null || volume <= 0f) return;
            if (!oneShot) audio.RemoveAll(item => item.Source == source && !item.OneShot);
            double now = AudioSettings.dspTime;
            audio.Add(new AudioPlayback(source, clip, track, Math.Max(now, scheduled), oneShot, volume));
        }

        private void Stop(AudioSource source) => audio.RemoveAll(item => item.Source == source);

        internal void Tick()
        {
            float now = Time.unscaledTime;
            if (newScenes.Count != 0 && now >= discoverAt) DiscoverSceneMedia();
            if (nativeAudio.Count != 0 && now >= scanNativeAudioAt)
            {
                scanNativeAudioAt = now + 0.1f;
                for (int i = nativeAudio.Count - 1; i >= 0; i--)
                {
                    var source = nativeAudio[i];
                    if (!source) nativeAudio.RemoveAt(i);
                    else if (source.isActiveAndEnabled && source.isPlaying &&
                             !audio.Exists(item => item.Source == source && !item.OneShot))
                        Play(source, source.clip, 0);
                }
            }
            if (audio.Count == 0 && videos.Count == 0 && lastVoice == null && lastWriting == null) return;

            var round = StartOfRound.Instance;
            var listener = round ? round.audioListener : null;
            var camera = round ? round.activeCamera : null;
            var localPlayer = GameNetworkManager.Instance ? GameNetworkManager.Instance.localPlayerController : null;
            var voice = new Choice();
            var writing = new Choice();
            double dsp = AudioSettings.dspTime;
            for (int i = audio.Count - 1; i >= 0; i--)
            {
                var item = audio[i];
                if (!item.ReadClock(dsp, out double seconds, out bool playing))
                {
                    audio.RemoveAt(i);
                    continue;
                }
                if (!playing) continue;
                var line = CaptionTrack.At(item.Track.Dialogue, seconds);
                if (line != null && !item.Source.isVirtual)
                    voice.Consider(item, line, Gain(item.Source, listener) * item.Volume, lastVoice);
            }
            for (int i = videos.Count - 1; i >= 0; i--)
            {
                var item = videos[i];
                var player = item.Player;
                if (!player || !player.isActiveAndEnabled || player.clip != item.Clip ||
                    (!player.isPlaying && !player.isPaused))
                {
                    videos.RemoveAt(i);
                    continue;
                }
                if (player.frame < 0) continue;
                bool preview = terminal && terminal.videoPlayer == player;
                if (preview && (localPlayer == null || !localPlayer.inTerminalMenu)) continue;
                var line = CaptionTrack.At(item.Track.Dialogue, player.time);
                if (line != null)
                    voice.Consider(item, line, VideoGain(player, listener), lastVoice);
                var text = CaptionTrack.At(item.Track.Writing, player.time);
                if (text != null && (preview || item.InView(camera, round)))
                    writing.Consider(item, text, preview ? 2f : 1f, lastWriting);
            }
            lastVoice = voice.Playback;
            lastWriting = writing.Playback;
            view.Show(voice.Text, writing.Text);
        }

        private static float VideoGain(VideoPlayer player, AudioListener listener)
        {
            if (!listener || !listener.isActiveAndEnabled) return 0f;
            float gain = 0f;
            int count = Math.Min(player.controlledAudioTrackCount, player.audioTrackCount);
            for (ushort i = 0; i < count; i++)
            {
                if (!player.IsAudioTrackEnabled(i)) continue;
                if (player.audioOutputMode == VideoAudioOutputMode.AudioSource)
                    gain = Mathf.Max(gain, Gain(player.GetTargetAudioSource(i), listener));
                else if (player.audioOutputMode == VideoAudioOutputMode.Direct && !AudioListener.pause && !player.GetDirectAudioMute(i))
                    gain = Mathf.Max(gain, player.GetDirectAudioVolume(i) * AudioListener.volume);
            }
            return gain;
        }

        private static float Gain(AudioSource source, AudioListener listener)
        {
            if (!source || !source.isActiveAndEnabled || !listener || !listener.isActiveAndEnabled ||
                source.mute || (AudioListener.pause && !source.ignoreListenerPause))
                return 0f;
            float gain = source.volume * (source.ignoreListenerVolume ? 1f : AudioListener.volume);
            if (source.spatialBlend <= 0f) return gain;
            float near = Mathf.Max(0.01f, source.minDistance);
            float far = Mathf.Max(near, source.maxDistance);
            float distance = Mathf.Clamp(Vector3.Distance(source.transform.position, listener.transform.position), near, far);
            float fraction = Mathf.InverseLerp(near, far, distance);
            float spatialGain;
            switch (source.rolloffMode)
            {
                case AudioRolloffMode.Linear:
                    spatialGain = 1f - fraction;
                    break;
                case AudioRolloffMode.Custom:
                    var curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                    spatialGain = curve == null ? near / distance : Mathf.Clamp01(curve.Evaluate(fraction));
                    break;
                default:
                    spatialGain = near / distance;
                    break;
            }
            return gain * Mathf.Lerp(1f, spatialGain, source.spatialBlend);
        }

        private struct Choice
        {
            private float score;
            internal object Playback;
            internal string Text;

            internal void Consider(object playback, string text, float gain, object incumbent)
            {
                // Keep a nearly equally audible source when players move between two sources.
                if (gain < (playback == incumbent ? 0.006f : 0.009f)) return;
                float priority = gain * (playback == incumbent ? 1.15f : 1f);
                if (priority <= score) return;
                score = priority;
                Playback = playback;
                Text = text;
            }
        }

        private sealed class AudioPlayback
        {
            private double clock;
            private double lastDsp;
            private readonly double scheduled;
            private readonly AudioClip clip;
            private bool wasPlaying;

            internal AudioPlayback(AudioSource source, AudioClip clip, CaptionTrack track, double scheduled, bool oneShot, float volume)
            {
                Source = source;
                this.clip = clip;
                Track = track;
                this.scheduled = scheduled;
                lastDsp = scheduled;
                OneShot = oneShot;
                Volume = volume;
            }

            internal AudioSource Source { get; }
            internal CaptionTrack Track { get; }
            internal bool OneShot { get; }
            internal float Volume { get; }

            internal bool ReadClock(double dsp, out double seconds, out bool playing)
            {
                seconds = 0;
                playing = false;
                if (!Source || !Source.isActiveAndEnabled || !clip || (!OneShot && Source.clip != clip))
                    return false;
                if (dsp < scheduled) return true;
                playing = Source.isPlaying;
                if (OneShot)
                {
                    // Unity exposes no sample cursor for PlayOneShot voices. Only their
                    // local DSP interval advances here; normal clips use the sample cursor.
                    if (playing && wasPlaying) clock += Math.Max(0, dsp - lastDsp) * Math.Abs(Source.pitch);
                    lastDsp = dsp;
                    seconds = clock;
                    if (clock >= clip.length) return false;
                }
                else
                {
                    int samples = Source.timeSamples;
                    seconds = samples / (double)clip.frequency;
                    if (!playing && samples == 0 && wasPlaying) return false;
                }
                if (!playing && !wasPlaying && dsp - scheduled > 0.25) return false;
                wasPlaying |= playing;
                return true;
            }
        }

        private sealed class VideoPlayback
        {
            private readonly Renderer renderer;

            internal VideoPlayback(VideoPlayer player, CaptionTrack track)
            {
                Player = player;
                Clip = player.clip;
                Track = track;
                renderer = player.targetMaterialRenderer;
                if (!renderer) renderer = player.GetComponent<Renderer>();
            }

            internal VideoPlayer Player { get; }
            internal VideoClip Clip { get; }
            internal CaptionTrack Track { get; }

            internal bool InView(Camera camera, StartOfRound round)
            {
                if (!camera || !round) return false;
                if (Player.renderMode == VideoRenderMode.CameraNearPlane || Player.renderMode == VideoRenderMode.CameraFarPlane)
                    return Player.targetCamera == camera;
                if (renderer && !renderer.enabled) return false;
                Vector3 world = renderer ? renderer.bounds.center : Player.transform.position;
                Vector3 viewport = camera.WorldToViewportPoint(world);
                if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1)
                    return false;
                var origin = camera.transform.position;
                if (!Physics.Linecast(origin, world, out var hit, round.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    return true;
                var screen = renderer ? renderer.transform : Player.transform;
                return hit.transform == screen || hit.transform.IsChildOf(screen) || screen.IsChildOf(hit.transform);
            }
        }

        [HarmonyPatch(typeof(VideoPlayer), "InvokeStartedCallback_Internal")]
        private static class VideoStarted
        {
            private static void Postfix(VideoPlayer source) => Current?.StartVideo(source);
        }

        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Play), new Type[] { })]
        private static class AudioStarted
        {
            private static void Postfix(AudioSource __instance) => Current?.Play(__instance, __instance.clip, 0);
        }

        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Play), typeof(ulong))]
        private static class AudioDelayedSamples
        {
            private static void Postfix(AudioSource __instance, ulong delay) =>
                Current?.Play(__instance, __instance.clip, AudioSettings.dspTime + delay / 44100d);
        }

        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.PlayDelayed))]
        private static class AudioDelayed
        {
            private static void Postfix(AudioSource __instance, float delay) =>
                Current?.Play(__instance, __instance.clip, AudioSettings.dspTime + delay);
        }

        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.PlayScheduled))]
        private static class AudioScheduled
        {
            private static void Postfix(AudioSource __instance, double time) => Current?.Play(__instance, __instance.clip, time);
        }

        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.PlayOneShot), typeof(AudioClip), typeof(float))]
        private static class AudioOneShot
        {
            private static void Postfix(AudioSource __instance, AudioClip clip, float volumeScale) =>
                Current?.Play(__instance, clip, 0, oneShot: true, volume: volumeScale);
        }

        [HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Stop), new Type[] { })]
        private static class AudioStopped
        {
            private static void Postfix(AudioSource __instance) => Current?.Stop(__instance);
        }
    }
}
