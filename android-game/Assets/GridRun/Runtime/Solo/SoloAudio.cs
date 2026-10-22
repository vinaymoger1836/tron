using System;
using System.Collections.Generic;
using UnityEngine;

namespace GridRun.Solo
{
    // Procedural sound matching the browser's synth: short effect tones and a sparse four-chord
    // music bed rendered at four intensities. No audio assets are bundled.
    public sealed class SoloAudio : MonoBehaviour
    {
        const int Rate = 44100;
        AudioSource effects, musicA, musicB;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly AudioClip[] music = new AudioClip[4];
        int musicLevel = -1;
        AudioSource activeMusic;

        public bool Enabled { get; set; } = true;

        void Awake()
        {
            effects = gameObject.AddComponent<AudioSource>();
            musicA = gameObject.AddComponent<AudioSource>();
            musicB = gameObject.AddComponent<AudioSource>();
            foreach (var source in new[] { effects, musicA, musicB }) { source.playOnAwake = false; source.spatialBlend = 0; }
            musicA.loop = musicB.loop = true;
            musicA.volume = musicB.volume = 0;
        }

        static float Wave(string type, double phase)
        {
            double p = phase - Math.Floor(phase);
            switch (type)
            {
                case "sine": return (float)Math.Sin(p * 2 * Math.PI);
                case "sawtooth": return (float)(2 * p - 1);
                default: return (float)(1 - 4 * Math.Abs(p - .5)); // triangle
            }
        }

        // Exponential pitch glide and attack/decay envelope, like synth() in dist/game.js.
        static void Render(float[] buffer, int start, double freq, double duration, string type, double gain, double finishFreq)
        {
            finishFreq = Math.Max(40, finishFreq);
            int count = (int)(duration * Rate);
            double phase = 0;
            for (int i = 0; i < count && start + i < buffer.Length; i++)
            {
                double t = (double)i / Rate;
                double f = freq * Math.Pow(finishFreq / freq, t / duration);
                phase += f / Rate;
                double attack = Math.Min(1, t / .012);
                double decay = Math.Exp(-6.9 * Math.Max(0, t - .012) / Math.Max(.02, duration - .012));
                buffer[start + i] += (float)(Wave(type, phase) * gain * attack * decay);
            }
        }

        AudioClip Tone(string key, Action<float[]> fill, double length)
        {
            if (clips.TryGetValue(key, out var clip)) return clip;
            var data = new float[(int)(length * Rate) + 1];
            fill(data);
            clip = AudioClip.Create("Grid tone " + key, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clips[key] = clip;
        }

        void Play(AudioClip clip) { if (Enabled && clip != null) effects.PlayOneShot(clip); }

        public void Beep(double freq, double duration = .11, string type = "sawtooth", double gain = .035)
        {
            if (!Enabled) return;
            string key = "beep" + freq + type + duration + gain;
            Play(Tone(key, data => Render(data, 0, freq, duration, type, gain * 9, freq * .57), duration + .02));
        }

        public void Motif(string kind)
        {
            if (!Enabled) return;
            switch (kind)
            {
                case "turn": Play(Tone(kind, d => Render(d, 0, 520, .08, "triangle", .025 * 9, 780), .1)); break;
                case "wall": Play(Tone(kind, d => Render(d, 0, 180, .15, "sine", .017 * 9, 110), .17)); break;
                case "near":
                    Play(Tone(kind, d => { Render(d, 0, 760, .13, "sine", .028 * 9, 1080); Render(d, (int)(.09 * Rate), 1020, .16, "triangle", .02 * 9, 1350); }, .27));
                    break;
                case "crash": Play(Tone(kind, d => Render(d, 0, 170, .24, "sawtooth", .047 * 9, 55), .26)); break;
                case "derez": Play(Tone(kind, d => Render(d, 0, 710, .27, "triangle", .026 * 9, 70), .29)); break;
                case "win":
                    Play(Tone(kind, d => { double[] notes = { 392, 523, 659 }; for (int i = 0; i < 3; i++) Render(d, (int)(i * .085 * Rate), notes[i], .19, "triangle", .021 * 9, notes[i]); }, .45));
                    break;
            }
        }

        // One looping bar of the browser's sequencer at a fixed intensity.
        AudioClip MusicBar(int level)
        {
            if (music[level] != null) return music[level];
            double intensity = .25 + level * .25;
            double beat = 60 / (100 + intensity * 34) / 2;
            int steps = 16;
            var data = new float[(int)(beat * steps * Rate) + Rate];
            for (int step = 0; step < steps; step++)
            {
                int at = (int)(step * beat * Rate);
                double bass = new[] { 55, 55, 65.41, 49 }[step / 4];
                if (step % 2 == 0) Render(data, at, bass, .27, "sine", (.066 + intensity * .025) * 4.5, bass * .88);
                if (step % 4 == 2) Render(data, at, bass * 2, .16, "triangle", (.015 + intensity * .013) * 4.5, bass * 2.04);
                if (step % 4 == 0 || (intensity > .65 && step % 4 == 3))
                {
                    double note = new[] { 220, 261.63, 329.63, 293.66 }[step / 4];
                    Render(data, at + (int)(.015 * Rate), note, .19, "triangle", (.012 + intensity * .014) * 4.5, note * 1.02);
                }
                if (step == 0 || step == 8) Render(data, at, 440, .75, "sine", (.006 + intensity * .006) * 4.5, 392);
            }
            // Fold the tail into the start so the loop is seamless.
            int length = (int)(beat * steps * Rate);
            for (int i = length; i < data.Length; i++) data[i - length] += data[i];
            Array.Resize(ref data, length);
            var clip = AudioClip.Create("Grid music " + level, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return music[level] = clip;
        }

        // intensity 0..1 picks the bed; null stops music (menus, results, pause).
        public void Music(float? intensity)
        {
            if (Enabled && intensity.HasValue)
            {
                int level = Mathf.Clamp(Mathf.FloorToInt(intensity.Value * 4), 0, 3);
                if (activeMusic == null || level != musicLevel)
                {
                    var next = activeMusic == musicA ? musicB : musicA;
                    int position = activeMusic != null && activeMusic.isPlaying ? activeMusic.timeSamples : 0;
                    next.clip = MusicBar(level);
                    next.Play();
                    next.timeSamples = position % next.clip.samples;
                    activeMusic = next;
                    musicLevel = level;
                }
            }
            else
            {
                activeMusic = null;
                musicLevel = -1;
            }
            float dt = Time.unscaledDeltaTime;
            foreach (var source in new[] { musicA, musicB })
            {
                source.volume = Mathf.MoveTowards(source.volume, source == activeMusic ? .55f : 0, dt * 1.5f);
                if (source.volume <= 0 && source != activeMusic && source.isPlaying) source.Stop();
            }
        }
    }
}
