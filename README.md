# Remotier

**Remotier** is a high-performance, low-latency Remote Desktop application designed for 1:1 sessions on Windows. It combines a **native C++ core** for efficient DirectX screen capture with a **modern WPF UI** for a seamless user experience.

![Icon](App/Resources/remote.ico)

## Key Features

### 🚀 High Performance
-   **DirectX Desktop Duplication**: Uses efficient GPU-based capture via `Native.dll`.
-   **Low Latency**: Custom UDP protocol for video streaming.
-   **Adaptive Quality**: Dynamic resolution scaling and JPEG compression.
-   **Hardware Accelerated**: Leveraging WIC for fast image encoding.

### 🛠️ Powerful Tools
-   **Clipboard Synchronization**: Seamless text copy-paste between Host and Client.
-   **File Transfer**: Send files directly to the remote machine via Drag & Drop or UI.
-   **Smart Networking**:
    -   **UPnP Support**: Automatic port forwarding for WAN access.
    -   **Auto-Discovery**: Find hosts on the local network automatically.
    -   **Jitter Buffer**: Smooths out network instability.

### 🎨 Modern UI
-   **Dark Theme**: Professional VS-style dark mode.
-   **Tray Integration**: Minimizes to system tray for unobtrusive hosting.
-   **Overlay Toolbar**: Floating controls for easy access in full-screen mode.

## Architecture

Remotier uses a hybrid architecture:
*   **App (WPF)**: Manages UI, Networking (TCP/UDP), Input Injection, and Coordination.
*   **Native (C++)**: Handles `IDXGIOutputDuplication` and memory-efficient frame processing.

For deep technical details, see the documentation:
*   📄 **[Application Documentation](Docs/App_Documentation.md)** - Protocols, Threading, Services.
*   📄 **[Native Documentation](Docs/Native_Documentation.md)** - C++ Internals, DXGI Pipeline.

## Getting Started

### Prerequisites
*   **Visual Studio 2022** (or newer)
*   Workloads: `.NET Desktop Development`, `Desktop development with C++`
*   **.NET 8.0 SDK**

### Build Instructions
1.  **Clone the repository**.
2.  Open `Remotier.slnx` in Visual Studio.
3.  **Build Solution** (Ctrl+Shift+B).
    *   *Note*: The build process automatically compiles the C++ `Native.dll` and copies it to the output directory.
4.  Run `Remotier.exe`.

### Usage
*   **To Host**: Click "Start Hosting". Share your IP or use Auto-Discovery.
*   **To Connect**: Enter the Host IP (or select from Discovered list) and click "Connect".

## Developer Tools
*   **Host Stats**: Visible in the Host Window (Capture Time, Encode Time).
*   **Client Graph**: Press `F3` in the viewer to toggle the Frame Timing Graph.

## License
MIT License.
