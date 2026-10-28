using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NeonStack;

public enum Sfx
{
    Move, Rotate, Lock, HardDrop, Hold,
    Clear1, Clear2, Clear3, Clear4,
    LevelUp, GameOver, Win, Menu, Pause, Start,
}

/// <summary>
/// Software audio mixer. The chiptune loop and every sound effect are synthesised at startup into
/// float buffers; a background thread mixes them and feeds the platform: winmm waveOut on Windows,
/// a raw PCM pipe (pw-cat / paplay / aplay) on Linux. No audio files, no third-party audio library.
/// </summary>
public static unsafe class Audio
{
    const int SR = 44100;
    const int Block = 512;           // ~12 ms of audio per block (keeps SFX latency low)
    const int Buffers = 8;           // ~93 ms of cushion so startup JIT/GC can't underrun

    // ---- public state ----
    public static bool MusicEnabled { get; set; } = true;
    public static bool Available { get; private set; }
    public static bool Synthesised { get; private set; }

    static float[] music = Array.Empty<float>();
    static readonly Dictionary<Sfx, float[]> sounds = new();
    static readonly List<Voice> voices = new();
    static readonly object gate = new();
    static readonly float[] mix = new float[Block];
    static int musicPos;
    static float musicFade;          // fades the loop in over ~0.8 s to avoid a hard start
    static float dcState;
    static bool running;
    static Thread? thread;
    static bool prevM;

    struct Voice
    {
        public float[] Data;
        public int Pos;
    }

