# airpods-windows

Small Windows tools that make AirPods nicer to use on a PC, without Apple's private protocol and without kernel drivers:

| Tool | What it does |
|---|---|
| `btaudio.exe` / `btaudiow.exe` | Connect, disconnect or toggle a paired Bluetooth audio device in about a second, with no Settings app. `btaudiow` is the windowless build for hotkeys and mouse buttons. |
| `btmute.exe` | Mute/unmute your call from the AirPods with a quick stem swipe up then down. You hear one low beep for muted and two high beeps for unmuted. In a Teams meeting it clicks Teams' own mic button; in other apps it mutes the Windows microphone. |
| `probe.exe` | Diagnostic: logs what Windows receives from stem presses and swipes (media keys, HID reports, volume and mute changes). |

Tested on Windows 11 with AirPods Pro 2 and Intel Bluetooth audio offload (Intel Smart Sound Technology).

## Build

Run `build.cmd`. It uses the C# compiler that ships with Windows (.NET Framework 4.x), so no SDK or Visual Studio is needed.

## Setup

1. Put your AirPods' Bluetooth name (or part of it) in `device.txt` next to the exes. Without it the tools look for "AirPods".
2. **Connect/disconnect:** bind `btaudiow.exe` to a hotkey, for example a Start Menu shortcut with a shortcut key, or a mouse button through OpenLogi or Logi Options+. With no arguments it toggles.
   ```
   btaudio.exe [toggle|connect|disconnect|status] [name]
   ```
3. **Swipe to mute:** put a shortcut to `btmute.exe` in `shell:startup`. It idles until an app uses the AirPods microphone, then watches for the gesture.
   ```
   btmute.exe [name]
   btmute.exe --beep-test [name]
   ```
   To use your own sounds, put `mute.wav` and `unmute.wav` next to `btmute.exe`.

Logs (`btaudio.log`, `btmute.log`) are written next to the exes.

## How it works

**Connect/disconnect.** Windows' "Connect" button sends a one-shot Bluetooth audio reconnect request (`KSPROPSETID_BtAudio`, `KSPROPERTY_ONESHOT_RECONNECT` / `DISCONNECT`) to the device's kernel-streaming filter. `btaudio` finds the filters behind the device's audio endpoints and sends the same `IOCTL_KS_PROPERTY`.

With Intel Bluetooth audio offload, the filters are `IntcBt{Topo,Wave}{Render,Capture}_<id>_<n>` on the Intel audio controller. Only the Hands-Free `TopoRender` filter acts on the request, and it has no visible endpoint, so `btaudio` adds the `Topo` siblings of every filter it finds. It caches them in `<name>.cache` because the Hands-Free endpoint disappears while disconnected. After re-pairing, delete the cache.

**Swipe to mute.** Apple's press-to-mute runs over Apple's private AirPods protocol (AAP/AACP, L2CAP PSM 0x1001). Windows needs a kernel driver to open that channel, and the available drivers need Test Mode. Without it, a press during a call plays the "not available" tone and sends nothing, and a double press ends the call through the standard hands-free profile.

Volume swipes during a call *do* reach Windows, as changes to the AirPods' output endpoint volume. `btmute` treats two opposite steps within 1.2 s as the gesture, so the volume ends where it started and single swipes still adjust it.

Teams ignores the Windows microphone mute, so in a Teams meeting `btmute` presses the meeting's mic button through UI Automation (`AutomationId = "microphone-button"`) and reads the button label to pick the beep. In other apps it toggles the microphone endpoint mute and unmutes automatically when the call ends. The beep is played on the AirPods as a communications stream so Windows doesn't duck it during the call.

## Limitations

- The swipe gesture needs room to move: at maximum volume, start with a swipe down.
- The Teams integration depends on Teams' UI; a Teams update could rename the mic button.
- Press once still plays the "not available" tone, and press twice still ends the call.

## License

MIT, see [LICENSE](LICENSE).
