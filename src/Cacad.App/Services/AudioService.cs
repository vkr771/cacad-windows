using System.Runtime.InteropServices;

namespace Cacad;

/// <summary>Speaker volume and microphone mute through Windows Core Audio.</summary>
public sealed class AudioService
{
    private IAudioEndpointVolume? _speaker;
    private DateTime _speakerAt;

    public float Volume
    {
        get
        {
            var ep = Speaker();
            if (ep == null) return 0;
            try { ep.GetMasterVolumeLevelScalar(out float v); return v; }
            catch { _speaker = null; return 0; }
        }
        set
        {
            var ep = Speaker();
            if (ep == null) return;
            var ctx = Guid.Empty;
            try
            {
                ep.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), ref ctx);
                if (value > 0) ep.SetMute(false, ref ctx);
            }
            catch { _speaker = null; }
        }
    }

    public bool Muted
    {
        get
        {
            var ep = Speaker();
            if (ep == null) return false;
            try { ep.GetMute(out bool m); return m; }
            catch { _speaker = null; return false; }
        }
        set
        {
            var ep = Speaker();
            var ctx = Guid.Empty;
            try { ep?.SetMute(value, ref ctx); } catch { _speaker = null; }
        }
    }

    /// <summary>The default output, re-resolved every few seconds so a new headset is picked up.</summary>
    private IAudioEndpointVolume? Speaker()
    {
        if (_speaker != null && (DateTime.UtcNow - _speakerAt).TotalSeconds < 4) return _speaker;
        _speakerAt = DateTime.UtcNow;
        try
        {
            var en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            en.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var dev);
            _speaker = Activate(dev);
        }
        catch { _speaker = null; }
        return _speaker;
    }

    private static IAudioEndpointVolume Activate(IMMDevice dev)
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object o);
        return (IAudioEndpointVolume)o;
    }

    /// <summary>True when every active microphone is muted.</summary>
    public bool MicMuted
    {
        get
        {
            bool any = false, all = true;
            ForEachMic(ep => { any = true; ep.GetMute(out bool m); all &= m; });
            return any && all;
        }
    }

    public bool HasMic
    {
        get { bool any = false; ForEachMic(_ => any = true); return any; }
    }

    /// <summary>Mute or unmute every active microphone at once, so no app is left listening.</summary>
    public void SetMicMuted(bool mute)
    {
        var ctx = Guid.Empty;
        ForEachMic(ep => ep.SetMute(mute, ref ctx));
    }

    public bool ToggleMic()
    {
        bool mute = !MicMuted;
        SetMicMuted(mute);
        return mute;
    }

    private static void ForEachMic(Action<IAudioEndpointVolume> act)
    {
        try
        {
            var en = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            en.EnumAudioEndpoints(EDataFlow.Capture, 1 /* DEVICE_STATE_ACTIVE */, out var coll);
            coll.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                try
                {
                    coll.Item(i, out var dev);
                    act(Activate(dev));
                }
                catch { /* skip a device that refuses */ }
            }
        }
        catch { }
    }

    // ---------- COM ----------

    private enum EDataFlow { Render = 0, Capture = 1 }
    private enum ERole { Console = 0, Multimedia = 1 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