    public static void Start()
    {
        try
        {
            music = ComposeMusic();
            BuildSfx();
            Synthesised = true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Audio synthesis failed: " + e.Message);
            return;
        }

        running = true;
        musicFade = 0f;
        thread = new Thread(OutputLoop)
        {
            IsBackground = true,
            Name = "NeonStack Audio",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
    }

    public static void Update(bool[] down)
    {
        bool m = down[(int)Key.M];
        if (m && !prevM)
        {
            MusicEnabled = !MusicEnabled;
            if (MusicEnabled) musicFade = 0f;   // fade back in when re-enabled
            Play(Sfx.Menu);
        }
        prevM = m;
    }

    public static void Play(Sfx id)
    {
        if (!Synthesised || Settings.Current.SfxVolume == 0) return;
        if (!sounds.TryGetValue(id, out var data)) return;

        lock (gate)
        {
            if (voices.Count >= 12) voices.RemoveAt(0);
            voices.Add(new Voice { Data = data });
        }
    }

    public static void Dispose()
    {
        running = false;
        try { thread?.Join(1500); } catch { /* shutting down */ }
        thread = null;
        Available = false;
    }

    /// <summary>Writes the music loop as a WAV (used by --dump-music).</summary>
    public static void DumpMusic(string path)
    {
        float[] track = ComposeMusic();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int data = track.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(data);
        foreach (float s in track) w.Write((short)(Math.Clamp(s, -1f, 1f) * 32000f));
        File.WriteAllBytes(path, ms.ToArray());
    }

    // ------------------------------------------------------------------ mixer

    static void FillBlock(short[] dst)
    {
        Array.Clear(mix);

        float musicVol = MusicEnabled ? Settings.Current.MusicVolume / 10f : 0f;
        float sfxVol = Settings.Current.SfxVolume / 10f;

        lock (gate)
        {
            if (music.Length > 0)
            {
                int mp = musicPos;
                float fade = Math.Min(1f, musicFade);
                if (musicVol > 0 && fade > 0)
                    for (int i = 0; i < Block; i++)
                    {
                        mix[i] += music[mp] * musicVol * fade;
                        if (++mp >= music.Length) mp = 0;
                    }

                musicPos = (musicPos + Block) % music.Length;
                musicFade = Math.Min(1f, musicFade + Block / (0.8f * SR));
            }

            for (int v = voices.Count - 1; v >= 0; v--)
            {
                Voice voice = voices[v];
                int n = Math.Min(Block, voice.Data.Length - voice.Pos);
                for (int i = 0; i < n; i++) mix[i] += voice.Data[voice.Pos + i] * sfxVol;

                voice.Pos += Block;
                if (voice.Pos >= voice.Data.Length) voices.RemoveAt(v);
                else voices[v] = voice;
            }
        }

        // Master DC blocker (also catches any asymmetric SFX) and a little headroom.
        for (int i = 0; i < Block; i++)
        {
            dcState += 0.004f * (mix[i] - dcState);
            float s = MathF.Tanh((mix[i] - dcState) * 1.05f) * 0.82f;
            dst[i] = (short)(Math.Clamp(s, -1f, 1f) * 32000f);
        }
    }

    // ------------------------------------------------------------------ output backends

    static void OutputLoop()
    {
        try
        {
            if (OperatingSystem.IsWindows()) RunWaveOut();
            else if (OperatingSystem.IsLinux()) RunPipe();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Audio disabled: " + e.Message);
            Available = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormat
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    // Must match the native WAVEHDR exactly. dwUser and reserved are DWORD_PTR (8 bytes on x64);
    // declaring them as uint shifts dwFlags and the driver then writes WHDR_DONE into the wrong
    // field, which silently stops the streaming after the first few buffers.
    [StructLayout(LayoutKind.Sequential)]
    struct WaveHeader
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr Reserved;
    }

    const uint WHDR_DONE = 0x00000001;

    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr hwo, int device, ref WaveFormat fmt, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, WaveHeader* header, int size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr hwo, WaveHeader* header, int size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, WaveHeader* header, int size);
    [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);

    static void RunWaveOut()
    {
        var fmt = new WaveFormat
        {
            wFormatTag = 1,                 // PCM
            nChannels = 1,
            nSamplesPerSec = SR,
            nAvgBytesPerSec = SR * 2,
            nBlockAlign = 2,
            wBitsPerSample = 16,
        };

        int openResult = waveOutOpen(out IntPtr hwo, -1 /* WAVE_MAPPER */, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
        if (openResult != 0)
        {
            Console.Error.WriteLine($"Audio disabled: waveOutOpen returned {openResult}");
            return;                         // no output device: stay silent
        }

        int size = Marshal.SizeOf<WaveHeader>();
        IntPtr mem = Marshal.AllocHGlobal(size * Buffers);
        var pins = new GCHandle[Buffers];
        var buffers = new short[Buffers][];

        long blocks = 0, errors = 0;
        long lastReport = Environment.TickCount64;

        try
        {
            for (int i = 0; i < Buffers; i++)
            {
                buffers[i] = new short[Block];
                pins[i] = GCHandle.Alloc(buffers[i], GCHandleType.Pinned);
                var hdr = (WaveHeader*)(mem + i * size);
                *hdr = default;
                hdr->lpData = pins[i].AddrOfPinnedObject();
                hdr->dwBufferLength = Block * 2;

                int prep = waveOutPrepareHeader(hwo, hdr, size);
                FillBlock(buffers[i]);
                int write = waveOutWrite(hwo, hdr, size);
                if (prep != 0 || write != 0)
                {
                    errors++;
                    Console.Error.WriteLine($"Audio: waveOut prepare={prep} write={write}");
                }
            }

            Available = true;

            while (running)
            {
                bool wrote = false;
                for (int i = 0; i < Buffers; i++)
                {
                    var hdr = (WaveHeader*)(mem + i * size);
                    if ((hdr->dwFlags & WHDR_DONE) == 0) continue;

                    FillBlock(buffers[i]);
                    int write = waveOutWrite(hwo, hdr, size);
                    if (write != 0) errors++;
                    blocks++;
                    wrote = true;
                }
                Thread.Sleep(wrote ? 2 : 5);

                if (Environment.GetEnvironmentVariable("NEONSTACK_DEBUG") == "1")
                {
                    long now = Environment.TickCount64;
                    if (now - lastReport >= 1000)
                    {
                        Console.WriteLine($"audio: {blocks} blocks played, {errors} errors, size={size}");
                        lastReport = now;
                    }
                }
            }
        }
        finally
        {
            Available = false;
            waveOutReset(hwo);
            for (int i = 0; i < Buffers; i++)
            {
                var hdr = (WaveHeader*)(mem + i * size);
                waveOutUnprepareHeader(hwo, hdr, size);
                if (pins[i].IsAllocated) pins[i].Free();
            }
            Marshal.FreeHGlobal(mem);
            waveOutClose(hwo);
        }
    }

    static void RunPipe()
    {
        string? exe = FindOnPath("pw-cat") ?? FindOnPath("paplay") ?? FindOnPath("aplay");
        if (exe == null) return;

        string name = Path.GetFileNameWithoutExtension(exe);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        switch (name)
        {
            case "pw-cat":
                psi.ArgumentList.Add("--playback"); psi.ArgumentList.Add("--raw");
                psi.ArgumentList.Add("--format"); psi.ArgumentList.Add("s16");
                psi.ArgumentList.Add("--rate"); psi.ArgumentList.Add("44100");
                psi.ArgumentList.Add("--channels"); psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-");
                break;
            case "paplay":
                psi.ArgumentList.Add("--raw"); psi.ArgumentList.Add("--format=s16le");
                psi.ArgumentList.Add("--rate=44100"); psi.ArgumentList.Add("--channels=1");
                psi.ArgumentList.Add("-");
                break;
            default:
                psi.ArgumentList.Add("-q"); psi.ArgumentList.Add("-t"); psi.ArgumentList.Add("raw");
                psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("S16_LE");
                psi.ArgumentList.Add("-r"); psi.ArgumentList.Add("44100");
                psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("1");
                break;
        }

        using var proc = Process.Start(psi);
        if (proc == null) return;

        Available = true;
        var block = new short[Block];
        var bytes = new byte[Block * 2];

        try
        {
            while (running)
            {
                FillBlock(block);
                Buffer.BlockCopy(block, 0, bytes, 0, bytes.Length);
                proc.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                proc.StandardInput.BaseStream.Flush();
            }
        }
        catch
        {
            // player closed or pipe broke: stay silent
        }
        finally
        {
            Available = false;
            try { proc.StandardInput.Close(); } catch { /* already closed */ }
            try { if (!proc.HasExited) proc.Kill(true); } catch { /* already gone */ }
        }
    }

    // ------------------------------------------------------------------ synthesis

    enum Wave { Square, Pulse, Triangle }

    static double Note(int midi) => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    static float[] New(double seconds) => new float[(int)(seconds * SR)];

    static void Tone(float[] buf, double start, double dur, int midi, float amp, Wave wave, float decay)
    {
        int s0 = (int)(start * SR);
        int n = (int)(dur * SR);
        int release = Math.Max(1, (int)(0.012 * SR));
        double f = Note(midi);

        for (int i = 0; i < n; i++)
        {
            int idx = s0 + i;
            if (idx >= buf.Length) break;

            double t = (double)i / SR;
            double phase = f * t;
            double frac = phase - Math.Floor(phase);
            float v = wave switch
            {
                Wave.Square => frac < 0.5 ? 1f : -1f,
                Wave.Pulse => frac < 0.25 ? 1f : -1f,
                _ => (float)(4.0 * Math.Abs(frac - 0.5) - 1.0),
            };

            float env = MathF.Exp(-decay * (float)t);
            if (i < 64) env *= i / 64f;
            int toEnd = n - i;
            if (toEnd < release) env *= toEnd / (float)release;

            buf[idx] += v * amp * env;
        }
    }

    static void Sweep(float[] buf, double start, double dur, double f0, double f1, float amp, Wave wave, float decay)
    {
        int s0 = (int)(start * SR);
        int n = (int)(dur * SR);
        int release = Math.Max(1, (int)(0.012 * SR));
        double phase = 0;

        for (int i = 0; i < n; i++)
        {
            int idx = s0 + i;
            if (idx >= buf.Length) break;

            double t = (double)i / n;
            double f = f0 + (f1 - f0) * t;
            phase += f / SR;
            double frac = phase - Math.Floor(phase);
            float v = wave switch
            {
                Wave.Square => frac < 0.5 ? 1f : -1f,
                Wave.Pulse => frac < 0.25 ? 1f : -1f,
                _ => (float)(4.0 * Math.Abs(frac - 0.5) - 1.0),
            };

            float env = MathF.Exp(-decay * (float)i / SR);
            int toEnd = n - i;
            if (toEnd < release) env *= toEnd / (float)release;

            buf[idx] += v * amp * env;
        }
    }

    static void Noise(float[] buf, double start, double dur, float amp, float decay, float tone)
    {
        int s0 = (int)(start * SR);
        int n = (int)(dur * SR);
        uint seed = 0x9E3779B9;
        float lp = 0;

        for (int i = 0; i < n; i++)
        {
            int idx = s0 + i;
            if (idx >= buf.Length) break;

            seed = seed * 1664525u + 1013904223u;
            float white = ((seed >> 8) & 0xFFFF) / 32768f - 1f;
            lp += tone * (white - lp);

            float env = MathF.Exp(-decay * (float)i / SR) * Math.Min(1f, i / 40f);
            buf[idx] += lp * amp * env;
        }
    }

    static float[] Normalize(float[] b, float peak)
    {
        float max = 0;
        foreach (float s in b) max = Math.Max(max, Math.Abs(s));
        if (max < 1e-5f) return b;
        float k = peak / max;
        for (int i = 0; i < b.Length; i++) b[i] *= k;
        return b;
    }

    static float[] Arp(int[] notes, double step, float amp, float length)
    {
        var b = New(length);
        for (int i = 0; i < notes.Length; i++)
            Tone(b, i * step, step * 1.8, notes[i], amp, Wave.Pulse, 11);
        return Normalize(b, 0.7f);
    }

    static string? FindOnPath(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // ------------------------------------------------------------------ drums

    static void Kick(float[] buf, double at, float amp)
    {
        int s0 = (int)(at * SR);
        int n = (int)(0.18 * SR);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            int idx = s0 + i;
            if (idx >= buf.Length) break;
            double t = (double)i / SR;
            double f = 115.0 - 60.0 * Math.Min(1.0, t / 0.10);   // 115 -> 55 Hz, no subsonic sweep
            phase += f / SR;
            float env = MathF.Exp(-22f * (float)t) * Math.Min(1f, i / 40f);
            buf[idx] += (float)Math.Sin(phase * 2 * Math.PI) * amp * env;
        }
    }

    static void Snare(float[] buf, double at, float amp, ref uint noise)
    {
        int s0 = (int)(at * SR);
        int n = (int)(0.16 * SR);
        float prev = 0;
        for (int i = 0; i < n; i++)
        {
            int idx = s0 + i;
            if (idx >= buf.Length) break;
            double t = (double)i / SR;
            noise = noise * 1664525u + 1013904223u;
            float white = ((noise >> 8) & 0xFFFF) / 32768f - 1f;
            float hp = white - prev * 0.7f;
            prev = white;
            float env = MathF.Exp(-26f * (float)t);
            buf[idx] += (hp * 0.75f + (float)Math.Sin(2 * Math.PI * 185 * t) * 0.25f) * amp * env;
        }
    }

    static void Hat(float[] buf, double at, float amp, ref uint noise)
    {
        int s0 = (int)(at * SR);
        int n = (int)(0.05 * SR);
        float prev = 0;
        for (int i = 0; i < n; i++)
        {
            int idx = s0 + i;
            if (idx >= buf.Length) break;
            noise = noise * 1664525u + 1013904223u;
            float white = ((noise >> 8) & 0xFFFF) / 32768f - 1f;
            float hp = white - prev;
            prev = white;
            buf[idx] += hp * amp * MathF.Exp(-70f * (float)i / SR);
        }
    }

    // ------------------------------------------------------------------ sfx bank

    static void BuildSfx()
    {
        float[] b;

        b = New(0.07);
        Tone(b, 0, 0.055, 84, 0.25f, Wave.Square, 36);
        sounds[Sfx.Move] = Normalize(b, 0.45f);

        b = New(0.12);
        Tone(b, 0, 0.055, 79, 0.20f, Wave.Pulse, 26);
        Tone(b, 0.045, 0.06, 86, 0.20f, Wave.Pulse, 26);
        sounds[Sfx.Rotate] = Normalize(b, 0.55f);

        b = New(0.18);
        Sweep(b, 0, 0.15, 210, 95, 0.40f, Wave.Square, 26);
        Noise(b, 0, 0.04, 0.25f, 80, 1f);
        sounds[Sfx.Lock] = Normalize(b, 0.62f);

        b = New(0.26);
        Sweep(b, 0, 0.22, 1100, 130, 0.22f, Wave.Pulse, 11);
        Noise(b, 0, 0.16, 0.30f, 16, 0.5f);
        sounds[Sfx.HardDrop] = Normalize(b, 0.65f);

        b = New(0.16);
        Tone(b, 0, 0.05, 76, 0.22f, Wave.Pulse, 28);
        Tone(b, 0.05, 0.07, 88, 0.22f, Wave.Pulse, 28);
        sounds[Sfx.Hold] = Normalize(b, 0.60f);

        sounds[Sfx.Clear1] = Arp(new[] { 72, 79 }, 0.055, 0.26f, 0.30f);
        sounds[Sfx.Clear2] = Arp(new[] { 72, 76, 79 }, 0.055, 0.26f, 0.38f);
        sounds[Sfx.Clear3] = Arp(new[] { 72, 76, 79, 84 }, 0.055, 0.26f, 0.46f);

        b = Arp(new[] { 72, 76, 79, 84, 88, 91 }, 0.05, 0.26f, 0.62f);
        Noise(b, 0.30, 0.25, 0.10f, 9, 0.8f);
        sounds[Sfx.Clear4] = Normalize(b, 0.78f);

        b = New(0.95);
        int[] up = { 72, 76, 79, 84, 88 };
        for (int i = 0; i < up.Length; i++) Tone(b, i * 0.11, 0.15, up[i], 0.22f, Wave.Pulse, 8);
        Tone(b, 0.60, 0.30, 91, 0.18f, Wave.Triangle, 3);
        sounds[Sfx.LevelUp] = Normalize(b, 0.80f);

        b = New(1.15);
        int[] down = { 60, 57, 53, 48 };
        for (int i = 0; i < down.Length; i++) Tone(b, i * 0.16, 0.22, down[i], 0.24f, Wave.Square, 7);
        sounds[Sfx.GameOver] = Normalize(b, 0.75f);

        b = New(1.4);
        int[] win = { 72, 76, 79, 84, 88, 91 };
        for (int i = 0; i < win.Length; i++) Tone(b, i * 0.14, 0.20, win[i], 0.20f, Wave.Pulse, 6);
        Tone(b, 0.90, 0.45, 96, 0.16f, Wave.Triangle, 2.5f);
        sounds[Sfx.Win] = Normalize(b, 0.85f);

        b = New(0.05);
        Tone(b, 0, 0.04, 96, 0.20f, Wave.Square, 40);
        sounds[Sfx.Menu] = Normalize(b, 0.35f);

        b = New(0.22);
        Tone(b, 0, 0.07, 79, 0.20f, Wave.Pulse, 24);
        Tone(b, 0.07, 0.09, 67, 0.20f, Wave.Pulse, 24);
        sounds[Sfx.Pause] = Normalize(b, 0.50f);

        b = New(0.32);
        Tone(b, 0, 0.07, 72, 0.20f, Wave.Pulse, 20);
        Tone(b, 0.07, 0.07, 79, 0.20f, Wave.Pulse, 20);
        Tone(b, 0.14, 0.10, 84, 0.20f, Wave.Pulse, 18);
        sounds[Sfx.Start] = Normalize(b, 0.60f);
    }

    // ------------------------------------------------------------------ music

    static float[] ComposeMusic()
    {
        const double bpm = 128.0;
        double beat = 60.0 / bpm;
        double bar = beat * 4;
        int loopLen = (int)(bar * 8 * SR);
        int tail = (int)(0.75 * SR);
        var buf = new float[loopLen + tail];

        int[][] chords =
        {
            new[] { 45, 48, 52 },   // Am
            new[] { 41, 45, 48 },   // F
            new[] { 48, 52, 55 },   // C
            new[] { 43, 47, 50 },   // G
        };
        // Bass an octave above the old 33/29/36/31: 43-65 Hz was mostly inaudible on laptop
        // speakers and just made them distort.
        int[] roots = { 45, 41, 48, 43 };
        int[] lead =
        {
            76, 72, 71, 72, 69, 0, 65, 69,
            72, 71, 67, 76, 74, 72, 71, 74,
        };

        uint noise = 0x12345u;

        for (int b = 0; b < 8; b++)
        {
            int chord = b / 2;
            double t0 = b * bar;
            int[] notes = chords[chord];

            for (int e = 0; e < 8; e++)
            {
                int midi = roots[chord] + (e % 4 == 3 ? 7 : 0);
                Tone(buf, t0 + e * beat * 0.5, beat * 0.46, midi, 0.17f, Wave.Square, 5.5f);
            }

            for (int s = 0; s < 16; s++)
            {
                int degree = (s + (b % 2) * 2) % 4;
                int midi = notes[degree % 3] + 12 + (degree >= 3 ? 12 : 0);
                Tone(buf, t0 + s * beat * 0.25, beat * 0.22, midi, 0.075f, Wave.Pulse, 9f);
            }

            Kick(buf, t0, 0.28f);
            Kick(buf, t0 + beat * 2, 0.24f);
            Snare(buf, t0 + beat, 0.19f, ref noise);
            Snare(buf, t0 + beat * 3, 0.19f, ref noise);
            for (int h = 0; h < 8; h++) Hat(buf, t0 + beat * 0.5 * h, h % 2 == 0 ? 0.05f : 0.032f, ref noise);

            int n0 = lead[b * 2], n1 = lead[b * 2 + 1];
            if (n0 > 0) Tone(buf, t0, beat * 0.95, n0, 0.085f, Wave.Triangle, 1.6f);
            if (n1 > 0) Tone(buf, t0 + beat, beat * 0.95, n1, 0.085f, Wave.Triangle, 1.6f);
        }

        // Gentle low-pass, then a DC blocker (~30 Hz). The old track carried a -439 DC offset,
        // which thumped when the stream started and wasted headroom.
        float lp = 0, dc = 0;
        for (int i = 0; i < buf.Length; i++)
        {
            lp += 0.35f * (buf[i] - lp);
            dc += 0.0045f * (lp - dc);
            buf[i] = MathF.Tanh((lp - dc) * 1.1f) * 0.66f;
        }
        for (int i = 0; i < tail && loopLen + i < buf.Length; i++) buf[i] += buf[loopLen + i];

        Array.Resize(ref buf, loopLen);
        return buf;
    }
}
