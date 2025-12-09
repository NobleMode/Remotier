#pragma once

#ifdef NATIVE_EXPORTS
#define NATIVE_API __declspec(dllexport)
#else
#define NATIVE_API __declspec(dllimport)
#endif

extern "C"
{
    NATIVE_API int Init(int monitorIndex);
    NATIVE_API int CaptureAndEncode(int quality, BYTE** outData, int* outSize);
    NATIVE_API void Release();
}
