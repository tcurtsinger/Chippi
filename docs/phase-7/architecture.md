# Phase 7 architecture: Capture, apps and system

Kayla, 2026-11-06. Research for task 467, read against main `9b162e5`. Behavior is in Karl's [behavior-spec-a.md](behavior-spec-a.md) (Capture) and Herb's [behavior-spec-b.md](behavior-spec-b.md) (Apps and System); where each feature lives is Lumen's [surfaces.md](surfaces.md). This doc says what each feature needs from Windows, what runs as a standard user and what needs admin, what it costs on ARM64, which libraries it adds, how each is tested without Travis's real apps, files or clipboard, and the build order.

Facts carry a source in brackets (linked at the end) or say they were measured here. **Inference** marks what I reasoned but didn't check.

**Measured on Travis's PC** (2026-11-06, a read-only probe: no screen capture, no clipboard, no settings or brightness changed):

| What | Result |
|---|---|
| DDC/CI | Both monitors answer. OMEN: brightness 54/100, contrast 100/100. Samsung: 60/100, 75/100. Each read takes 55 to 65 ms. Reading the capabilities string takes 6.6 s (OMEN) and 1.3 s (Samsung). |
| Laptop brightness (WMI `WmiMonitorBrightness`) | No instances, as expected on a desktop |
| OCR (`Windows.Media.Ocr`) | en-US installed, `MaxImageDimension` 10000 |
| Windows.Graphics.Capture | Supported. `IsCursorCaptureEnabled`, `IsBorderRequired`, `MinUpdateInterval`, `IncludeSecondaryWindows`, `DirtyRegionMode` and `GraphicsCaptureAccess` are all present (build 26300) |
| Video encoders (Media Foundation) | H.264: NVIDIA, AMD, Microsoft DX12 and the software `H264 Encoder MFT`. HEVC: NVIDIA, AMD, DX12, the HEVC extension. AV1: NVIDIA. VP9: the VP9 extension. AAC: 1 |
| Image encoders (WIC) | BMP, GIF, JPEG, PNG, TIFF, JPEG XR, DDS, HEIF, JPEG XL. No WebP or AVIF encoder |
| Ports | 596 TCP v4 rows read in 1.6 ms, 17 TCP v6 in 0.3 ms, 34 UDP v4 in 0.1 ms |
| winget | v1.29.380 |
| Print Screen setting | `PrintScreenKeyForSnippingEnabled` isn't set, so Windows' default (Print Screen opens Snipping Tool) applies |

## 1. What Chippi already has

| Piece | Where | Phase 7 use |
|---|---|---|
| Connected monitors with stable ids (`\\?\DISPLAY#…`), their names and handles | `Chippi.UI/Displays/DisplayCatalog.cs` | The selector's monitors, Displays' list, the id brightness settings are saved under |
| Per-monitor DPI v2 in the app manifest | `Chippi.App/app.manifest` | Overlays and capture work in physical pixels with no scaling by Windows |
| Top-level windows in z-order, Alt+Tab rules, cloaked windows | `Chippi.UI/Actions/WindowsWindowList.cs`, `QuickActions/Windows/AltTabApps.cs` | The selector's window snapping, window capture's target list |
| Topmost, click-through, no-activate overlay | `WindowLayouts/Views/ZoneOverlay.cs` | Pattern for the recording frame |
| Overlay that takes the keyboard after a shortcut | `AppSwitcher/Views/SwitcherOverlay.cs` and the Phase 6 foreground rule (AGENTS.md) | The selector overlay |
| Clipboard images: `ClipImage(Width, Height, Bgra)`, written through the hub, 16 MB encoded and 16,588,800 pixels at most (two 4K screens) | `Chippi.Core/Clipboard/IClipboardHub.cs` | Copy for captures, OCR text and colors. No feature opens the clipboard itself |
| Actions, shortcuts, commands | `Chippi.Core/Actions`, `Chippi.Core/Shortcuts`, `IModuleUi.AddCommands` (Phase 6) | Every Phase 7 verb (section 8) |
| End a process, empty the Recycle Bin | `QuickActions/Windows/AltTabApps.cs`, `WindowsQuickSystem` | Port manager's End, the Cleaner's Recycle Bin row |
| WASAPI capture | `NAudio.Wasapi` 2.2.1 (already a package) | Recording's system sound and microphone |
| A sealed SQLite store with a per-user key | `Chippi.Clipboard/Storage/SealedDatabase.cs` | Capture history's index, if Travis wants it sealed like clipboard history (section 2) |
| Network readings with `GetIfTable2` | `SystemMonitor/Sensors/Platform/NetworkSource.cs` | Pattern for the IP Helper calls in Port manager |
| `Capability.ScreenCapture` and `Capability.Admin`, already on the four capture features in the catalog | `Chippi.Core/Modules/Capability.cs`, `FeatureCatalog.cs` | `Admin` gets real with the helper (section 6) |
| Elevation check (`TokenElevation`) | `TextSnippets/Native.cs`, `SmoothScrolling/WindowsScrollEnvironment.cs` | Shared into Core once Phase 7 needs it a third time |
| One `runas` launch (VB-CABLE's installer) | `Sound/Processing/VbCablePackage.cs` | The only elevation today; the helper replaces nothing there |

## 2. One capture layer

A new project, `Chippi.Capture` (WPF-free, in `Chippi.slnx`), owns reading the screen. Four features use it: Screen capture, Screen recording, Text and QR, and Color picker. The selector overlay (the frozen screen you drag on) is a WPF piece in `Chippi.UI` that sits on top of it, so each feature gets the same selector, the same window snapping and the same keys.

**Freeze.** On the shortcut, Chippi reads every monitor once into memory, then shows one overlay window per monitor with that image. Everything after that (the drag, window snapping, the magnifier, the markup, the color under the pointer) reads the frozen image, never the live screen. That's how Lightshot and Snipping Tool behave (Karl's spec), and it means a menu or tooltip open at the press is captured as it was.

