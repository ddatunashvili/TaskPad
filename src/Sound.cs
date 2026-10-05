using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace TaskPad
{
    /// Small optional sound effects (Settings → Sound effects, off by default).
    /// The tones are synthesised once in memory, so the exe stays a single file.
    public static class Sound
    {
        public enum Fx { Done, Undone, Comment, Reminder }

        static readonly Dictionary<Fx, SoundPlayer> Players = new Dictionary<Fx, SoundPlayer>();

        public static void Play(Fx fx)
        {
            if (!Workspace.Settings.Sounds) return;
            try
            {
                if (!Players.TryGetValue(fx, out var p))
                {
                    p = new SoundPlayer(new MemoryStream(Render(fx)));
                    p.Load();
                    Players[fx] = p;
                }
                p.Play(); // async; a newer sound simply replaces the one still playing
            }
            catch { }
        }

        /// (frequency Hz, start s, length s) notes with a soft attack and exponential fade.
        static byte[] Render(Fx fx)
        {
            (double F, double At, double Len)[] notes;
            double gain;
            switch (fx)
            {
                case Fx.Done: notes = new[] { (880.0, 0.0, 0.16), (1318.5, 0.07, 0.22) }; gain = 0.22; break;      // A5 → E6, a bright "ding"
                case Fx.Undone: notes = new[] { (523.25, 0.0, 0.10) }; gain = 0.16; break;                         // soft low tick
                case Fx.Comment: notes = new[] { (659.25, 0.0, 0.08), (987.77, 0.05, 0.12) }; gain = 0.18; break;  // quick "pop"
                default: notes = new[] { (783.99, 0.0, 0.3), (1046.5, 0.16, 0.3), (1318.5, 0.32, 0.45) }; gain = 0.22; break; // chime
            }
            const int rate = 44100;
            double total = 0;
            foreach (var n in notes) total = Math.Max(total, n.At + n.Len);
            int count = (int)(total * rate) + rate / 50;
            var samples = new double[count];
            foreach (var n in notes)
            {
                int start = (int)(n.At * rate), len = (int)(n.Len * rate);
                for (int i = 0; i < len && start + i < count; i++)
                {
                    double t = (double)i / rate;
                    double env = Math.Min(1, t / 0.004) * Math.Exp(-t * 5.5 / n.Len);
                    // a touch of the octave makes it sound less like a test tone
                    samples[start + i] += env * (Math.Sin(2 * Math.PI * n.F * t) + 0.25 * Math.Sin(4 * Math.PI * n.F * t));
                }
            }

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + count * 2);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(count * 2);
            foreach (var s in samples) w.Write((short)(Math.Max(-1, Math.Min(1, s * gain)) * short.MaxValue));
            w.Flush();
            return ms.ToArray();
        }
    }
}
