# Remote Access App – Feature List

- [x] **Host Mode**
  - [x] Share screen with client
  - [x] Start/stop hosting
  - [x] Display host IP / code
  - [x] **Smart Idle**: Pause capture when no clients connected
  - [x] **Single Client Architecture**: Strictly customized for 1:1 sessions.

- [x] **Client Mode**
  - [x] Connect to host
  - [x] Display remote stream
  - [x] Windowed Mode by Default (Toggleable)
  - [x] Send mouse and keyboard input
  - [x] **Floating Pill Toolbar**: Modern overlay for controls.

- [x] **Screen Capture**
  - [x] DXGI Desktop Duplication
  - [x] Multi-monitor support (Selectable source)
  - [x] Configurable FPS cap (30/60/120)

- [x] **Streaming**
  - [x] UDP video streaming
  - [x] TCP control channel
  - [x] Frame compression (JPEG)

- [x] **Input Control**
  - [x] Mouse movement, click, scroll
  - [x] Keyboard press/release
  - [ ] Optional relative input mode

---

## Quality / Performance Controls
- [x] Speed / Quality / Balanced presets
- [x] Encoding quality slider (Mapped to Resolution Scale)
- [x] **Resolution Scaling**: 
    - Dynamic scaling based on Client Window Size
    - Manual scaling via Quality Slider
- [x] FPS options (30/60/120)
- [x] Bandwidth estimation
- [x] Auto-latency correction
- [x] Smart Frame Pacing (Host side)

---

## Security
- [ ] Password/PIN for host session
- [ ] Optional encryption (AES or SSL)
- [ ] Connection authorization prompt
- [x] Input block/allow switch (Toggle Input button)
- [x] Read-only mode (via Toggle Input)

---

## Advanced Features (Optional)
- [-] Clipboard sync (text/files) (Testing Needed)
- [-] File transfer (Testing Needed)
- [x] Built-in chat (Embedded)
- [ ] Audio streaming (Opus)
- [ ] Session recording
- [x] Window-only streaming (Host acts as window)
- [ ] GPU H.264 encoding (NVENC/AMF/QSV)

---

## User Interface (Pass Thru 2)
- [x] Simple Host / Connect main menu
- [x] **Global Dark Theme** (Modernized)
- [x] **Material Design Icons** (All Windows)
- [x] Connection status indicators (Client Info)
- [x] FPS & latency display (Overlay / Toolbar)
- [x] Quality indicator (Resolution Stats)
- [x] Hotkeys (e.g., disconnect, fullscreen)
- [x] **Recent Connections** list

---

## Networking
- [-] Port forwarding helper (Testing Needed)
- [x] LAN discovery via broadcast
- [ ] Relay server fallback
- [x] Auto-reconnect on drop
- [x] Packet chunking and jitter buffer (Jitter Buffer implemented)
- [x] Auto discovery of host

---

## System Features
- [ ] Wake-on-LAN support
- [ ] Auto-start host mode on boot
- [-] Tray minimization (Testing Needed)
- [ ] Logging system
- [ ] Crash reporting

---

## Developer / Debug Tools
- [ ] Frame timing graph
- [ ] Capture debug overlay
- [x] Network stats overlay (Basic FPS/Res)
- [ ] Input event log