- **How to read the screen:** three ways, all from a standard user.
  - GDI `BitBlt` from the screen DC with `CAPTUREBLT`: one call, simplest, CPU copy.
  - DXGI Desktop Duplication: one per GPU output. Travis's monitors may sit on different adapters (RTX 4080 and the Radeon iGPU).
  - Windows.Graphics.Capture (WGC) for a monitor (`CreateForMonitor`): the same API recording uses, frames on the GPU, HDR as float.
  
  Which is fastest from key to frozen screen isn't known, and it's the number Travis feels. **Wave 1 spike:** time all three, cold and warm, for each monitor, in Windows Sandbox with its virtual GPU (no read of Travis's screen), and pick one. **Inference:** WGC for a still costs a session start and one frame (tens of milliseconds) but keeps one path for stills and video; `BitBlt` is the fallback if it's slower.
- **What it returns:** one `CapturedScreen` per monitor: BGRA pixels, the monitor's rectangle in virtual-screen pixels, its DPI and its `DisplayCatalog` id. A capture's result is a `Capture`: pixels, the source rectangle, the source window (title, app path) when there is one, and the time. Pixels stay in Chippi's memory; nothing is written to disk until a feature saves.
- **Size:** Travis's two monitors are 3440 × 1440 and 1920 × 1080, about 28 MB of BGRA together. Held only while the overlay is up, then released.
- **What can't be read:** windows that ask not to be captured with `SetWindowDisplayAffinity` (some password managers, DRM video) come out empty (`WDA_MONITOR`) or missing (`WDA_EXCLUDEFROMCAPTURE`) in Windows' capture APIs [SetWindowDisplayAffinity]. The secure desktop (UAC prompts, Ctrl+Alt+Del) can't be read at all. The selector shows what it got; it doesn't try to work around either.
- **HDR:** WGC and Desktop Duplication can read an HDR screen as 16-bit float, and then Chippi tone-maps it to SDR itself; read as 8-bit, bright areas can wash out [HDR]. What `BitBlt` returns on an HDR screen isn't documented.

**The selector overlay.** One topmost window per monitor, each at that monitor's DPI, so no window ever spans two scales (the problem Snap's zones solved the same way). Coordinates are physical virtual-screen pixels throughout; a region may cross monitors, and its pixels are cut from both frozen images.

