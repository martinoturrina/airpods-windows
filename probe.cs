// probe: logs what Windows receives from AirPods stem gestures while the AirPods mic is open (call/HFP mode).
// Usage: probe.exe [seconds=600] [name=AirPods] [nocapture]
// Logs media/volume keys only (no typing), raw HID consumer/telephony reports, and endpoint volume/mute/state changes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
}

class Endpoint { public string Name; public int Flow; public int State; public IAudioEndpointVolume Vol; public float Level = -1; public int Muted = -1; }

class MsgWindow : NativeWindow {
    public MsgWindow() { CreateHandle(new CreateParams()); }
    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x00FF) Probe.OnRawInput(m.LParam);       // WM_INPUT
        else if (m.Msg == 0x0319) Probe.Log("WM_APPCOMMAND cmd=" + (((long)m.LParam >> 16) & 0x0FFF)); // only if targeted at us
        base.WndProc(ref m);
    }
}

static class Probe {
    static readonly PROPERTYKEY PKEY_Device_FriendlyName =
        new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
    static string logPath, name;
    static IMMDeviceEnumerator enumerator;
    static readonly Dictionary<string, Endpoint> endpoints = new Dictionary<string, Endpoint>();
    static IAudioClient capture; // kept alive so the mic stays open
    static MsgWindow window;
    static HookProc hookProc; static IntPtr hook;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfoW(IntPtr dev, uint cmd, IntPtr data, ref uint size);

    public static void Log(string s) {
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s;
        Console.WriteLine(line);
        try { File.AppendAllText(logPath, line + Environment.NewLine); } catch (IOException) { }
    }

