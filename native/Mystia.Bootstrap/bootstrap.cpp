#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
using il2cpp_init_fn = int (*)(const char*);
using hostfxr_handle = void*;

struct hostfxr_initialize_parameters
{
    size_t size;
    const wchar_t* host_path;
    const wchar_t* dotnet_root;
};

using hostfxr_initialize_for_dotnet_command_line_fn = int(__cdecl*)(int, const wchar_t**, const hostfxr_initialize_parameters*, hostfxr_handle*);
using hostfxr_get_runtime_delegate_fn = int(__cdecl*)(hostfxr_handle, int, void**);
using hostfxr_close_fn = int(__cdecl*)(hostfxr_handle);
using get_function_pointer_fn = int(__cdecl*)(const wchar_t*, const wchar_t*, const wchar_t*, void*, void*, void**);
using mystia_init_fn = void(__cdecl*)(const wchar_t*);

constexpr int hdt_get_function_pointer = 6;

HMODULE g_module = nullptr;
uint8_t* g_trampolineMemory = nullptr;
il2cpp_init_fn g_original = nullptr;
wchar_t g_launcherDirectory[MAX_PATH] = {};
volatile LONG g_managedStarted = 0;

struct UnicodeString
{
    USHORT Length;
    USHORT MaximumLength;
    PWSTR Buffer;
};

struct LoadedNotification
{
    ULONG Flags;
    UnicodeString* FullDllName;
    UnicodeString* BaseDllName;
    PVOID DllBase;
    ULONG SizeOfImage;
};

union NotificationData
{
    LoadedNotification Loaded;
    LoadedNotification Unloaded;
};

using LdrRegisterDllNotification_fn = LONG(NTAPI*)(ULONG, void(__stdcall*)(ULONG, const NotificationData*, void*), void*, void**);

void Log(const wchar_t* message)
{
    wchar_t path[MAX_PATH];
    if (g_launcherDirectory[0] == 0)
        return;
    swprintf_s(path, L"%s\\bootstrap.log", g_launcherDirectory);
    FILE* file = nullptr;
    if (_wfopen_s(&file, path, L"a, ccs=UTF-8") != 0 || file == nullptr)
        return;
    fwprintf(file, L"%s\n", message);
    fclose(file);
}

void LogCode(const wchar_t* message, unsigned int code)
{
    wchar_t line[512];
    swprintf_s(line, L"%s (0x%08X)", message, code);
    Log(line);
}

bool InstallHook(void* target)
{
    auto bytes = static_cast<uint8_t*>(target);
    const uint8_t expected[] = {0x40, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B, 0xD9, 0x48, 0x8D, 0x15};
    if (std::memcmp(bytes, expected, sizeof(expected)) != 0)
    {
        Log(L"il2cpp_init prologue does not match the pinned 4.4.0e player. Mods were not started.");
        return false;
    }

    const int32_t displacement = *reinterpret_cast<int32_t*>(bytes + 12);
    uint8_t* leaTarget = bytes + 16 + displacement;
    auto trampoline = g_trampolineMemory;
    std::memcpy(trampoline, bytes, 9);
    trampoline[9] = 0x48;
    trampoline[10] = 0xBA;
    auto absolute = reinterpret_cast<uint64_t>(leaTarget);
    std::memcpy(trampoline + 11, &absolute, sizeof(absolute));
    trampoline[19] = 0x48;
    trampoline[20] = 0xB8;
    auto resume = reinterpret_cast<uint64_t>(bytes + 16);
    std::memcpy(trampoline + 21, &resume, sizeof(resume));
    trampoline[29] = 0xFF;
    trampoline[30] = 0xE0;
    g_original = reinterpret_cast<il2cpp_init_fn>(trampoline);

    DWORD oldProtect = 0;
    if (!VirtualProtect(bytes, 16, PAGE_EXECUTE_READWRITE, &oldProtect))
    {
        LogCode(L"VirtualProtect failed", GetLastError());
        return false;
    }

    int HookedIl2CppInit(const char* domain);
    bytes[0] = 0x48;
    bytes[1] = 0xB8;
    auto hook = reinterpret_cast<uint64_t>(&HookedIl2CppInit);
    std::memcpy(bytes + 2, &hook, sizeof(hook));
    bytes[10] = 0xFF;
    bytes[11] = 0xE0;
    bytes[12] = 0x90;
    bytes[13] = 0x90;
    bytes[14] = 0x90;
    bytes[15] = 0x90;
    VirtualProtect(bytes, 16, oldProtect, &oldProtect);
    FlushInstructionCache(GetCurrentProcess(), bytes, 16);
    Log(L"Hooked il2cpp_init.");
    return true;
}

