# Remotier Native Documentation

## 1. Overview
**Remotier Native** (`Native.dll`) is the high-performance core responsible for Screen Capture and Image Encoding. It isolates Unmanaged DirectX/C++ resources from the Managed .NET environment to ensure stability and speed. It is built as a standard C++ DLL exporting "C" functions.

---

## 2. API Reference (NativeHost.cpp)
All functions are exported via `extern "C"` using `__declspec(dllexport)`.

### `int Init(int monitorIndex)`
Initializes the DirectX Device, Context, and Duplication Output.
*   **Parameters**:
    *   `monitorIndex` (int): Zero-based index of the monitor to capture (0 = Primary).
*   **Returns**: `0` on Success, `1` on Failure (e.g. GPU device lost).
*   **Thread Safety**: Must be called from the main Capture thread.

### `int CaptureAndEncode(int scalePercent, int quality, BYTE** outData, int* outSize)`
Core function called every frame (e.g., 60 times/sec).
*   **Parameters**:
    *   `scalePercent` (int): Resolution scaling factor (e.g., 100 for native, 50 for half-size).
    *   `quality` (int): JPEG Quality (0-100).
    *   `outData` (BYTE**): **Output**. Receives the pointer to the internal `std::vector` buffer.
    *   `outSize` (int*): **Output**. Receives the size of the buffer.
*   **Returns**: `1` if a new frame was captured, `0` if timeout/no update.
*   **Memory Note**: The returned pointer points to *internal DLL memory*. It is valid only until the next call to `CaptureAndEncode`. The Caller (C#) **MUST COPY** this data immediately (via `Marshal.Copy`) and not hold the pointer.

### `void Release()`
Cleans up all COM interfaces and releases memory. Must be called when stopping the host.

---

## 3. Internal Architecture

### 3.1 DXGI Desktop Duplication (`DXGIManager`)
This class wraps the `IDXGIOutputDuplication` interface, which provides efficient access to desktop frames directly on the GPU.

#### State Machine
1.  **Initialize**:
    *   Creates `ID3D11Device` with `D3D_DRIVER_TYPE_HARDWARE`.
    *   Enumerates `IDXGIAdapter` -> `IDXGIOutput` -> `IDXGIOutput1`.
    *   Calls `DuplicateOutput` to get the Duplication interface.
2.  **Acquire Frame**:
    *   Calls `duplication->AcquireNextFrame(timeout, ...)`
    *   If `DXGI_ERROR_WAIT_TIMEOUT`, returns. The desktop hasn't changed.
    *   If Success, retrieves the `ID3D11Texture2D` containing the desktop image.
3.  **Release Frame**:
    *   Calls `duplication->ReleaseFrame()`.
    *   **CRITICAL**: This MUST be called before the next Acquire call. Failure to do so will freeze the duplication interface.

#### Threading Constraints
*   `ID3D11Device` is thread-safe.
*   `ID3D11DeviceContext` is **NOT** thread-safe. All rendering/copying commands must happen on the same thread (the Capture Thread).

### 3.2 WIC Encoding Pipeline
(Implementation inferred from high-level flow)

1.  **GPU to CPU Transfer**:
    *   The Desktop Texture is on the GPU (VRAM).
    *   Direct access by CPU is slow or impossible depending on usage flags.
    *   **Staging Texture**: A generic D3D11 Texture with `D3D11_USAGE_STAGING` and `D3D11_CPU_ACCESS_READ`.
    *   `context->CopyResource(staging, desktopTexture)`: Copies VRAM -> System Memory (or readable VRAM).
    *   `context->Map(staging, ...)`: Locks the memory for reading by the CPU.
2.  **WIC (Windows Imaging Component)**:
    *   Creates a `IWICBitmap` from the Mapped memory pointer.
    *   Creates a `IWICBitmapEncoder` (GUID_ContainerFormatJpeg).
    *   Sets Scaling properties if `scalePercent < 100`.
    *   Encodes to a generic `IStream` (backed by a `std::vector` or global memory block).
3.  **Result**:
    *   The `std::vector` now contains the JPEG bytes. `outData` is set to `vector.data()`.

---

## 4. Performance & Memory
*   **Latency**: The pipeline is designed for < 10ms processing time. The main bottleneck is typically the JPEG Encoding step on the CPU.
*   **Zero-Copy Limitations**: To encode with standard WIC/LibJPEG, the data *must* move to CPU RAM. This incurs a PCIe bus transfer cost.
*   **Future Optimization**: Use Hardware Encoding (NVENC/AMF) via Media Foundation to keep data on the GPU and export only the compressed bitstream.

## 5. Build Configuration
*   **Platform**: `x64` (Mandatory for modern DXGI).
*   **Dependencies**: `d3d11.lib`, `dxgi.lib`, `windowscodecs.lib`.
*   **Runtime**: Multi-threaded DLL (`/MD`).
