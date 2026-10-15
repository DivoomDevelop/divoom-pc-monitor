#include <windows.h>
#include <cstring>
#include "ADLX.h"
#include "ADLXVersion.h"
#include "IPerformanceMonitoring.h"

// Only this small C ABI is consumed by .NET. All AMD C++ interfaces stay native.
struct AdlxMetric {
    char name[256];
    char pnp[512];
    double usage, temperature, hotspot;
    unsigned int valid;
};
static HMODULE module = nullptr;
static adlx::IADLXSystem* systemApi = nullptr;
static adlx::IADLXPerformanceMonitoringServices* performance = nullptr;
static ADLXTerminate_Fn terminateApi = nullptr;

extern "C" __declspec(dllexport) void __cdecl AdlxClose() {
    if (performance) { performance->Release(); performance = nullptr; }
    if (systemApi && terminateApi) terminateApi();
    systemApi = nullptr;
    terminateApi = nullptr;
    if (module) { FreeLibrary(module); module = nullptr; }
}
extern "C" __declspec(dllexport) int __cdecl AdlxOpen() {
    if (systemApi) return 0;
    // Load the installed display driver's DLL, never a download or current-directory DLL.
    module = LoadLibraryExW(L"amdadlx64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!module) return -1;
    auto initialize = reinterpret_cast<ADLXInitialize_Fn>(GetProcAddress(module, ADLX_INIT_FUNCTION_NAME));
    terminateApi = reinterpret_cast<ADLXTerminate_Fn>(GetProcAddress(module, ADLX_TERMINATE_FUNCTION_NAME));
    if (!initialize || !terminateApi) { AdlxClose(); return -2; }
    // ADLX 1.0 ABI is sufficient for the read-only metrics used here, including older drivers.
    const adlx_uint64 version = (static_cast<adlx_uint64>(1) << 48);
    ADLX_RESULT result = initialize(version, &systemApi);
    if (result != ADLX_OK || !systemApi) { AdlxClose(); return static_cast<int>(result) + 100; }
    result = systemApi->GetPerformanceMonitoringServices(&performance);
    if (result != ADLX_OK || !performance) { AdlxClose(); return static_cast<int>(result) + 200; }
    return 0;
}
extern "C" __declspec(dllexport) int __cdecl AdlxRead(AdlxMetric* output, int capacity) {
    if (!output || capacity <= 0 || !performance || !systemApi) return -1;
    adlx::IADLXGPUList* gpus = nullptr;
    if (systemApi->GetGPUs(&gpus) != ADLX_OK || !gpus) return -2;
    int count = 0;
    for (adlx_uint i = 0; i < gpus->Size() && count < capacity; ++i) {
        adlx::IADLXGPU* gpu = nullptr;
        if (gpus->At(i, &gpu) != ADLX_OK || !gpu) continue;
        AdlxMetric item{};
        const char* name = nullptr;
        const char* pnp = nullptr;
        if (gpu->Name(&name) == ADLX_OK && name) strncpy_s(item.name, name, _TRUNCATE);
        if (gpu->PNPString(&pnp) == ADLX_OK && pnp) strncpy_s(item.pnp, pnp, _TRUNCATE);
        adlx::IADLXGPUMetrics* metrics = nullptr;
        if (performance->GetCurrentGPUMetrics(gpu, &metrics) == ADLX_OK && metrics) {
            if (metrics->GPUUsage(&item.usage) == ADLX_OK) item.valid |= 1;
            if (metrics->GPUTemperature(&item.temperature) == ADLX_OK) item.valid |= 2;
            if (metrics->GPUHotspotTemperature(&item.hotspot) == ADLX_OK) item.valid |= 4;
            metrics->Release();
        }
        output[count++] = item;
        gpu->Release();
    }
    gpus->Release();
    return count;
}