void StartManaged()
{
    if (InterlockedCompareExchange(&g_managedStarted, 1, 0) != 0)
        return;

    wchar_t hostfxrPath[MAX_PATH];
    wchar_t runtimeConfig[MAX_PATH];
    wchar_t assemblyPath[MAX_PATH];
    swprintf_s(hostfxrPath, L"%s\\host\\hostfxr.dll", g_launcherDirectory);
    swprintf_s(runtimeConfig, L"%s\\host\\Mystia.Modding.Host.runtimeconfig.json", g_launcherDirectory);
    swprintf_s(assemblyPath, L"%s\\host\\Mystia.Modding.Host.dll", g_launcherDirectory);

    const HMODULE hostfxr = LoadLibraryW(hostfxrPath);
    if (hostfxr == nullptr)
    {
        LogCode(L"hostfxr.dll did not load", GetLastError());
        return;
    }

    auto initialize = reinterpret_cast<hostfxr_initialize_for_dotnet_command_line_fn>(GetProcAddress(hostfxr, "hostfxr_initialize_for_dotnet_command_line"));
    auto getDelegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(GetProcAddress(hostfxr, "hostfxr_get_runtime_delegate"));
    auto close = reinterpret_cast<hostfxr_close_fn>(GetProcAddress(hostfxr, "hostfxr_close"));
    if (initialize == nullptr || getDelegate == nullptr || close == nullptr)
    {
        Log(L"hostfxr exports are missing.");
        return;
    }

    hostfxr_initialize_parameters parameters{};
    parameters.size = sizeof(parameters);
    wchar_t hostDirectory[MAX_PATH];
    swprintf_s(hostDirectory, L"%s\\host", g_launcherDirectory);
    parameters.host_path = assemblyPath;
    parameters.dotnet_root = hostDirectory;

    const wchar_t* argv[] = { assemblyPath };
    hostfxr_handle context = nullptr;
    const int initResult = initialize(1, argv, &parameters, &context);
    if (initResult != 0 || context == nullptr)
    {
        LogCode(L"hostfxr_initialize_for_dotnet_command_line failed", static_cast<unsigned int>(initResult));
        Log(runtimeConfig);
        return;
    }

    // load_assembly_and_get_function_pointer puts the host in an isolated context.
    // MonoMod then binds a second MonoMod.Utils into the default context and its
    // ILGenerator proxy constraint fails. get_function_pointer stays on the default context.
    void* loadPtr = nullptr;
    const int delegateResult = getDelegate(context, hdt_get_function_pointer, &loadPtr);
    close(context);
    if (delegateResult != 0 || loadPtr == nullptr)
    {
        LogCode(L"hostfxr_get_runtime_delegate failed", static_cast<unsigned int>(delegateResult));
        return;
    }

    auto getFunctionPointer = reinterpret_cast<get_function_pointer_fn>(loadPtr);
    void* entry = nullptr;
    const int loadResult = getFunctionPointer(
        L"Mystia.Modding.Host.Entry, Mystia.Modding.Host",
        L"MystiaHostInitialize",
        reinterpret_cast<const wchar_t*>(static_cast<intptr_t>(-1)),
        nullptr,
        nullptr,
        &entry);
    if (loadResult != 0 || entry == nullptr)
    {
        LogCode(L"MystiaHostInitialize was not found", static_cast<unsigned int>(loadResult));
        return;
    }

    Log(L"Starting the managed host.");
    reinterpret_cast<mystia_init_fn>(entry)(g_launcherDirectory);
}

int HookedIl2CppInit(const char* domain)
{
    Log(L"il2cpp_init entered.");
    const int result = g_original(domain);
    Log(L"il2cpp_init returned.");
    StartManaged();
    return result;
}

void TryInstallFromModule(HMODULE module)
{
    if (g_original != nullptr || module == nullptr)
        return;
    auto init = GetProcAddress(module, "il2cpp_init");
    if (init == nullptr)
    {
        Log(L"GameAssembly.dll has no il2cpp_init export.");
        return;
    }

    InstallHook(reinterpret_cast<void*>(init));
}

void CALLBACK OnDllNotification(ULONG reason, const NotificationData* data, void*)
{
    if (reason != 1 || data == nullptr || data->Loaded.BaseDllName == nullptr || data->Loaded.BaseDllName->Buffer == nullptr)
        return;
    const size_t characters = data->Loaded.BaseDllName->Length / sizeof(wchar_t);
    if (characters == 16 && _wcsnicmp(data->Loaded.BaseDllName->Buffer, L"GameAssembly.dll", characters) == 0)
        TryInstallFromModule(static_cast<HMODULE>(data->Loaded.DllBase));
}

DWORD WINAPI BootThread(LPVOID)
{
    g_trampolineMemory = static_cast<uint8_t*>(VirtualAlloc(nullptr, 64, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (g_trampolineMemory == nullptr)
    {
        Log(L"Trampoline allocation failed.");
        return 0;
    }

    auto ntdll = GetModuleHandleW(L"ntdll.dll");
    auto registerNotification = reinterpret_cast<LdrRegisterDllNotification_fn>(GetProcAddress(ntdll, "LdrRegisterDllNotification"));
    void* cookie = nullptr;
    if (registerNotification != nullptr)
        registerNotification(0, &OnDllNotification, nullptr, &cookie);

    if (const auto existing = GetModuleHandleW(L"GameAssembly.dll"))
        TryInstallFromModule(existing);
    return 0;
}

void CaptureLauncherDirectory()
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(g_module, path, MAX_PATH);
    wchar_t* slash = wcsrchr(path, L'\\');
    if (slash != nullptr)
        *slash = 0;
    wcscpy_s(g_launcherDirectory, path);
}

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH)
        return TRUE;
    g_module = module;
    DisableThreadLibraryCalls(module);
    CaptureLauncherDirectory();
    CreateThread(nullptr, 0, &BootThread, nullptr, 0, nullptr);
    return TRUE;
}
