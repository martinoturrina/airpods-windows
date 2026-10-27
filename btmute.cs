// btmute: mute/unmute the AirPods mic during calls with a quick stem swipe up-then-down (or down-then-up).
// Windows can't see the AirPods' own press-to-mute, but in-call volume swipes arrive as endpoint volume changes.
// In a Teams meeting it clicks Teams' own mic button (Teams ignores the Windows mic mute); otherwise it mutes the mic endpoint.
// Usage: btmute.exe [name substring]                          (runs in background; logs to btmute.log)
//        btmute.exe --beep-test [name]                       (plays the muted/unmuted beeps on the AirPods)
// The name defaults to the first line of device.txt next to the exe, else "AirPods".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

[StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
[StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public short vt; short r1, r2, r3; public IntPtr p; IntPtr p2; }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
    void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection {
    void GetCount(out int count);
    void Item(int index, out IMMDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
    void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    void OpenPropertyStore(int access, out IPropertyStore store);
    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetState(out int state);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore {
    void GetCount(out int count);
    void GetAt(int index, out PROPERTYKEY key);
    void GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
}
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume {
    void RegisterControlChangeNotify(IntPtr n);
    void UnregisterControlChangeNotify(IntPtr n);
    void GetChannelCount(out uint c);
    void SetMasterVolumeLevel(float l, ref Guid ctx);
    void SetMasterVolumeLevelScalar(float l, ref Guid ctx);
    void GetMasterVolumeLevel(out float l);
    void GetMasterVolumeLevelScalar(out float l);
    void SetChannelVolumeLevel(uint ch, float l, ref Guid ctx);
    void SetChannelVolumeLevelScalar(uint ch, float l, ref Guid ctx);
    void GetChannelVolumeLevel(uint ch, out float l);
    void GetChannelVolumeLevelScalar(uint ch, out float l);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
}
[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient {
    void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    void GetBufferSize(out uint frames);
    void GetStreamLatency(out long latency);
    void GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    void GetMixFormat(out IntPtr format);
    void GetDevicePeriod(out long def, out long min);
    void Start();
    void Stop();
    void Reset();
    void SetEventHandle(IntPtr h);
    void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}
[StructLayout(LayoutKind.Sequential)] struct AudioClientProperties { public uint cbSize; public int bIsOffload; public int eCategory; public int Options; }
[ComImport, Guid("726778CD-F60A-4EDA-82DE-E47610CD78AA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient2 {
    void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    void GetBufferSize(out uint frames);
    void GetStreamLatency(out long latency);
    void GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    void GetMixFormat(out IntPtr format);
    void GetDevicePeriod(out long def, out long min);
    void Start();
    void Stop();
    void Reset();
    void SetEventHandle(IntPtr h);
    void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    void IsOffloadCapable(int category, [MarshalAs(UnmanagedType.Bool)] out bool capable);
    void SetClientProperties(ref AudioClientProperties props);
}
[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioRenderClient {
    void GetBuffer(uint frames, out IntPtr data);
    void ReleaseBuffer(uint frames, uint flags);
}
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionManager2 {
    void GetAudioSessionControl(IntPtr guid, uint flags, out IntPtr control);
    void GetSimpleAudioVolume(IntPtr guid, uint flags, out IntPtr volume);
    void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionEnumerator {
    void GetCount(out int count);
    void GetSession(int index, out IAudioSessionControl session);
}
[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioSessionControl {
    void GetState(out int state);
}

static class Program {
    const int eRender = 0, eCapture = 1, DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 0x17, AudioSessionStateActive = 1;
    const int GestureWindowMs = 1200;   // max time between the two opposite swipes
    const int MergeMs = 300;            // same-direction steps closer than this count as one swipe
    const int CallSettleMs = 2500;      // ignore the volume jump Windows makes when a call starts
    static readonly PROPERTYKEY PKEY_Device_FriendlyName =
        new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
    static Guid ctx = Guid.Empty;
    static string logPath;

    class Watched { public IMMDevice Dev; public IAudioEndpointVolume Vol; public float Level; public int LastDir; public DateTime LastAt; }

    static int Main(string[] args) {
        bool beepTest = args.Length > 0 && args[0] == "--beep-test";
        string name = args.Length > (beepTest ? 1 : 0) ? args[beepTest ? 1 : 0] : DefaultName();
        logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "btmute.log");
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));

        if (beepTest) {
            var outs = Find(enumerator, eRender, name);
            if (outs.Count == 0) { Log("beep test: no active output matching " + name); return 2; }
            Beep(outs[0], true); Thread.Sleep(600); Beep(outs[0], false);
            return 0;
        }

        bool created;
        using (new Mutex(true, @"Local\btmute-" + name, out created)) {
            if (!created) return 0; // already running
            Log("started, watching '" + name + "'");
            Run(enumerator, name);
        }
        return 0;
    }

    static void Run(IMMDeviceEnumerator enumerator, string name) {
        var watched = new Dictionary<string, Watched>();
        IMMDevice mic = null;
        bool inCall = false, mutedByUs = false;
        DateTime callStart = DateTime.MinValue, lastScan = DateTime.MinValue;

        while (true) {
            DateTime now = DateTime.Now;
            try {
                if ((now - lastScan).TotalMilliseconds >= 1000) {
                    lastScan = now;
                    var mics = Find(enumerator, eCapture, name);
                    mic = mics.Count > 0 ? mics[0] : null;
                    bool call = mic != null && MicInUse(mic);
                    if (call && !inCall) {
                        callStart = now;
                        watched.Clear();
                        foreach (var d in Find(enumerator, eRender, name)) {
                            string id; d.GetId(out id);
                            var iid = typeof(IAudioEndpointVolume).GUID; object o;
                            d.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o);
                            var w = new Watched { Dev = d, Vol = (IAudioEndpointVolume)o };
                            w.Vol.GetMasterVolumeLevelScalar(out w.Level);
                            watched[id] = w;
                        }
                        teamsMic = null;
                        Log("call started" + (FindTeamsMic() != null ? " (Teams meeting found)" : ""));
                    } else if (!call && inCall) {
                        Log("call ended");
                        if (mutedByUs && mic != null) { SetMicMute(mic, false); Log("auto-unmuted after call"); }
                        mutedByUs = false;
                        watched.Clear();
                    }
                    inCall = call;
                }

                if (inCall) {
                    foreach (var w in watched.Values) {
                        float level; w.Vol.GetMasterVolumeLevelScalar(out level);
                        if (Math.Abs(level - w.Level) < 0.001f) continue;
                        int dir = level > w.Level ? 1 : -1;
                        Log("volume " + w.Level.ToString("0.000") + " -> " + level.ToString("0.000"));
                        w.Level = level;
                        if ((now - callStart).TotalMilliseconds < CallSettleMs) continue;

                        double sinceLast = (now - w.LastAt).TotalMilliseconds;
                        if (w.LastDir == dir && sinceLast < MergeMs) { w.LastAt = now; continue; }
                        if (w.LastDir == -dir && sinceLast <= GestureWindowMs && mic != null) {
                            bool? teamsMuted = ToggleTeamsMic();
                            bool muted;
                            if (teamsMuted.HasValue) {
                                muted = teamsMuted.Value;
                                Log("Teams " + (muted ? "MUTED" : "UNMUTED"));
                            } else {
                                muted = !GetMicMute(mic);
                                SetMicMute(mic, muted);
                                mutedByUs = muted;
                                Log("mic endpoint " + (muted ? "MUTED" : "UNMUTED"));
                            }
                            Beep(w.Dev, muted);
                            w.LastDir = 0;
                            continue;
                        }
                        w.LastDir = dir; w.LastAt = now;
                    }
                }
            } catch (Exception ex) {
                Log("error: " + ex.Message);   // e.g. AirPods disconnected mid-call; rescan next round
                inCall = false; watched.Clear(); lastScan = DateTime.MinValue;
                Thread.Sleep(1000);
            }
            Thread.Sleep(inCall ? 40 : 1000);
        }
    }

    static AutomationElement teamsMic;

    // The mic button of an open Teams meeting window (any window of ms-teams with AutomationId "microphone-button").
    static AutomationElement FindTeamsMic() {
        if (teamsMic != null) {
            try { var n = teamsMic.Current.Name; return teamsMic; } catch (ElementNotAvailableException) { teamsMic = null; }
        }
        var pids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("ms-teams")) pids.Add(p.Id);
        if (pids.Count == 0) return null;
        var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, "microphone-button");
        // Teams (WebView2) builds its accessibility tree lazily, so the first query can come back empty.
        for (int attempt = 0; attempt < 2 && teamsMic == null; attempt++) {
            if (attempt > 0) Thread.Sleep(300);
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)) {
                try {
                    if (!pids.Contains(w.Current.ProcessId)) continue;
                    teamsMic = w.FindFirst(TreeScope.Descendants, cond);
                    if (teamsMic != null) break;
                } catch (ElementNotAvailableException) { }
            }
        }
        return teamsMic;
    }

    // Clicks the Teams mic button; returns the new muted state, or null when no Teams meeting is open.
    static bool? ToggleTeamsMic() {
        try {
            var btn = FindTeamsMic();
            if (btn == null) return null;
            string before = btn.Current.Name;
            ((InvokePattern)btn.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            string after = before;
            for (int i = 0; i < 20 && after == before; i++) { Thread.Sleep(50); after = btn.Current.Name; }
            return after.StartsWith("Unmute", StringComparison.OrdinalIgnoreCase); // label shows the action: "Unmute mic" = muted
        } catch (Exception ex) {
            Log("Teams toggle failed: " + ex.Message);
            teamsMic = null;
            return null;
        }
    }

    static List<IMMDevice> Find(IMMDeviceEnumerator enumerator, int flow, string name) {
        var list = new List<IMMDevice>();
        IMMDeviceCollection devices;
        enumerator.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out devices);
        int count; devices.GetCount(out count);
        for (int i = 0; i < count; i++) {
            IMMDevice dev; devices.Item(i, out dev);
            string fn = FriendlyName(dev);
            if (fn != null && fn.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) list.Add(dev);
        }
        return list;
    }

    // A call app is capturing from the AirPods mic.
    static bool MicInUse(IMMDevice mic) {
        var iid = typeof(IAudioSessionManager2).GUID; object o;
        mic.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o);
        IAudioSessionEnumerator sessions; ((IAudioSessionManager2)o).GetSessionEnumerator(out sessions);
        int count; sessions.GetCount(out count);
        for (int i = 0; i < count; i++) {
            IAudioSessionControl s; sessions.GetSession(i, out s);
            int state; s.GetState(out state);
            if (state == AudioSessionStateActive) return true;
        }
        return false;
    }

    static IAudioEndpointVolume Volume(IMMDevice dev) {
        var iid = typeof(IAudioEndpointVolume).GUID; object o;
        dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o);
        return (IAudioEndpointVolume)o;
    }
    static bool GetMicMute(IMMDevice mic) { bool m; Volume(mic).GetMute(out m); return m; }
    static void SetMicMute(IMMDevice mic, bool m) { Volume(mic).SetMute(m, ref ctx); }

    // Muted: mute.wav next to the exe, else one low beep. Unmuted: unmute.wav, else two high beeps.
    // Played on the AirPods themselves.
    static void Beep(IMMDevice dev, bool muted) {
        try {
            var iid = typeof(IAudioClient2).GUID; object o;
            dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o);
            var client = (IAudioClient2)o;
            // Communications category: otherwise Windows ducks this "other sound" by 80% during the call.
            var props = new AudioClientProperties { cbSize = 16, eCategory = 3 /* AudioCategory_Communications */ };
            client.SetClientProperties(ref props);
            IntPtr fmt; client.GetMixFormat(out fmt);
            int tag = (ushort)Marshal.ReadInt16(fmt), channels = Marshal.ReadInt16(fmt, 2);
            int rate = Marshal.ReadInt32(fmt, 4), bits = Marshal.ReadInt16(fmt, 14);
            bool isFloat = tag == 3 || (tag == 0xFFFE && Marshal.ReadInt32(fmt, 24) == 3);
            client.Initialize(0, 0, 20000000 /* 2 s */, 0, fmt, IntPtr.Zero);
            uint bufferFrames; client.GetBufferSize(out bufferFrames);

            string wav = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, muted ? "mute.wav" : "unmute.wav");
            List<float> samples = File.Exists(wav) ? LoadWav(wav, rate) : null;
            if (samples == null) {
                var tones = muted ? new[] { new[] { 440.0, 220 } } : new[] { new[] { 880.0, 90 }, new[] { 0.0, 60 }, new[] { 880.0, 90 } };
                samples = new List<float>();
                foreach (var t in tones) {
                    int n = (int)(rate * t[1] / 1000), fade = rate / 200;
                    for (int i = 0; i < n; i++) {
                        double env = Math.Min(1.0, Math.Min(i, n - i) / (double)fade);
                        samples.Add(t[0] == 0 ? 0f : (float)(0.6 * env * Math.Sin(2 * Math.PI * t[0] * i / rate)));
                    }
                }
            }
            uint frames = (uint)Math.Min(samples.Count, (int)bufferFrames);
            var rid = typeof(IAudioRenderClient).GUID; object ro;
            client.GetService(ref rid, out ro);
            var render = (IAudioRenderClient)ro;
            IntPtr data; render.GetBuffer(frames, out data);
            for (int f = 0; f < frames; f++)
                for (int c = 0; c < channels; c++) {
                    int idx = f * channels + c;
                    if (isFloat) Marshal.WriteInt32(data, idx * 4, BitConverter.ToInt32(BitConverter.GetBytes(samples[f]), 0));
                    else if (bits == 16) Marshal.WriteInt16(data, idx * 2, (short)(samples[f] * short.MaxValue));
                    else if (bits == 32) Marshal.WriteInt32(data, idx * 4, (int)(samples[f] * int.MaxValue));
                }
            render.ReleaseBuffer(frames, 0);
            client.Start();
            Thread.Sleep((int)(frames * 1000L / rate) + 150);
            client.Stop();
            Marshal.FreeCoTaskMem(fmt);
        } catch (Exception ex) { Log("beep failed: " + ex.Message); }
    }

    // Reads a PCM (16/24/32-bit) or float WAV as mono, linearly resampled to `rate`. Null if unreadable.
    static List<float> LoadWav(string path, int rate) {
        try {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 12 || BitConverter.ToUInt32(b, 0) != 0x46464952 || BitConverter.ToUInt32(b, 8) != 0x45564157) return null; // RIFF / WAVE
            int tag = 0, channels = 0, srcRate = 0, bits = 0, dataOff = -1, dataLen = 0;
            for (int p = 12; p + 8 <= b.Length; ) {
                string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
                int len = BitConverter.ToInt32(b, p + 4);
                if (id == "fmt ") {
                    tag = BitConverter.ToUInt16(b, p + 8); channels = BitConverter.ToUInt16(b, p + 10);
                    srcRate = BitConverter.ToInt32(b, p + 12); bits = BitConverter.ToUInt16(b, p + 22);
                    if (tag == 0xFFFE) tag = BitConverter.ToUInt16(b, p + 32); // extensible: first 2 bytes of SubFormat
                } else if (id == "data") { dataOff = p + 8; dataLen = Math.Min(len, b.Length - dataOff); break; }
                p += 8 + len + (len & 1);
            }
            if (dataOff < 0 || channels == 0 || srcRate == 0 || (tag != 1 && tag != 3)) { Log("unsupported wav: " + path); return null; }
            int bytesPer = bits / 8, frameCount = dataLen / (bytesPer * channels);
            var mono = new float[frameCount];
            for (int f = 0; f < frameCount; f++) {
                double sum = 0;
                for (int c = 0; c < channels; c++) {
                    int o = dataOff + (f * channels + c) * bytesPer;
                    if (tag == 3) sum += bits == 64 ? BitConverter.ToDouble(b, o) : BitConverter.ToSingle(b, o);
                    else if (bits == 16) sum += BitConverter.ToInt16(b, o) / 32768.0;
                    else if (bits == 24) sum += ((b[o] | (b[o + 1] << 8) | (b[o + 2] << 16)) << 8 >> 8) / 8388608.0;
                    else if (bits == 32) sum += BitConverter.ToInt32(b, o) / 2147483648.0;
                    else if (bits == 8) sum += (b[o] - 128) / 128.0;
                }
                mono[f] = (float)(sum / channels);
            }
            var outp = new List<float>((int)((long)frameCount * rate / srcRate) + 1);
            for (double t = 0; t < frameCount - 1; t += (double)srcRate / rate) {
                int i = (int)t; double frac = t - i;
                outp.Add((float)(mono[i] * (1 - frac) + mono[i + 1] * frac));
            }
            return outp;
        } catch (Exception ex) { Log("wav load failed: " + ex.Message); return null; }
    }

    static string DefaultName() {
        string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device.txt");
        string line = File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        return line.Length > 0 ? line.Split('\n')[0].Trim() : "AirPods";
    }

    static string FriendlyName(IMMDevice dev) {
        IPropertyStore store; dev.OpenPropertyStore(0, out store);
        var key = PKEY_Device_FriendlyName; PROPVARIANT v;
        try { store.GetValue(ref key, out v); } catch (COMException) { return null; }
        return v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null; // VT_LPWSTR
    }

    static void Log(string s) {
        try { File.AppendAllText(logPath, DateTime.Now.ToString("s") + " " + s + Environment.NewLine); } catch (IOException) { }
    }
}
