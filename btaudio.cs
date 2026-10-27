using System.IO;
using System.Collections.Generic;
// btaudio: connect / disconnect / toggle a paired Bluetooth audio device without opening Settings.
// Usage: btaudio.exe [toggle|connect|disconnect|status] [name substring]
// The name defaults to the first line of device.txt next to the exe, else "AirPods".
// Sends KSPROPSETID_BtAudio one-shot reconnect/disconnect to the KS filter behind the device's audio endpoint
// (works with both the inbox Bluetooth stack and Intel SST Bluetooth audio offload).
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
[StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public short vt; short r1, r2, r3; public IntPtr p; IntPtr p2; }
[StructLayout(LayoutKind.Sequential)] struct KSPROPERTY { public Guid Set; public uint Id; public uint Flags; }

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
[ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDeviceTopology {
    void GetConnectorCount(out uint count);
    void GetConnector(uint index, out IConnector connector);
}
[ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IConnector {
    void GetType(out int type);
    void GetDataFlow(out int flow);
    void ConnectTo(IConnector other);
    void Disconnect();
    void IsConnected(out bool connected);
    void GetConnectedTo(out IConnector other);
    void GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    void GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
}
static class Program {
    const int eRender = 0, DEVICE_STATE_ACTIVE = 1, DEVICE_STATE_DISABLED = 2, DEVICE_STATE_UNPLUGGED = 8, CLSCTX_ALL = 0x17;
    const uint KSPROPERTY_ONESHOT_RECONNECT = 0, KSPROPERTY_ONESHOT_DISCONNECT = 1, KSPROPERTY_TYPE_GET = 1;
    const uint IOCTL_KS_PROPERTY = 0x2F0003;
    static readonly Guid KSPROPSETID_BtAudio = new Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    static readonly PROPERTYKEY PKEY_Device_FriendlyName =
        new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref KSPROPERTY input, int inLen, IntPtr output, int outLen, out int returned, IntPtr overlapped);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_List_SizeW(out int len, ref Guid cls, string devId, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_ListW(ref Guid cls, string devId, char[] buffer, int len, int flags);
    static Guid KSCATEGORY_AUDIO = new Guid("6994AD04-93EF-11D0-A3CC-00A0C9223196");

    static string[] AudioInterfaces() {
        int len;
        CM_Get_Device_Interface_List_SizeW(out len, ref KSCATEGORY_AUDIO, null, 0);
        var buf = new char[len];
        CM_Get_Device_Interface_ListW(ref KSCATEGORY_AUDIO, null, buf, len, 0);
        return new string(buf).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
    }

    static int Main(string[] args) {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "toggle";
        string name = args.Length > 1 ? args[1] : DefaultName();

        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
        // All matching output and input endpoints. Skips stale "not present" duplicates left by re-pairing.
        // With Intel SST offload, stereo and Hands-Free sit on different filters and only the Hands-Free
        // one actually acts on the request, so it is sent to every filter found.
        bool connected = false;
        var filters = new List<string>();
        for (int flow = 0; flow < 2; flow++) {
            IMMDeviceCollection devices;
            enumerator.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE | DEVICE_STATE_DISABLED | DEVICE_STATE_UNPLUGGED, out devices);
            int count; devices.GetCount(out count);
            for (int i = 0; i < count; i++) {
                IMMDevice dev; devices.Item(i, out dev);
                string fn = FriendlyName(dev);
                if (fn == null || fn.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int state; dev.GetState(out state);
                if (state == DEVICE_STATE_ACTIVE) connected = true;
                try {
                    string f = FilterPathFor(dev);
                    if (!filters.Contains(f)) filters.Add(f);
                } catch (COMException) { }
            }
        }
        // Intel SST exposes per-device filters as IntcBt{Topo,Wave}{Render,Capture}_<id>_<n>; the Hands-Free
        // TopoRender sibling has no visible endpoint but is the filter that honours the request, so add it.
        foreach (string f in filters.ToArray()) {
            int slash = f.LastIndexOf('\\');
            string[] parts = f.Substring(slash + 1).Split('_');
            if (parts.Length < 3 || !parts[0].StartsWith("intcbttopo", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (string other in AudioInterfaces()) {
                string tail = other.Substring(other.LastIndexOf('\\') + 1);
                if (other.StartsWith(f.Substring(0, slash), StringComparison.OrdinalIgnoreCase)
                    && tail.StartsWith("intcbttopo", StringComparison.OrdinalIgnoreCase)
                    && tail.IndexOf("_" + parts[1] + "_", StringComparison.OrdinalIgnoreCase) >= 0
                    && !filters.Exists(x => string.Equals(x, other, StringComparison.OrdinalIgnoreCase)))
                    filters.Insert(0, other);
            }
        }
        // While disconnected the Hands-Free endpoint disappears, so remember the filters seen while connected.
        string cache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "btaudio." + name + ".cache");
        if (File.Exists(cache)) {
            var cached = new List<string>(File.ReadAllLines(cache));
            foreach (string f in filters) if (!cached.Contains(f)) cached.Add(f);
            filters = cached;
        }
        try { File.WriteAllLines(cache, filters.ToArray()); } catch (IOException) { }
        if (filters.Count == 0) { Console.Error.WriteLine("No audio endpoint matching '" + name + "'"); return 2; }

        if (mode == "status") { Console.WriteLine(name + ": " + (connected ? "connected" : "disconnected")); return 0; }
        if (mode == "toggle") mode = connected ? "disconnect" : "connect";

        var prop = new KSPROPERTY {
            Set = KSPROPSETID_BtAudio,
            Id = mode == "disconnect" ? KSPROPERTY_ONESHOT_DISCONNECT : KSPROPERTY_ONESHOT_RECONNECT,
            Flags = KSPROPERTY_TYPE_GET
        };
        int sent = 0;
        foreach (string filter in filters) {
            using (var h = CreateFileW(filter, 0, 3 /* share read|write */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero)) {
                if (h.IsInvalid) { Console.Error.WriteLine("open " + filter + ": " + new Win32Exception().Message); continue; }
                int returned;
                bool ok = DeviceIoControl(h, IOCTL_KS_PROPERTY, ref prop, Marshal.SizeOf(prop), IntPtr.Zero, 0, out returned, IntPtr.Zero);
                string line = mode + " " + (ok ? "ok" : new Win32Exception().Message) + "  " + filter.Substring(filter.LastIndexOf('\\') + 1);
                Console.WriteLine(line);
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "btaudio.log"), DateTime.Now.ToString("s") + " " + line + Environment.NewLine); } catch (IOException) { }
                if (ok) sent++;
            }
        }
        return sent > 0 ? 0 : 1;
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

    // Device interface path of the KS filter the endpoint is wired to (e.g. \?\INTELAUDIO#...\IntcBtTopoRender_...).
    static string FilterPathFor(IMMDevice dev) {
        var iidTopology = typeof(IDeviceTopology).GUID;
        object o; dev.Activate(ref iidTopology, CLSCTX_ALL, IntPtr.Zero, out o);
        IConnector conn; string path;
        ((IDeviceTopology)o).GetConnector(0, out conn);
        conn.GetDeviceIdConnectedTo(out path);
        int p = path.IndexOf(@"\\?\");
        return p > 0 ? path.Substring(p) : path; // strip "{2}." prefix
    }
}