    [STAThread]
    static void Main(string[] args) {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 600;
        name = args.Length > 1 ? args[1] : "AirPods";
        bool doCapture = Array.IndexOf(args, "nocapture") < 0;
        logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe.log");
        Log("=== probe start, " + seconds + "s, capture=" + doCapture);

        enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
        if (doCapture) OpenMic();
        PollEndpoints();

        window = new MsgWindow();
        var devs = new[] {
            new RAWINPUTDEVICE { UsagePage = 0x0C, Usage = 0x01, Flags = 0x100, Target = window.Handle }, // consumer control
            new RAWINPUTDEVICE { UsagePage = 0x0B, Usage = 0x05, Flags = 0x100, Target = window.Handle }, // telephony headset
            new RAWINPUTDEVICE { UsagePage = 0x0B, Usage = 0x01, Flags = 0x100, Target = window.Handle }, // telephony phone
        };
        Log("raw input registered: " + RegisterRawInputDevices(devs, (uint)devs.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))));

        hookProc = KeyboardHook;
        hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, hookProc, GetModuleHandle(null), 0);
        Log("keyboard hook: " + (hook != IntPtr.Zero));

        var poll = new Timer { Interval = 50 }; poll.Tick += (s, e) => PollEndpoints(); poll.Start();
        var stop = new Timer { Interval = seconds * 1000 }; stop.Tick += (s, e) => Application.Exit(); stop.Start();
        Application.Run();
        Log("=== probe end");
    }

    static void OpenMic() {
        IMMDeviceCollection devices;
        enumerator.EnumAudioEndpoints(1 /* capture */, 1 /* active */, out devices);
        int count; devices.GetCount(out count);
        for (int i = 0; i < count; i++) {
            IMMDevice dev; devices.Item(i, out dev);
            string fn = FriendlyName(dev);
            if (fn == null || fn.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
            try {
                var iid = typeof(IAudioClient).GUID; object o;
                dev.Activate(ref iid, 0x17, IntPtr.Zero, out o);
                capture = (IAudioClient)o;
                IntPtr fmt; capture.GetMixFormat(out fmt);
                capture.Initialize(0 /* shared */, 0, 10000000, 0, fmt, IntPtr.Zero);
                capture.Start();
                Log("mic opened: " + fn);
                return;
            } catch (Exception ex) { Log("mic open failed on " + fn + ": " + ex.Message); }
        }
        Log("no active capture endpoint matching " + name);
    }

    static void PollEndpoints() {
        var seen = new HashSet<string>();
        for (int flow = 0; flow < 2; flow++) {
            IMMDeviceCollection devices;
            enumerator.EnumAudioEndpoints(flow, 0xF, out devices);
            int count; devices.GetCount(out count);
            for (int i = 0; i < count; i++) {
                IMMDevice dev; devices.Item(i, out dev);
                string fn = FriendlyName(dev);
                if (fn == null || fn.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string id; dev.GetId(out id); seen.Add(id);
                int state; dev.GetState(out state);
                Endpoint ep;
                if (!endpoints.TryGetValue(id, out ep)) {
                    ep = new Endpoint { Name = (flow == 0 ? "out " : "in  ") + fn, Flow = flow, State = -1 };
                    endpoints[id] = ep;
                }
                if (ep.State != state) {
                    Log("STATE  " + ep.Name + ": " + ep.State + " -> " + state);
                    ep.State = state; ep.Vol = null;
                    if (state == 1) {
                        try { var iid = typeof(IAudioEndpointVolume).GUID; object o; dev.Activate(ref iid, 0x17, IntPtr.Zero, out o); ep.Vol = (IAudioEndpointVolume)o; }
                        catch (Exception ex) { Log("volume activate failed: " + ex.Message); }
                    }
                }
                if (ep.Vol == null) continue;
                float level; bool muted;
                ep.Vol.GetMasterVolumeLevelScalar(out level); ep.Vol.GetMute(out muted);
                if (Math.Abs(level - ep.Level) > 0.001f) { if (ep.Level >= 0) Log("VOLUME " + ep.Name + ": " + ep.Level.ToString("0.000") + " -> " + level.ToString("0.000")); ep.Level = level; }
                if ((muted ? 1 : 0) != ep.Muted) { if (ep.Muted >= 0) Log("MUTE   " + ep.Name + ": " + muted); ep.Muted = muted ? 1 : 0; }
            }
        }
    }

    // Only media / volume / browser / F13-F24 keys are logged, never normal typing.
    static IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam) {
        if (code >= 0) {
            int vk = Marshal.ReadInt32(lParam);
            int flags = Marshal.ReadInt32(lParam, 8);
            if ((vk >= 0xA6 && vk <= 0xB7) || (vk >= 0x7C && vk <= 0x87) || vk == 0xFA)
                Log("KEY    vk=0x" + vk.ToString("X2") + " " + ((int)wParam == 0x100 || (int)wParam == 0x104 ? "down" : "up") + (((flags & 0x10) != 0) ? " (injected)" : ""));
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    public static void OnRawInput(IntPtr hRaw) {
        uint headerSize = (uint)(IntPtr.Size == 8 ? 24 : 16), size = 0;
        GetRawInputData(hRaw, 0x10000003, IntPtr.Zero, ref size, headerSize);
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try {
            if (GetRawInputData(hRaw, 0x10000003, buf, ref size, headerSize) != size) return;
            if (Marshal.ReadInt32(buf) != 2) return; // RIM_TYPEHID
            IntPtr hDev = Marshal.ReadIntPtr(buf, 8);
            int sizeHid = Marshal.ReadInt32(buf, (int)headerSize), countHid = Marshal.ReadInt32(buf, (int)headerSize + 4);
            var bytes = new byte[sizeHid * countHid];
            Marshal.Copy(IntPtr.Add(buf, (int)headerSize + 8), bytes, 0, bytes.Length);
            Log("HID    " + BitConverter.ToString(bytes) + "  dev=" + DeviceName(hDev));
        } finally { Marshal.FreeHGlobal(buf); }
    }

    static string DeviceName(IntPtr hDev) {
        uint chars = 0;
        GetRawInputDeviceInfoW(hDev, 0x20000007, IntPtr.Zero, ref chars);
        IntPtr p = Marshal.AllocHGlobal((int)chars * 2 + 2);
        try { GetRawInputDeviceInfoW(hDev, 0x20000007, p, ref chars); string s = Marshal.PtrToStringUni(p); return s.Length > 90 ? s.Substring(0, 90) : s; }
        finally { Marshal.FreeHGlobal(p); }
    }

    static string FriendlyName(IMMDevice dev) {
        IPropertyStore store; dev.OpenPropertyStore(0, out store);
        var key = PKEY_Device_FriendlyName; PROPVARIANT v;
        try { store.GetValue(ref key, out v); } catch (COMException) { return null; }
        return v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null;
    }
}
