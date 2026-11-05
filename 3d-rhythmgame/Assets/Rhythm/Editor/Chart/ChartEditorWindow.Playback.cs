using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Song;
using UnityEditor;
using UnityEngine;

namespace Arte.Rhythm.Editor
{
    // 再生・メトロノーム・テンポのタップ（ChartEditorWindow の一部．ChartEditorWindow.cs と同じクラス）
    partial class ChartEditorWindow
    {
        void SetTempo(double bpm, double firstBeatSec, int beatsPerBar)
        {
            var p = new TempoPoint { TimeSec = firstBeatSec, Bpm = bpm, BeatsPerBar = beatsPerBar };
            if (data.Tempo.Count == 0) data.Tempo.Add(p);
            else data.Tempo[0] = p;
            tempo = data.TempoMap();
            dirty = true;
        }

        /// <summary>
        /// 曲に合わせて叩いた拍からテンポと小節の頭を合わせる（B = 拍，N = 小節の 1 拍目．計算は TapTempo）．
        /// 間が 2 秒以上空いたら・時刻が戻ったら新しく数え直す．
        /// </summary>
        void Tap(double t, bool downbeat)
        {
            // 2 秒以上空いた・時刻が戻った（曲の頭からもう一度）ら数え直す
            if (taps.Count > 0 && (t - taps[taps.Count - 1].Time > 2.0 || t <= taps[taps.Count - 1].Time)) taps.Clear();
            if (taps.Count == 0)
            {
                var p0 = data.Tempo.Count > 0 ? data.Tempo[0] : null;
                tapBase = p0 == null ? null : new TempoPoint { TimeSec = p0.TimeSec, Bpm = p0.Bpm, BeatsPerBar = p0.BeatsPerBar };
            }
            taps.Add(new TapTempo.Tap(t, downbeat));
            // 叩くたびに書き換えたテンポを基準にすると，最初の数回で決まったテンポから動かなくなるので，叩き始める前のものを使う
            var current = tapBase;
            int bpb = current?.BeatsPerBar ?? 4;
            var r = TapTempo.Fit(taps, current, bpb);
            if (r == null)
            {
                tapMessage = $"タップ {taps.Count} 回（{TapTempo.MinTaps} 回から合わせ始める）";
                return;
            }
            var res = r.Value;
            SetTempo(res.KeptTempo ? current.Bpm : Math.Round(res.Bpm, 2), Math.Round(res.FirstBarSec, 4), bpb);
            int downs = taps.Count(x => x.Downbeat);
            tapMessage = $"タップ {taps.Count} 回（うち 1 拍目 {downs}）: " +
                         (res.KeptTempo ? $"テンポは元のまま（{current.Bpm} BPM．叩いた拍だけで求めると {res.FittedBpm:F2}）" : $"{res.Bpm:F2} BPM") +
                         $"，小節の頭 {res.FirstBarSec:F3} 秒（{res.ShiftSec * 1000:+0;-0} ms），ばらつき {res.ErrorSec * 1000:F0} ms" +
                         (downs == 0 ? "．1 拍目で N を叩くとどの拍が小節の頭かも合わせる" : "");
        }

        void SetMetronome(bool on)
        {
            metronome = on;
            double ph = PlayheadSec;
            if (!double.IsNaN(ph)) PlayFrom(ph);
        }

        /// <summary>曲時刻 t から鳴らす（メトロノームが入っていれば，曲にクリック音を混ぜた音を鳴らす．ずれないように 1 本の音にする）．</summary>
        void PlayFrom(double t)
        {
            var clip = metronome ? MetronomeClip() : song.audio;
            EditorAudio.Play(clip ?? song.audio, t + data.AudioStartSec);
        }

        AudioClip MetronomeClip()
        {
            var src = song.audio;
            if (src == null || !src.LoadAudioData()) return null;
            string key = $"{src.GetInstanceID()}:{string.Join(";", tempo.Points.Select(p => $"{p.TimeSec},{p.Bpm},{p.BeatsPerBar}"))}:{data.AudioStartSec}";
            if (metronomeClip != null && metronomeKey == key) return metronomeClip;
            int ch = src.channels, rate = src.frequency;
            var samples = new float[src.samples * ch];
            if (!src.GetData(samples, 0)) return null;
            // 曲を少し下げてクリック音を足す（小節の頭 2000Hz，ほかの拍 1200Hz，30ms で減衰）
            for (int i = 0; i < samples.Length; i++) samples[i] *= 0.7f;
            double length = (double)src.samples / rate - data.AudioStartSec;
            int clickLen = (int)(0.03 * rate);
            foreach (var (sec, isBeat, isBar) in tempo.GridLines(0, length, 1))
            {
                if (!isBeat && !isBar) continue;
                long start = (long)Math.Round((sec + data.AudioStartSec) * rate);
                double freq = isBar ? 2000 : 1200;
                float amp = isBar ? 0.6f : 0.4f;
                for (int k = 0; k < clickLen; k++)
                {
                    long s = start + k;
                    if (s < 0 || s >= src.samples) continue;
                    float v = amp * (float)(Math.Sin(2 * Math.PI * freq * k / rate) * Math.Exp(-k / (0.006 * rate)));
                    for (int c = 0; c < ch; c++) samples[s * ch + c] += v;
                }
            }
            // エディタの試聴はスクリプトで作った AudioClip を鳴らせないので，WAV に書いて読み込む（ArteTemp は git に入れない）
            Directory.CreateDirectory(MetronomeDir);
            WriteWav(MetronomePath, samples, ch, rate);
            AssetDatabase.ImportAsset(MetronomePath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(MetronomePath) is AudioImporter importer)
            {
                var settings = importer.defaultSampleSettings;
                if (settings.compressionFormat != AudioCompressionFormat.PCM || settings.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }
            }
            metronomeClip = AssetDatabase.LoadAssetAtPath<AudioClip>(MetronomePath);
            metronomeKey = metronomeClip != null ? key : null;
            return metronomeClip;
        }

        static void WriteWav(string path, float[] samples, int channels, int rate)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                foreach (float v in samples) w.Write((short)(Mathf.Clamp(v, -1f, 1f) * 32767));
            }
        }

        void TogglePlay()
        {
            if (EditorAudio.IsPlaying)
            {
                double ph = PlayheadSec;
                EditorAudio.Stop();
                if (!double.IsNaN(ph)) cursorSec = ph;
            }
            else PlayFrom(cursorSec);
        }
    }
}
