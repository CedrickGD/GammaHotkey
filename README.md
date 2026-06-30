# GammaHotkey

Change your screen **gamma with a hotkey or mouse button** — no more opening the
NVIDIA Control Panel and dragging the slider every time. Bind a key or a Logitech
mouse button, press it in‑game, and your gamma jumps to a preset. A beautiful little
Windows tray app that also **generates the Logitech G HUB Lua script** for you.

> **γ** Built with C# / WPF (.NET 9). Single window, lives in the system tray, no installer.

---

## What it does

- **Gamma presets** — six named levels (`Low · Mid · Normal · Higher · High · Max`),
  each editable to any value **0.1 – 2.5**. Higher = brighter mid‑tones; `1.0` is the
  Windows default.
- **Two trigger styles, configurable in the UI**
  - **Cycle mode** — one key/button steps through the presets you pick (e.g.
    `Normal → Higher → High → Max`, then wraps around).
  - **Direct bindings** — each key/button jumps straight to one preset.
- **Triggers can be** a keyboard key, a mouse side‑button (Mouse 3/4/5), **or** an
  F13–F24 key that a **G HUB Lua script** sends from any Logitech mouse button.
- **G HUB export** — generates a ready‑to‑paste Lua script that maps your mouse
  buttons to the right hotkeys. Copy it or save it as `.lua`.
- **Live preview** — drag the slider and watch your screen change instantly; `Apply / Test`
  and `Reset to 1.0` buttons.
- **Runs in the tray**, optional **run‑on‑startup**, applies to **all monitors**,
  and **restores your original gamma on exit**.

It uses the Windows GDI gamma‑ramp API (`SetDeviceGammaRamp`) — the *same* hardware
lookup table the NVIDIA/AMD/Intel control‑panel gamma sliders write to — so it's
**GPU‑agnostic** and needs no vendor SDK.

---

## Build & run

You need the **.NET 9 SDK** (or newer) on Windows.

```powershell
cd GammaHotkey
dotnet build -c Release
# run it:
.\src\GammaHotkey\bin\Release\net9.0-windows\GammaHotkey.exe
```

Or open `GammaHotkey.sln` in Visual Studio 2022+ and press F5.

The app starts as a window; **closing or minimizing it tucks it into the system tray**
(right‑click the tray icon for Settings / Listening / Run on startup / Exit). Launch
with `--tray` to start hidden (this is what the “run on startup” entry uses).

---

## Using it

1. **Set your presets.** Edit the six values if you like. Drag **Live preview** and hit
   **Apply / Test** to see a value on your screen right now.
2. **Pick a trigger style.**
   - *Cycle mode:* tick the presets you want in the cycle and set an **Advance key**.
   - *Direct bindings:* add rows, each mapping one key/button to one preset.
3. **Capture a trigger.** Click the pill and **press a key or a mouse side‑button**.
   For an **F13–F24** key (which keyboards don’t physically have — ideal for G HUB),
   click the **▾** on the pill and pick one.
4. **Turn on “Start listening”** (top‑right). Now your triggers change gamma globally,
   even in‑game.

### The G HUB flow (Logitech mouse)

Logitech mice route extra buttons through G HUB, and a G HUB Lua script can only send
**keystrokes** — it can’t touch gamma directly. So the model is:

```
G HUB mouse button  ──Lua PressKey──▶  F13 (etc.)  ──GammaHotkey listens──▶  gamma changes
```

1. In GammaHotkey, set your trigger(s) to **F13–F24** keys (the ▾ menu).
2. Click **Generate Lua script**, then **Copy** (or **Save .lua**).
3. In **G HUB → your profile → Assignments → Scripting**, create/edit a Lua script,
   paste it, and **Save**. Keep that profile active.
4. Keep GammaHotkey running with **Listening** on.

The generated script has a `bindings` table like `[4] = "f13"` (mouse button 4 → F13).
Change the button numbers to match the buttons you want — the comments explain the
numbering (`2=Right 3=Middle 4=Back 5=Forward 6+=extra`). Uncomment the
`OutputLogMessage` line in the G HUB console to discover your own mouse’s button numbers.

> Prefer no G HUB? Just bind a **mouse side‑button or a normal key** directly — GammaHotkey
> detects those itself, no Lua needed. G HUB is only needed for buttons Windows can’t
> see as standard mouse buttons (G‑keys, extra buttons on a G502/G600, etc.).

---

## Troubleshooting gamma

The gamma‑ramp API works on essentially every normal desktop GPU output, but Windows
**disables it in a few situations**. If `Apply / Test` says *“Couldn’t set gamma”*:

- **HDR is on.** While Windows HDR is enabled, the gamma ramp is disabled (the NVIDIA
  gamma slider goes inert too). **Turn HDR off** for the display you want to adjust.
- **Remote Desktop / virtual or streaming display.** RDP and many virtual‑display
  drivers (Parsec, Sunshine, headless/IddCx adapters) don’t implement gamma ramps, so
  the call fails. It works when you’re at the physical machine on a real GPU output.
- **“Windows limited the range.”** Windows rejects gamma curves that deviate too far
  from linear (an anti‑“black screen” safety). Extreme values (very low gamma, etc.) may
  be silently clamped. To unlock the full range, set this registry value (admin, then
  **reboot / sign out**):

  ```
  HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM
      GdiIcmGammaRange  (REG_DWORD) = 256
  ```

  `256` = allow any adjustment; `0` = none. GammaHotkey never writes this for you — it’s
  a power‑user opt‑in.

GammaHotkey **captures your current ramp at launch and restores it on exit**, and
re‑applies your gamma after display changes / sleep. If a crash ever leaves your screen
on a weird ramp, just relaunch and hit **Reset to 1.0**, or sign out and back in.

---

## How it’s built

```
src/GammaHotkey/
  Models/        TriggerInput, gamma presets, AppConfig (+ JSON)
  Services/      NativeMethods (P/Invoke), GammaController, HookService,
                 ConfigStore, StartupManager, LuaGenerator, KeyNames
  ViewModels/    MainViewModel, PresetViewModel, DirectBindingViewModel
  Controls/      KeyCapturePill (capture a key / mouse button, or pick F13–F24)
  Themes/        Theme.xaml (dark UI: brushes + control styles)
  App / MainWindow
```

- **GammaController** builds the 768‑entry ramp (`value = (i/255)^(1/gamma) · 65535`),
  applies it to every attached monitor, verifies the write, and restores the original.
- **HookService** runs global low‑level keyboard + mouse hooks on a dedicated
  message‑pumped thread; it dispatches bound triggers while *listening* and also powers
  the one‑shot key‑capture used by the UI.
- **Config** is saved to `%APPDATA%\GammaHotkey\config.json`.

No third‑party UI libraries — the whole look is hand‑rolled WPF styles.

---

## Notes

- Binding a plain mouse button or key means GammaHotkey **swallows** it while listening
  (so it doesn’t also page‑back / type) — that’s expected for a hotkey. F13–F24 are the
  cleanest choice because nothing else uses them.
- Run‑on‑startup is a per‑user entry (`HKCU\…\Run`) — **no admin required**.