- **Window snapping:** at the freeze, read the top-level windows in z-order with `WindowsWindowList` (visible, not cloaked, frame bounds from `DWMWA_EXTENDED_FRAME_BOUNDS`). Hovering highlights the frontmost window under the pointer; a click takes it. This list is a snapshot, so it matches the frozen image. **Not in the first build:** snapping to controls inside a window (Snipaste does it through UI Automation, which is slow across processes and would delay the freeze).
- **Keys:** the overlay needs the keyboard (Esc, Enter, Ctrl+C, Ctrl+S, arrow keys to nudge). After a `RegisterHotKey` shortcut Windows lets it take the foreground (the Phase 6 rule). After a press the input hub swallowed (a Caps Lock combo, a mouse button, Print Screen through the hub), it uses the marked 0x07 tap and one retry (`WindowsSwitcherPc.TakeForeground`).
- **Magnifier, repeat last region, delay, freeform:** all read the frozen image or the saved last rectangle, so they cost nothing extra. A delay waits before the freeze, with a countdown on the bar.
- **In-place markup (Lightshot's flow):** the toolbar appears beside the selection on release, on the monitor where the pointer is. Shapes are WPF vectors over the frozen image while editing; Copy and Save flatten them at the image's own pixel size (`RenderTargetBitmap` at 96 DPI over the cropped pixels, so a 150% monitor's capture stays 1:1). Blur and pixelate work on the source pixels on the CPU. **Timing to prove in wave 1:** strokes keep up with the pointer on the 3440-wide display at 165 Hz.
- **What leaves the overlay:** Copy goes through the clipboard hub (`ClipContent.FromImage`). A region bigger than the hub's pixel limit (only possible past two 4K screens) is refused with "Copy failed"; the limit stays where it is. Save writes through WIC (section 3).

**Window capture** without the overlay (the "Capture window" verb on the window in front) uses WGC's `CreateForWindow`, which also captures a window covered by others. `CreateForWindow` and `CreateForMonitor` are desktop interop calls from Windows 10 1903 and need no picker [IGraphicsCaptureItemInterop]. **Not verified:** whether a standard-user Chippi can capture an elevated window this way; OBS's guides say a window with higher rights than the capturing app can't be captured. **Inference:** a monitor capture includes it, since DWM composes the whole screen.

**Scrolling capture** (the "whole page" question for Travis, Step 2). Windows has no API for it. ShareX does it by scrolling the window in steps, capturing each step and stitching the images where they overlap (Karl's spec). In Chippi that means: WGC capture of the target window, wheel input sent to it through `ModuleContext.Actions` (marked as Chippi's, so the hub passes it), and an overlap match on the CPU. **Cost, inference:** L, with known failures that no tool fixes: sticky headers and footers repeat, lazy-loaded pages change height mid-capture, and admin windows ignore the wheel. It also sends input into another app, so its tests are Sandbox-only. Recommend leaving it out of wave 1 and deciding it with Karl's findings.

**Print Screen.** Two pieces of Phase 6 stand in the way:
- **The shortcut service refuses a bare Print Screen:** `KeyShortcut.IsBindable` needs Ctrl, Alt or Win and doesn't list VK 0x2C. Capture region needs a narrow exception: Print Screen alone, and Print Screen with modifiers, are bindable. Nothing else bare.
- **Windows holds it by default:** Windows 11's "Use the Print screen key to open screen capture" setting is on by default since build 22621.1546, keeping a user's earlier choice [Insider 22621.1546]. Third-party guides give its value as `PrintScreenKeyForSnippingEnabled` under `HKCU\Control Panel\Keyboard` (Microsoft doesn't document it); on Travis's PC it isn't set, so the default (on) applies (**inference**). Greenshot's FAQ tells users to turn the setting off and restart Greenshot before its Print Screen works [Greenshot FAQ], and ShareX users report the key flipping between the two [ShareX#6581]. `RegisterHotKey`'s own page says a registration usually fails for keys already taken, but that some of the OS's default hotkeys, "such as PrintScreen, which launches the Snipping tool", may be overridden by another registration when one of the app's windows is in the foreground [RegisterHotKey]. So registering may succeed and still work only while a Chippi window is in front, which is useless for a shortcut that starts a capture from anywhere. The wave 1 spike, in the Sandbox, tests the setting on and off, each with a Chippi window in the foreground and with another app in front: whether `RegisterHotKey` succeeds, whether `WM_HOTKEY` arrives, whether Snipping Tool opens too, and whether the input hub's hook sees the key. It also checks whether turning the setting off takes effect without signing out; that isn't documented.
- **Recommend (the plan's Step 2 question):** when Travis gives Capture region Print Screen, Chippi turns that setting off, records what it found, and puts it back when the shortcut changes or the feature is uninstalled, the way Focus follows mouse handles its Windows setting. Win+Shift+S stays Windows'.

**History.** Captures Travis keeps (saved, or copied if history is on) go to the feature's data folder as PNG files with a small index. **Decision for Step 2 (where captures go):** screenshots can hold whatever was on screen, passwords included. Clipboard history seals its store for that reason; capture history can use the same `SealedDatabase` for its index and keep the images in `%LOCALAPPDATA%` (readable only by Travis's account), or seal the images too at a cost of a decode on every thumbnail. **Inference:** sealing the index and leaving files plain matches what Travis sees with a saved file anyway.

## 3. Encoders

Windows has every encoder Phase 7 needs for the formats people use most, on x64 and ARM64. No ffmpeg in the first build.

**Images: WIC.** PNG, JPEG, BMP, GIF and TIFF encoders are built into Windows. HEIF (and AVIF through it) needs Store extensions, which Travis has, as does a JPEG XL encoder [WIC codecs, HEIF codec]; Chippi offers those formats only where the encoder is present. No WebP encoder is installed here (measured), and Microsoft doesn't document one. Media tools can read WebP (decoder) but would save it only with a library (libwebp, BSD); **recommend** not offering WebP or AVIF output in the first build. Resizing uses `IWICBitmapScaler`; its interpolation mode (Fant or high-quality cubic) is picked on fixture images in the brief.

**Animated GIF: WIC, with Chippi's own palette.** WIC's GIF encoder writes frames, each frame's delay (`/grctlext/Delay`) and looping (`/appext/Application` and `/appext/Data`, the NETSCAPE2.0 block) [WIC metadata]. It needs an indexed image per frame; WIC builds an optimized palette of up to 256 colors from a frame (`IWICPalette::InitializeFromBitmap`) and dithers to it [InitializeFromBitmap]. **Inference:** WIC's palette is a quality risk for screen recordings with gradients; ScreenToGif uses its own quantizer for that reason. Plan: start with WIC's palette plus frame differencing (unchanged pixels transparent, which also makes files much smaller), and compare against a GIF from ScreenToGif's description in Karl's spec. If it falls short, write an octree quantizer (an M task, no library).

**Video: Media Foundation through WinRT.** WGC frames are Direct3D textures; a `MediaStreamSource` hands them to `MediaTranscoder`, which encodes H.264 into MP4 on the GPU. Microsoft's SimpleRecorder sample (MIT) does exactly this with `MediaStreamSample.CreateFromDirect3D11Surface` and `PrepareMediaStreamSourceTranscodeAsync` [SimpleRecorder]. Windows ships H.264 and AAC encoders and prefers a hardware one when present [MF formats, H.264 encoder]; on this PC the NVIDIA and AMD hardware encoders and Microsoft's DX12 encoder are all there, with the software `H264 Encoder MFT` as the fallback. Windows N editions have none of this until the Media Feature Pack is installed [Media Feature Pack]; Recording and Media tools then say so (Lumen's copy) and stay off. Audio comes from WASAPI (system sound by loopback, the microphone by capture, both through NAudio already in Chippi), mixed to 48 kHz and encoded as AAC.
- **Sync:** WGC frames carry a `SystemRelativeTime` and WASAPI capture packets a QPC position, so audio and video can be lined up on one clock. **Inference:** both are the performance counter; NAudio's capture event doesn't pass the QPC position, so recording reads the capture client itself.
- **Region recording:** WGC captures a whole monitor; the frame is cropped on the GPU (`CopySubresourceRegion`) before encoding. **Inference:** a region spanning two monitors needs two sessions composed into one texture; recommend one monitor per recording in the first build.
- **Recording frame and controls:** the frame around the region and the bar's stop button must not appear in the video. Chippi's own frame window sets `WDA_EXCLUDEFROMCAPTURE`, which hides it from WGC and every other capture (OBS, Teams) for as long as it's up. The yellow WGC border goes away with `IsBorderRequired = false` (Windows 11, build 20348 on) after `GraphicsCaptureAccess.RequestAccessAsync(Borderless)`. For packaged apps that request prompts; Microsoft's sample author says a full-trust unpackaged app sees no prompt but should still call it [IsBorderRequired, WGC sample #97] (**inference** for Chippi until the spike).
- **GIF recording:** frames go to a temporary MP4 while recording (memory stays flat), then become a GIF on Stop. A raw 3440 × 1440 frame is 20 MB, so holding frames in memory isn't an option.

**Media tools: the same transcoder.** Convert, resize and compress video with `MediaTranscoder` and an encoding profile: H.264 or HEVC in MP4, with a lower bitrate or size for Compress. Inputs Windows decodes: MP4 and MOV with H.264 built in; HEVC, VP9 and AV1 through Store extensions [HEIF codec, MF formats], all three present here; MKV is commonly reported as built in since Windows 10 (**inference**). Chippi lists what the PC can decode rather than a fixed list (`CodecQuery`, as the probe did). Outputs: MP4 only, plus GIF from a clip through the GIF path.

**ffmpeg** would add WebM output, odd inputs (AVI with old codecs, FLV) and HandBrake-like control. BtbN publishes win64 and winarm64 builds: LGPL shared is 78 MiB (x64) and 56 MiB (ARM64) zipped, GPL static 193 and 136 MiB; gyan.dev's builds are GPLv3 and x64 only [BtbN, gyan.dev]. Either license is compatible with Chippi's GPL-3.0-or-later, but it would roughly double the installer. **Recommend:** leave it out; offer it to Travis in Step 2 only if Herb's spec finds a Media tools job Windows can't do.

## 4. Text and QR

**OCR: `Windows.Media.Ocr`**, built in, standard user, the same engine PowerToys Text Extractor uses [Text Extractor]. Snipping Tool's Text actions use an engine Microsoft doesn't name. The newer Windows AI `TextRecognizer` runs only on Copilot+ PCs with an NPU and needs a packaged app's capability [Text recognition], so it's out for Chippi. **Inference:** Chippi matches PowerToys' accuracy, not necessarily Snipping Tool's; Travis judges that in the live check. It reads only languages whose OCR capability is installed (`Language.OCR~~~<tag>`, added with admin [Text Extractor]); Travis has en-US. Images over `MaxImageDimension` on a side are refused (10,000 here, read at run time since it isn't documented), which no screen region reaches. **Inference:** small UI text reads better scaled up 2× before recognition; the brief tests both on fixture images.

**QR: ZXing.Net** 0.16.11 (2025-10-25, Apache-2.0, compatible with GPL-3.0), a managed library that works on x64 and ARM64 alike and reads QR and other barcodes from a raw BGRA buffer (`RGBLuminanceSource` with `BGRA32`) [ZXing.Net]. Chippi decodes the selected region, or the whole frozen screen so Travis doesn't have to select the code. ZXingCpp (Apache-2.0) wraps the C++ port with native x64 and ARM64 libraries; **inference:** faster, but a native library per architecture for a job that takes one image, so not recommended.

**What happens with the result** (Karl's spec decides): text and decoded links go to the clipboard through the hub, or open, never typed. Links open only after Travis picks Open; a QR code can carry any URL.

## 5. Color picker

No new Windows API: it reads the frozen image under the pointer through the same overlay, with the magnifier. **Inference:** a frozen screen gives a stable color for video and animations, where PowerToys' live picker reads a moving target; Karl's spec decides frozen or live. A live picker would read a small square around the pointer on each move with WGC frames already on the GPU, not `GetPixel`, which is slow.

Formats (HEX, RGB, HSL, HSV, CMYK, OKLCH and the rest) are pure math in Core, unit tested. **Not verified:** colors on an HDR display. With HDR on, an 8-bit capture is Windows' tone-mapped version of the screen; the value may differ from the app's own color. Whether Travis's OMEN runs in HDR isn't known.

## 6. The admin helper

The first real use of `Capability.Admin`. Chippi stays a standard-user process; the helper is a separate small program that runs elevated only while it has work, does a closed list of jobs, and exits.

**Does Phase 7 need it at all?** Less than the plan assumed:
- **App updates:** winget runs each installer, and when winget isn't elevated, Windows asks for admin as a machine-wide installer starts [winget]. **Inference:** updating five machine-wide apps means five prompts. With the helper, one prompt covers the batch.
- **Uninstaller:** a machine-wide app's uninstaller also asks for admin itself (MSI and most EXE uninstallers). Without the helper, one prompt per app. The helper is needed for **leftovers** under Program Files, ProgramData and `HKLM`, which a standard user can't delete.
- **Cleaner:** a standard user can clear their own temp, caches and Recycle Bin. `C:\Windows\Temp`, Windows Update's download cache and Delivery Optimization's cache need admin.
- **Port manager:** a standard user can see every port and its owner's PID (measured above). Ending a process that runs elevated, as another user or as a service needs admin. Closing a single TCP connection (`SetTcpEntry` with `DELETE_TCB`) needs admin and works for IPv4 only [SetTcpEntry].
- **Displays, every capture feature:** never.

So the Step 2 question is really "one prompt per batch, plus leftovers, admin temp and services" versus "a prompt per app from the installers, and skip the rest". **Recommend:** build the helper in wave 3, as the plan has it.

**How it's launched.** `Chippi.Elevated.exe`, installed beside `Chippi.exe`, started with `ShellExecuteEx` and the `runas` verb only when Travis presses a button that needs it. Windows shows one UAC prompt. It never runs at startup, as a service or as a scheduled task, so there's no standing elevation. It exits when its pipe closes, when the job list is done, or after 60 s idle.

**How it talks.** Chippi creates a named pipe with a random name, `FILE_FLAG_FIRST_PIPE_INSTANCE` (so nothing else can create the same name first and pose as Chippi), `PIPE_REJECT_REMOTE_CLIENTS`, and a DACL naming only Travis's logon session; the default DACL lets Everyone read [pipe security]. It passes the name on the helper's command line. The helper connects and checks the other end with `GetNamedPipeServerProcessId`: the server must be `Chippi.exe` from the helper's own folder (**inference**, the call isn't in the docs I read).

**What it may do: verbs, not commands.** The helper never runs a command line it's given. Each request is one of a fixed set of verbs with typed arguments, and the helper looks up the details itself:
- `UpdatePackage(id)`: the id must match winget's id pattern and goes as one argument to `winget upgrade --id <id> --exact --silent`, which acts only on a package that's installed. The helper uses winget's command line, not its COM API: COM activation from an elevated unpackaged process has an open crash report and needs a special factory [winget#4377, Microsoft.WinGet.Client]. The full path to `winget.exe` is resolved once, under `WindowsApps`.
- `Uninstall(registryKey)`: the helper reads the uninstall command from that `HKLM` key itself. Only admins can write `HKLM`, so the command is as trusted as the app that's installed. An `HKCU` entry is never run elevated: any program running as Travis can write one, so Chippi runs it as a standard user.
- `DeleteLeftovers(app, items)`: removes only the given app's leftovers, under the ownership rule in Herb's spec (`behavior-spec-b.md`, Leftover ownership). The helper doesn't trust Chippi's labels; it works the rule out again itself, and refuses an item that fails any check below:
  - **The app's identity is the helper's own.** `app` is the `HKLM` Uninstall key the helper read for `Uninstall(registryKey)` in the same session, before the uninstaller ran (the uninstaller usually deletes the key). The helper keeps that key's name, `InstallLocation`, product code and install paths. Without that record, or the key still in place, nothing is deleted elevated.
  - **Ownership is checked again.** Each item says which class Chippi showed it in. A "Safe" item must be verified: inside the recorded `InstallLocation`, or a key or folder named by the app's own uninstall data. A "Check first" item is accepted only if Travis ticked it after the review, which the request records. Any item that the helper's own check doesn't match to the class Chippi sent is refused.
  - **Never another app's.** The helper reads every installed app's Uninstall entry (both `HKLM` views and Travis's `HKCU`). It refuses an item that is, contains, or sits inside another app's `InstallLocation`, install folder or uninstall key. For example, while Firefox is removed, `Program Files\Mozilla Thunderbird` is refused, and so is a shared `Mozilla` folder that Thunderbird's paths sit under. Shared items never reach the helper ("Shared" in the spec), and it refuses them anyway.
  - **Fixed deny list:** Windows, System32, the Program Files, Common Files and ProgramData roots, user profile roots, and Chippi's own folders.
  - **Paths are canonical before any check.** It refuses relative paths, `.` and `..` segments, `\\?\` and `\\.\` prefixes, UNC and network paths, alternate data streams (a `:` after the drive) and short 8.3 names. Each check compares whole path segments, case-insensitively, never a string prefix: `C:\Program Files\App` doesn't contain `C:\Program Files\AppData`.
  - **No reparse points on the way.** A reparse point redirects file operations to another path [reparse points], so a junction in ProgramData (where standard users can create folders) could point at System32. The helper opens the item and each folder above it, up to the root, with `FILE_FLAG_OPEN_REPARSE_POINT`. It refuses any of them that's a reparse point, or whose `GetFinalPathNameByHandle` path isn't the requested one. It holds the folders above open without `FILE_SHARE_DELETE` until the item is gone, so they can't be renamed or swapped between the check and the delete. Inside a deleted folder, a reparse point is removed as a link and never followed.
  - **Only what Travis can't delete himself.** An item whose owner is Travis or another standard user is refused and goes back to Chippi's standard-user delete (section 7). This also refuses a junction or folder that a program running as Travis has planted.
  - **Registry keys:** only under `HKLM\Software` (each view named explicitly with `KEY_WOW64_64KEY` or `KEY_WOW64_32KEY`), and only keys named by the app's uninstall data. A key opened with `REG_OPTION_OPEN_LINK` that turns out to be a registry link is refused.

  Files go to the Recycle Bin (`SHFileOperation` with `FOF_ALLOWUNDO` and `FOF_WANTNUKEWARNING`, which needs no COM); registry keys are exported with `%SystemRoot%\System32\reg.exe export` to a `.reg` file in Chippi's data folder first.
- `CleanCategory(name)`: a fixed category maps to fixed folders in the helper's own code.
- `EndProcess(pid, startTime)` and `StopService(name)`: the start time guards against a reused PID; never PID 0 or 4, `csrss`, `wininit`, `lsass`, `services` or `smss`.

Every verb and its result goes to Chippi's log. A verb Chippi doesn't know about can't be sent, since the protocol has no generic form.

**The risk to say plainly.** Chippi installs per user in `%LOCALAPPDATA%\ChippiApp`, which Travis's own account can write, and it's unsigned. Any program running as Travis could replace `Chippi.Elevated.exe` or drop a DLL beside it, and the next UAC prompt would elevate that instead (**inference**; Microsoft documents the DLL case as a way to elevation [DLL security]). Microsoft doesn't treat UAC as a security boundary, so a program already running as Travis can get admin in other ways too [MSRC criteria]; the helper doesn't make that worse, but it shouldn't be the easy route either. **Mitigations:**
- The UAC prompt names the program, and it appears only right after Travis presses a button that needs admin.
- The helper loads DLLs from System32 only (`SetDefaultDllDirectories` with `LOAD_LIBRARY_SEARCH_SYSTEM32`) [DLL security].
- It ships as one NativeAOT executable, with no managed DLLs beside it to plant over. NativeAOT supports win-arm64 from .NET 8 and has no built-in COM [NativeAOT], which is why the verbs above avoid COM.

**The full fix**, signing and installing under Program Files, is out of scope as in Phase 6 (code signing is out per `build/README.md`). **The alternative** PowerToys uses, restarting the whole app as admin [PowerToys admin], would run every feature elevated; not recommended.

**Tests:** no UAC prompt in tests or dev runs (the plan's rule). The verbs' checks are pure and run in CI against a fake pipe. `DeleteLeftovers` gets fixture trees and fake Uninstall entries, each of which must be refused, with nothing deleted:
- removing Firefox with `Mozilla Thunderbird` installed: Thunderbird's folder and the shared `Mozilla` folder
- a "Safe" item outside the recorded `InstallLocation`, and a call with no Uninstall record from the same session
- `C:\Program Files\App\..\Other`, a short 8.3 name, a `\\?\` prefix, a UNC path, an alternate data stream, and a sibling with the same prefix (`App` against `AppData`)
- a ProgramData junction pointing at System32, a junction or symbolic link as the item itself, and a junction above it
- a folder renamed or swapped for a junction after the check, which the held handles must block
- an item owned by a standard user, which goes back to the standard-user path
- a registry link, and a key outside `HKLM\Software`

A junction inside a deleted folder must be removed as a link, with its target untouched, both when the folder is recycled and when it's deleted for good. Junctions need no admin, so the path checks run in CI on temp trees; symbolic links, ownership and the elevated runs are Sandbox cases. The helper's own process, elevated, runs only in Windows Sandbox, where a prompt can't touch Travis's apps (**inference:** the Sandbox's account can approve its own prompt; the first helper run checks it).

## 7. Apps: updates, uninstall, cleaner

**winget.** Travis's PC has winget 1.29.380. Two ways to drive it:
- **The COM API** (`Microsoft.Management.Deployment`, NuGet `Microsoft.WindowsPackageManager.ComInterop` 1.29.380, MIT) gives typed results: installed packages, available versions, progress, and install and upgrade calls. Microsoft's own `Microsoft.WinGet.Client` PowerShell module uses it, activating the out-of-process server with a plain `CreateInstance` from a standard-user process [Microsoft.WinGet.Client]. The server lives in App Installer, so it costs nothing on ARM64.
- **The command line** has no JSON output for `list` or `upgrade` [winget list]; its table is truncated and translated, so parsing it is fragile. UniGetUI uses the COM API first and parses the table only as a fallback [UniGetUI].

**Recommend:** the COM API in Chippi (standard user) for listing, checking and upgrading, with the installer's own UAC prompt when a machine-wide one needs it; the command line only inside the helper, for a batch Travis approves once (section 6). Checking for updates reads only, so it can run against Travis's real PC; installing runs only in the Sandbox or on throwaway installs in his live check.

**Uninstaller.** Installed apps come from three places, read as a standard user:
- The `Uninstall` keys under `HKLM` (64-bit and `WOW6432Node`, the 32-bit view) and `HKCU` [Uninstall key, registry redirection], skipping `SystemComponent` entries [ARPSYSTEMCOMPONENT] and updates with a `ParentKeyName`. `QuietUninstallString` and `WindowsInstaller` are conventions installers follow, not documented by Microsoft (**inference**).
- Store and MSIX apps through `PackageManager.FindPackagesForUser`; `RemovePackageAsync` removes one for the current user at standard (medium) integrity, no admin [PackageManager].
- winget's list, which maps many registry entries to package ids.

The icon is the entry's `DisplayIcon`, or the exe's, through `AppIcon.View`. **Leftovers** after the uninstaller finishes: folders and keys named for the app's publisher or name in Program Files, ProgramData, `%APPDATA%`, `%LOCALAPPDATA%`, `HKCU\Software` and `HKLM\Software`, plus its Start menu shortcuts. Each found item is shown before anything is removed. A name match is never proof: which items are "Safe", "Check first" or "Shared" follows the ownership rule in Herb's spec (`behavior-spec-b.md`, Leftover ownership), and the helper checks it again for anything it deletes (section 6).

**Deleting.** Files and folders go to the Recycle Bin through `IFileOperation` with recycle-on-delete; registry keys have no Recycle Bin, so they're exported to a `.reg` file first and Travis can merge it back. With `FOF_WANTNUKEWARNING`, Windows warns before deleting a file for good when it can't be recycled (too big, or a drive without a Recycle Bin) [IFileOperation].

**Cleaner.** Each category is a list of folders and a rule (older than a day, not in use). It measures first and shows the total before deleting; files in use are skipped and counted. **Decision for Step 2:** the plan recommends the Recycle Bin wherever possible, but recycling a cache frees no space until the bin is emptied. **Recommend:** caches and temp files are deleted for good after the preview; anything that isn't a cache or temp file isn't in the Cleaner. Finding large folders (WizTree's job) as a standard user walks the disk with `FindFirstFileEx` (large fetch, basic info); **inference:** a minute or more on a full drive. WizTree reads the NTFS master file table directly, which needs admin, and could be a helper verb later.

## 8. Displays and brightness

**External monitors: DDC/CI through `dxva2`.** `GetPhysicalMonitorsFromHMONITOR`, then `GetVCPFeatureAndVCPFeatureReply` and `SetVCPFeature` with VCP 0x10 (brightness) and 0x12 (contrast). Standard user. Both of Travis's monitors answer (measured above).
- **Every call is slow:** 55 to 65 ms per read here, and the capabilities string 1.3 to 6.6 s. Microsoft documents about 40 ms per read, 50 ms per write, and "several seconds" for capabilities [monitor config]. All DDC calls run on one worker per monitor, never the UI thread. A slider sends only its latest value: while a write is in flight, newer values replace the queued one. Twinkle Tray throttles to one write per 500 ms by default [Twinkle Tray].
- **Skip writes that change nothing:** many monitors store brightness in their own memory on every write. KDE's power manager skips a write equal to the current value for that reason [KDE PowerDevil]; Chippi does the same (**inference**: no vendor documents the wear).
- **Capabilities** are read once in the background and cached by the monitor's `DisplayCatalog` id. The page opens filled from the cache and the last values read.
- **Writes are limited to VCP 0x10 and 0x12** (the plan's rule). No input switching, power or factory reset codes.
- **When DDC/CI fails** (turned off in the monitor's menu, docks and adapters that don't pass it, VGA and DVI [Monitorian, Twinkle Tray]), the row says "Brightness control failed" and offers nothing. **Inference:** a software dimming overlay would cover those, but it's a different feature; not in the plan.

**Laptop panels: WMI** `root\wmi`, `WmiMonitorBrightness` to read and `WmiMonitorBrightnessMethods.WmiSetBrightness` to write [WmiSetBrightness]. Microsoft's page names no privilege; Monitorian and Twinkle Tray use it without elevating (**inference** that a standard user can). Travis's desktop has no instances, so this path can only be tested with a fake here.

**Brightness up and down shortcuts** step every monitor (or the one under the pointer; Herb's spec decides) through the same worker. The keys go through the shortcut service; a laptop's own brightness keys are handled by its firmware and Windows, not Chippi.

**Monitors coming and going:** `DisplayCatalog` is read again on `WM_DISPLAYCHANGE`; physical monitor handles are released and reopened. Win+P turning the Samsung off (from the arrangements check) is the case to test.

**Tests:** the DDC layer sits behind an interface with a fake for CI. A real check on Travis's PC reads only. Any dev run that writes says so first, reads the current value, and writes it back after (the plan's rule). The Sandbox has a virtual display with no DDC/CI, so it can't test this.

## 9. Port manager

- **Reading:** `GetExtendedTcpTable` and `GetExtendedUdpTable` with owner PIDs, IPv4 and IPv6 [GetExtendedTcpTable], as a standard user: about 650 rows in under 2 ms here, every process's included. Polled every 2 s while the page shows, never in the background. Windows has no change notification for these tables.
- **Names:** process names from the process list (`Process.GetProcesses`, which reads names without opening each process, so it works for SYSTEM and elevated ones too; **inference** from how it's built). The exe path and icon with `QueryFullProcessImageName`, which needs only limited query access, granted even for protected processes; `OpenProcess` fails for PID 0, System and CSRSS [OpenProcess, process rights]. Services hosted in `svchost`: `GetOwnerModuleFromTcpEntry` returns empty names to a standard user for System32 programs [GetOwnerModuleFromTcpEntry], so Chippi matches the PID to `EnumServicesStatusEx` instead (**inference** that a standard user may enumerate services, as `sc query` does).
- **Freeing a port:** ending its process (`TerminateProcess`, as Quit apps does). Never PID 0 (TIME_WAIT rows) or 4 (System), never Windows' core processes, and for a service, Stop service rather than ending its `svchost`, which may host others. Ending Travis's own processes needs no admin; the rest go through the helper.
- **Tests:** a test opens its own listener on `127.0.0.1` (not all interfaces, which raises a Windows Firewall prompt for a new program) in a child process it starts, finds it, and ends it. Never another process.

## 10. Verbs, shortcuts and doors

Every verb is a Phase 6 shared action, with no new mechanism:
- **Commands:** each feature adds its verbs through `IModuleUi.AddCommands`, so Search, the Radial menu's Chippi slice and Actions tiles get them with no further work. Proposed keys: `screen-capture/region`, `/window`, `/screen`, `/all-screens`, `/repeat`, `/freeform`; `screen-recording/region`, `/window`, `/stop`; `screen-text/text`, `/qr`; `color-picker/pick`; `displays/brightness-up`, `/brightness-down`; `app-updates/check`; `cleaner/clean`. Karl's and Herb's specs set the final list; Lumen's map sets each one's door.
- **Shortcut scope is still Travis's to decide** in Step 2, as one rule for both specs (task 471). Settings › Shortcuts stays the only place a key is set (Travis, 2026-11-05), and no slot has a default except Print Screen if he picks it (section 2). The two options:
  - **Every external verb gets a global slot** (Karl's recommendation). Each feature adds one `ModuleContext.Shortcuts` entry per verb when it starts, set or not, with `ShortcutEdits`. A verb that needs a target (a capture file, a recording, a result) opens a picker when it's fired from anywhere, never takes a stale selection or the latest private result, and asks before anything it can't undo (`behavior-spec-a.md`). The service needs nothing new for this beyond Print Screen.
  - **Global slots only for the starters and Stop recording** (Herb's recommendation). Result and editor commands are contextual.
- **Either way, editor keys are contextual.** Ctrl+C, Ctrl+S, Esc, Enter, Ctrl+Z and the tool letters are handled by the overlay's own key handling while it has the keyboard, and never registered with `RegisterHotKey`. Fixed contextual keys need no service change. If Travis wants them customizable in Settings › Shortcuts (the spec's "Capture editor" slots), that needs a separate contextual-binding design. Today the service holds only global `RegisterHotKey` bindings and checks clashes against them, so it would need a scope on each entry, clash checks within a scope, and a lookup the overlay reads. That design comes before the capture brief, and only if he picks it.
- **Service changes:** Print Screen becomes bindable alone and with modifiers on either path (section 2). A contextual scope is added only if Travis picks customizable editor keys. Both are shared Core code, called out in the first capture PR.
- **Foreground:** capture verbs fired from a shortcut get the foreground for the overlay; from the Radial menu or a Caps Lock combo they use the marked tap (Phase 6 rule). From the island, the island closes first, then the freeze, so the panel isn't in the capture.
- **Recording state on the bar** is a bar item like Keep awake's timer, published through `ModuleContext.Island`.

## 11. Standard user or admin

| Feature | Standard user | Needs admin |
|---|---|---|
| Screen capture, Text and QR, Color picker | Everything | Never. Admin windows show in captures of the screen |
| Screen recording | Everything, system sound and microphone included | Never |
| Media tools | Everything | Never |
| Displays | DDC/CI and (**inference**) WMI brightness | Never |
| Port manager | Seeing every port and owner, ending own processes | Ending elevated, other users' or service processes, stopping services, closing one connection |
| App updates | Listing updates; per-user installs; machine-wide installs with the installer's own UAC prompt | One prompt for a batch (the helper) |
| Uninstaller | Listing; per-user apps and Store apps; machine-wide uninstallers with their own prompt | Leftovers under Program Files, ProgramData and `HKLM` |
| Cleaner | Own temp, browser caches, thumbnails, own Recycle Bin | Windows temp, Windows Update and Delivery Optimization caches, a fast disk scan |

## 12. ARM64

| Piece | ARM64 |
|---|---|
| WGC, WIC, Media Foundation, OCR, DDC/CI, IP Helper, WMI | Part of Windows on ARM64. Hardware video encoders depend on the chip's driver (Snapdragon has them, **inference** from Qualcomm's public specs); the software H.264 encoder is the fallback |
| ZXing.Net | Managed, no native code: no cost |
| WinGet COM interop | A managed projection; the server is App Installer's own, on both: no cost |
| The admin helper | One more NativeAOT build per architecture, a few MB each (**inference**: a .NET 8 hello world is about 1.8 MB) |
| ffmpeg (not recommended) | A separate native build per architecture, 56 to 136 MiB zipped |

Nothing in Phase 7 can be tested on ARM64 hardware here; CI builds win-arm64, as now.

## 13. New libraries

| Library | For | License | Recommend |
|---|---|---|---|
| ZXing.Net 0.16.11 | QR and barcodes | Apache-2.0 | Add (Step 2 approval) |
| Microsoft.WindowsPackageManager.ComInterop 1.29.380 | App updates | MIT | Add (Step 2 approval) |
| libwebp | WebP output | BSD-3-Clause | Not now |
| ffmpeg | Video formats Windows can't write | LGPL-2.1+ or GPL-3.0 build | Not now |

The SimpleRecorder sample (MIT) is a reference for the recording pipeline, not a dependency.

Everything else is Windows itself.

## 14. Testing without Travis's apps, files or clipboard

| Level | Runs where | Covers |
|---|---|---|
| Pure code | CI | Region math across monitors and scales, window snapping on a fake window list, markup flattening at 100/150/200%, color formats, GIF palette and frame differencing, DDC write coalescing, the helper's verb checks, leftover matching on fake registry and folder trees, cleaner rules on a temp tree, port table parsing |
| Fixture images and clips | CI | OCR on rendered text (Windows' OCR runs on the CI runner if it has en-US; **not verified**, else the test is skipped there), QR on generated codes, encoding a generated frame stream to MP4 and GIF and reading it back (the runner's software encoder) |
| Test-owned windows and processes | Local and CI | Window capture of the test's own window, the recording frame excluded from capture, a test's own `127.0.0.1` listener found and ended |
| Windows Sandbox | Local, by an agent | The selector from each shortcut, Copy to the clipboard (Sandbox clipboard), Print Screen with the Windows setting on and off, each with a Chippi window and with another app in the foreground, key-to-frozen-screen timing with the virtual GPU, recording with sound from a test tone in the Sandbox, the admin helper's UAC launch and verbs, uninstalling throwaway apps registered by the test (fake `Uninstall` entries pointing at a test exe), the cleaner on the Sandbox's own folders. winget isn't in the Sandbox: a run that needs it installs it there with `Repair-WinGetPackageManager` and needs networking on, so it's a separate `.wsb` with clipboard redirection still off [winget Sandbox, wsb]. The Sandbox's virtual GPU is on by default on x64 [wsb] |
| Travis's PC, read-only | Local | DDC/CI reads, the port list, winget's update list (reading only) |
| Travis's live checks | His desk | The freeze and markup speed by feel against Snipping Tool, both monitors and their scales, brightness on both monitors, recording with his audio, updates and uninstalls of throwaway installs he picks |

The Sandbox has one virtual monitor with no DDC/CI, so mixed DPI, two-monitor regions and brightness are live-check items.

## 15. Build order and sizes

Sizes: **S** about a day, **M** a few days, **L** a week or more, each one PR. The waves are Marge's, set by Travis in Step 2.

**Wave 1, capture core:**
1. The capture spike (S, no PR): the three ways to read the screen, timed in the Sandbox.
2. `Chippi.Capture` and the selector overlay (L, Kayla): the freeze, window snapping, keys, magnifier, Print Screen in the shortcut service, the Print Screen setting. New project in `Chippi.slnx`; shared code in Core and Chippi.UI, called out in the PR.
3. Screen capture with markup (L, Karl), Color picker (S, Herb), Text and QR (M, Herb, with ZXing.Net).
4. Displays and brightness (M, Kayla), alongside.

**Wave 2, motion:** Screen recording (L, Karl: WGC sessions, the transcoder, audio mix, GIF), then Media tools (M, Herb) on the same encoder code.

**Wave 3, apps:** the admin helper (M, Kayla, full review: it's the first elevated code), then App updates (M), Uninstaller (L), Cleaner (M) and Port manager (M).

## 16. Not verified

- Which way of reading the screen is fastest from key to frozen screen (wave 1 spike).
- Print Screen while Windows' setting is on: whether `RegisterHotKey` succeeds and `WM_HOTKEY` arrives with a Chippi window in front and with another app in front, whether the hub's hook sees the key, and whether turning the setting off needs a sign-out (wave 1 spike, Sandbox).
- The helper's leftover checks against junctions, renames between check and delete, and ownership (fixtures in CI and the Sandbox, section 6).
- No prompt for borderless WGC capture in an unpackaged app.
- Capturing an elevated window with `CreateForWindow` from a standard user.
- Colors and captures with HDR on; whether Travis's monitors run HDR.
- WIC's GIF palette quality on screen recordings.
- OCR on the CI runner.
- The admin helper end to end: Sandbox only, since a UAC prompt on Travis's PC is his to approve. Its peer check (`GetNamedPipeServerProcessId`) and winget's command line run elevated are from reading, not tried.
- That a standard user can enumerate services by PID and set laptop brightness through WMI (both what the tools do; neither documented).
- MKV decoding built into Windows (press reports only).
- DDC writes on Travis's monitors (reads only so far), and the laptop WMI path (no laptop).
- Mixed-DPI selection and two-monitor regions: live check.

## Sources

Read 2026-11-06. Behavior of the competing tools is in Karl's and Herb's specs.

- **Capture:** [IGraphicsCaptureItemInterop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nn-windows-graphics-capture-interop-igraphicscaptureiteminterop), [IsBorderRequired](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired), [WGC sample #97](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/issues/97), [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity), [HDR](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range), [SimpleRecorder](https://github.com/robmikh/SimpleRecorder)
- **Print Screen:** [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), [Insider 22621.1546](https://blogs.windows.com/windows-insider/2023/04/07/announcing-windows-11-insider-preview-build-22621-1546-and-22624-1546/), [Greenshot FAQ](https://getgreenshot.org/faq/my-prntscreen-key-no-longer-works-with-greenshot/), [ShareX#6581](https://github.com/ShareX/ShareX/issues/6581)
- **Encoding:** [WIC codecs](https://learn.microsoft.com/en-us/windows/win32/wic/native-wic-codecs), [HEIF codec](https://learn.microsoft.com/en-us/windows/win32/wic/heif-codec), [WIC metadata](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-native-image-format-metadata-queries), [InitializeFromBitmap](https://learn.microsoft.com/en-us/windows/win32/api/wincodec/nf-wincodec-iwicpalette-initializefrombitmap), [MF formats](https://learn.microsoft.com/en-us/windows/win32/medfound/supported-media-formats-in-media-foundation), [H.264 encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-encoder), [Media Feature Pack](https://support.microsoft.com/windows/media-feature-pack-for-windows-n-8622b390-4ce6-43c9-9b42-549e5328e407), [BtbN](https://github.com/BtbN/FFmpeg-Builds), [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)
- **Text and QR:** [Text Extractor](https://learn.microsoft.com/en-us/windows/powertoys/text-extractor), [Text recognition](https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition), [ZXing.Net](https://www.nuget.org/packages/ZXing.Net)
- **Admin helper:** [MSRC criteria](https://www.microsoft.com/en-us/msrc/windows-security-servicing-criteria), [pipe security](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights), [CreateNamedPipe](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createnamedpipea), [DLL security](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-security), [NativeAOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/), [PowerToys admin](https://learn.microsoft.com/en-us/windows/powertoys/administrator), [reparse points](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-points)
- **winget:** [winget](https://learn.microsoft.com/en-us/windows/package-manager/winget/#administrator-considerations), [winget list](https://learn.microsoft.com/en-us/windows/package-manager/winget/list), [ComInterop](https://www.nuget.org/packages/Microsoft.WindowsPackageManager.ComInterop), [Microsoft.WinGet.Client](https://github.com/microsoft/winget-cli/blob/master/src/PowerShell/Microsoft.WinGet.Client.Engine/Helpers/ManagementDeploymentFactory.cs), [winget#4377](https://github.com/microsoft/winget-cli/issues/4377), [UniGetUI](https://github.com/Devolutions/UniGetUI/tree/main/src/UniGetUI.PackageEngine.Managers.WinGet), [winget Sandbox](https://learn.microsoft.com/en-us/windows/package-manager/winget/#install-winget-on-windows-sandbox)
- **Uninstall and delete:** [Uninstall key](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key), [registry redirection](https://learn.microsoft.com/en-us/windows/win32/winprog64/shared-registry-keys), [ARPSYSTEMCOMPONENT](https://learn.microsoft.com/en-us/windows/win32/msi/arpsystemcomponent), [PackageManager](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager.removepackageasync), [IFileOperation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)
- **Brightness:** [monitor config](https://learn.microsoft.com/en-us/windows/win32/monitor/using-the-high-level-monitor-configuration-functions), [WmiSetBrightness](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmisetbrightness-method-in-class-wmimonitorbrightnessmethods), [Monitorian](https://github.com/emoacht/Monitorian), [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray), [KDE PowerDevil](https://invent.kde.org/plasma/powerdevil/-/merge_requests/456)
- **Ports:** [GetExtendedTcpTable](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable), [SetTcpEntry](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-settcpentry), [GetOwnerModuleFromTcpEntry](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getownermodulefromtcpentry), [OpenProcess](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess), [process rights](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights)
- **Sandbox:** [wsb](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file)
