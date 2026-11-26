using System.Runtime.InteropServices;

namespace Cacad;

/// <summary>The sound the PC is playing, as four frequency bands (bass, low-mid, mid, treble), from a WASAPI
/// loopback capture of the default output. Runs on its own thread, only while started; the pill's visualizer reads
/// <see cref="Level"/> to dance to the real music instead of a made-up pattern.</summary>
public sealed class AudioLoopback : IDisposable
{
    private Thread? _thread;
    private volatile bool _run;
    private readonly float[] _levels = new float[4];
    private long _stamp;

    /// <summary>True while levels are arriving (something is playing and the capture runs).</summary>
    public bool Fresh => Environment.TickCount64 - Interlocked.Read(ref _stamp) < 400;

    public float Level(int band) => Volatile.Read(ref _levels[Math.Clamp(band, 0, 3)]);

    public void Start()
    {
        if (_thread != null) return;
        _run = true;
        int gen = ++_gen;
        _thread = new Thread(() => Loop(gen)) { IsBackground = true, Name = "cacad loopback" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Stop()
    {
        _run = false;
        _thread = null;
    }

    // Four band-pass filters (RBJ biquads), one per visualizer column.
    private static readonly float[] Centres = { 90, 450, 2000, 7000 };

    private struct Biquad
    {
        public float b0, b1, b2, a1, a2, z1, z2;

        public static Biquad BandPass(float f, float q, float rate)
        {
            double w = 2 * Math.PI * f / rate, alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            return new Biquad
            {
                b0 = (float)(alpha / a0), b1 = 0, b2 = (float)(-alpha / a0),
                a1 = (float)(-2 * Math.Cos(w) / a0), a2 = (float)((1 - alpha) / a0),
            };
        }

        public float Process(float x)
        {
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    private volatile int _gen;

    /// <summary>One capture session; it ends when stopped, or when a newer session has started.</summary>
    private void Loop(int gen)
    {
        bool Live() => _run && gen == _gen;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr fmt = IntPtr.Zero;
        try
        {
            // Created by its class ID: AudioService declares the same COM class, and two [ComImport] declarations of
            // one class clash ("cannot cast MMDeviceEnumeratorCom to MMDeviceEnumeratorCom").
            var en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            int hr = en.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var dev);
            Log($"endpoint hr={hr:X}");
            if (hr != 0) return;
            var iid = typeof(IAudioClient).GUID;
            hr = dev.Activate(ref iid, 23, IntPtr.Zero, out object o);
            Log($"activate hr={hr:X}");
            if (hr != 0) return;
            client = (IAudioClient)o;
            if (client.GetMixFormat(out fmt) != 0) return;

            // WAVEFORMATEX: tag, channels, rate, ..., bits.
            ushort tag = (ushort)Marshal.ReadInt16(fmt, 0);
            int channels = Marshal.ReadInt16(fmt, 2), rate = Marshal.ReadInt32(fmt, 4), bits = Marshal.ReadInt16(fmt, 14);
            bool isFloat = tag == 3 || (tag == 0xFFFE && bits == 32);
            if (channels <= 0 || rate <= 0) return;

            const uint LOOPBACK = 0x00020000;
            Log($"format tag={tag:X} ch={channels} rate={rate} bits={bits} float={isFloat}");
            hr = client.Initialize(0, LOOPBACK, 1_000_000, 0, fmt, IntPtr.Zero);
            Log($"initialize hr={hr:X}");
            if (hr != 0) return;
            var cid = typeof(IAudioCaptureClient).GUID;
            if (client.GetService(ref cid, out object co) != 0) return;
            capture = (IAudioCaptureClient)co;
            hr = client.Start();
            Log($"start hr={hr:X}");
            if (hr != 0) return;

            var filters = Centres.Select(f => Biquad.BandPass(f, 0.9f, rate)).ToArray();
            var energy = new double[4];
            var peaks = new float[] { 0.05f, 0.05f, 0.05f, 0.05f };
            int count = 0, block = rate / 60; // ~60 updates a second
            long lastPacket = Environment.TickCount64;

            while (Live())
            {
                Thread.Sleep(8);
                capture.GetNextPacketSize(out uint packet);
                if (packet == 0)
                {
                    // Loopback goes quiet when nothing plays: let the levels fall.
                    if (Environment.TickCount64 - lastPacket > 120)
                        for (int b = 0; b < 4; b++) Volatile.Write(ref _levels[b], _levels[b] * 0.85f);
                    continue;
                }
                while (packet > 0 && Live())
                {
                    if (capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) != 0) break;
                    bool silent = (flags & 0x2) != 0;
                    unsafe
                    {
                        float* fp = (float*)data;
                        short* sp = (short*)data;
                        for (uint f = 0; f < frames; f++)
                        {
                            float mono = 0;
                            if (!silent)
                            {
                                for (int ch = 0; ch < channels; ch++)
                                    mono += isFloat ? fp[f * channels + ch] : sp[f * channels + ch] / 32768f;
                                mono /= channels;
                            }
                            for (int b = 0; b < 4; b++)
                            {
                                float y = filters[b].Process(mono);
                                energy[b] += y * y;
                            }
                            if (++count >= block)
                            {
                                for (int b = 0; b < 4; b++)
                                {
                                    float rms = (float)Math.Sqrt(energy[b] / count);
                                    // Automatic gain per band: follow the loudest recent level, slowly letting go.
                                    peaks[b] = Math.Max(rms, peaks[b] * 0.997f);
                                    float v = Math.Clamp(rms / Math.Max(peaks[b], 0.004f), 0, 1);
                                    v = MathF.Pow(v, 0.8f);
                                    float old = _levels[b];
                                    // Quick to rise, slower to fall, so the bars snap to a beat and settle.
                                    Volatile.Write(ref _levels[b], v > old ? old + (v - old) * 0.7f : old + (v - old) * 0.18f);
                                    energy[b] = 0;
                                }
                                count = 0;
                                Interlocked.Exchange(ref _stamp, Environment.TickCount64);
                                if (++_logged % 120 == 0) Log($"levels {_levels[0]:0.00} {_levels[1]:0.00} {_levels[2]:0.00} {_levels[3]:0.00} silent={silent}");
                            }
                        }
                    }
                    capture.ReleaseBuffer(frames);
                    lastPacket = Environment.TickCount64;
                    capture.GetNextPacketSize(out packet);
                }
            }
            client.Stop();
        }
        catch (Exception ex) { Log("failed: " + ex); /* no device, exclusive mode, or the device went away: the visualizer falls back to its pattern */ }
        finally
        {
            if (fmt != IntPtr.Zero) Marshal.FreeCoTaskMem(fmt);
            if (capture != null) Marshal.ReleaseComObject(capture);
            if (client != null) Marshal.ReleaseComObject(client);
            for (int b = 0; b < 4; b++) Volatile.Write(ref _levels[b], 0);
        }
    }

    public void Dispose() => Stop();

    private int _logged;
    private static readonly bool Logging = Environment.GetEnvironmentVariable("CACAD_LOOPLOG") == "1";
    private static void Log(string s)
    {
        if (Logging) try { File.AppendAllText(Path.Combine(SettingsStore.DataDir, "loopback.log"), $"{DateTime.Now:HH:mm:ss.fff} {s}\r\n"); } catch { }
    }

    // ---------- COM ----------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
