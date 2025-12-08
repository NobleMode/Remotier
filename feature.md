# Remote Access App – Feature List

## Core Features
- [x] **Host Mode**
  - [x] Share screen with client
  - [x] Start/stop hosting
  - [x] Display host IP / code
  - [x] **Smart Idle**: Pause capture when no clients connected

- [x] **Client Mode**
  - [x] Connect to host
  - [x] Display remote stream
  - [x] Windowed Mode by Default (Toggleable)
  - [ ] Send mouse and keyboard input (Implemented but untested)

- [x] **Screen Capture**
  - [x] DXGI Desktop Duplication
  - [x] Multi-monitor support (Selectable source)
  - [x] Configurable FPS cap (30/60/120)

- [x] **Streaming**
  - [x] UDP video streaming
  - [x] TCP control channel
  - [x] Frame compression (JPEG)

- **Input Control** (Untested)
  - Mouse movement, click, scroll
  - Keyboard press/release
  - Optional relative input mode

---

## Quality / Performance Controls
- [ ] Speed / Quality / Balanced presets
- [x] Encoding quality slider (Mapped to Resolution Scale)
- [x] **Resolution Scaling**: 
    - Dynamic scaling based on Client Window Size
    - Manual scaling via Quality Slider
- [x] FPS options (30/60/120)
- [ ] Bandwidth estimation
- [ ] Auto-latency correction

---

## Security
- [ ] Password/PIN for host session
- [ ] Optional encryption (AES or SSL)
- [ ] Connection authorization prompt
- [x] Input block/allow switch (Toggle Input button)
- [x] Read-only mode (via Toggle Input)

---

## Advanced Features (Optional)
- [ ] Clipboard sync (text/files)
- [ ] File transfer
- [ ] Built-in chat
- [ ] Audio streaming (Opus)
- [x] Multi-client viewing (Host supports multiple, Viewers independent)
- [ ] Session recording
- [x] Window-only streaming (Host acts as window)
- [ ] GPU H.264 encoding (NVENC/AMF/QSV)

---

## User Interface (Pass Thru 1)
- [x] Simple Host / Connect main menu
- [x] **Global Dark Theme**
- [x] Connection status indicators (Client List)
- [x] FPS & latency display (Overlay)
- [x] Quality indicator (Resolution Stats)
- [x] Hotkeys (e.g., disconnect, fullscreen)
- [x] **Recent Connections** list

---

## Networking
- [ ] Port forwarding helper
- [ ] LAN discovery via broadcast
- [ ] Relay server fallback
- [x] Auto-reconnect on drop (Basic handling)
- [ ] Packet chunking and jitter buffer (Basic chunking implemented)

---

## System Features
- [ ] Wake-on-LAN support
- [ ] Auto-start host mode on boot
- [ ] Tray minimization
- [ ] Logging system
- [ ] Crash reporting

---

## Developer / Debug Tools
- [ ] Frame timing graph
- [ ] Capture debug overlay
- [x] Network stats overlay (Basic FPS/Res)
- [ ] Input event log
