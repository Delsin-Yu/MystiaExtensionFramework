// Loads Mystia.Bootstrap.dll into a game process that the store front started itself.
//
// Why: the game calls SteamAPI_RestartAppIfNecessary at startup. A process we create ourselves is
// not a child of the Steam client, so that call relaunches the game through steam://run/<appid> and
// the injected copy exits. Standing where the game's own imports look (the executable directory)
// keeps the store front as the launcher, so its launch checks see exactly what they expect.
//
// The build output is a drop-in replacement for one system DLL; deploy it as <game>\version.dll
// (UnityPlayer.dll imports VERSION.dll, and VERSION.dll is not a KnownDLL). Every export forwards to
// the real DLL under the system directory, and a worker thread loads the bootstrap from the launcher
// directory named by Mystia.Proxy.txt next to this file.
#include <windows.h>
#include <cstdio>

namespace
{
HMODULE g_real = nullptr;
wchar_t g_ownDirectory[MAX_PATH] = {};
wchar_t g_logPath[MAX_PATH] = {};

// The bootstrap and the host log into the launcher directory; this log starts at the machine's temp
// directory and moves there, because the steps that matter - loading the real DLL and finding the
// launcher directory - both happen before the bootstrap exists and fail silently otherwise.
void Log(const wchar_t* message)
{
    wchar_t path[MAX_PATH];
    if (g_logPath[0] != 0)
    {
        wcscpy_s(path, g_logPath);
    }
    else
    {
        wchar_t directory[MAX_PATH];
        const DWORD length = GetTempPathW(MAX_PATH, directory);
        swprintf_s(path, L"%sMystia.Proxy.log", length == 0 ? L".\\" : directory);
    }

    FILE* file = nullptr;
    if (_wfopen_s(&file, path, L"a, ccs=UTF-8") != 0 || file == nullptr)
        return;
    SYSTEMTIME now;
    GetLocalTime(&now);
    fwprintf(file, L"%02u:%02u:%02u.%03u %s\n", now.wHour, now.wMinute, now.wSecond, now.wMilliseconds, message);
    fclose(file);
}

void LogCode(const wchar_t* message, unsigned int code)
{
    wchar_t line[512];
    swprintf_s(line, L"%s (0x%08X)", message, code);
    Log(line);
}

// One line, made absolute against the proxy directory when it is not already. Written by whatever
// installs the proxy, so it is read permissively: UTF-8 with or without a BOM, or UTF-16LE.
bool ReadLauncherDirectory(wchar_t* target)
{
    wchar_t path[MAX_PATH];
    swprintf_s(path, L"%s\\Mystia.Proxy.txt", g_ownDirectory);

    FILE* file = nullptr;
    if (_wfopen_s(&file, path, L"rb") != 0 || file == nullptr)
    {
        Log(L"Mystia.Proxy.txt is missing.");
        return false;
    }

    unsigned char raw[4096] = {};
    const size_t read = fread(raw, 1, sizeof(raw) - 2, file);
    fclose(file);

    wchar_t line[2048] = {};
    if (read >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
    {
        wcsncpy_s(line, reinterpret_cast<const wchar_t*>(raw + 2), (read - 2) / sizeof(wchar_t));
    }
    else
    {
        const char* text = reinterpret_cast<const char*>(raw);
        if (read >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            text += 3;
        MultiByteToWideChar(CP_UTF8, 0, text, -1, line, 2048);
    }

    size_t length = wcslen(line);
    while (length > 0 && (line[length - 1] == L'\r' || line[length - 1] == L'\n' || line[length - 1] == L' ' || line[length - 1] == L'\t'))
        line[--length] = 0;
    if (length == 0)
    {
        Log(L"Mystia.Proxy.txt is empty.");
        return false;
    }

    if (line[1] == L':' || line[0] == L'\\' || line[0] == L'/')
        wcsncpy_s(target, MAX_PATH, line, MAX_PATH - 1);
    else
        swprintf_s(target, MAX_PATH, L"%s\\%s", g_ownDirectory, line);
    return true;
}

// Loading the bootstrap from a thread keeps the loader lock free while a second managed runtime is
// brought up. The bootstrap only hooks il2cpp_init, which the player calls long after this.
DWORD WINAPI StartThread(LPVOID)
{
    wchar_t launcherDirectory[MAX_PATH] = {};
    if (!ReadLauncherDirectory(launcherDirectory))
        return 0;

    wchar_t line[MAX_PATH * 2];
    swprintf_s(g_logPath, L"%s\\proxy.log", launcherDirectory);
    swprintf_s(line, L"launcher directory: %s", launcherDirectory);
    Log(line);

    swprintf_s(line, L"%s\\Mystia.Bootstrap.dll", launcherDirectory);
    const HMODULE bootstrap = LoadLibraryW(line);
    if (bootstrap == nullptr)
    {
        LogCode(L"Mystia.Bootstrap.dll did not load", GetLastError());
        Log(line);
        return 0;
    }

    Log(L"Mystia.Bootstrap.dll loaded. The bootstrap owns il2cpp_init from here.");
    return 0;
}
}

// Set up as early as the loader allows: the real DLL first, because our own exports are reachable
// from this instant, then the bootstrap.
//
// This one stays outside the anonymous namespace above: the runtime exports DllMain from the module
// it hands to the loader, and an internal-linkage definition is simply a different function.
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH)
        return TRUE;
    DisableThreadLibraryCalls(instance);

    wchar_t path[MAX_PATH];
    GetModuleFileNameW(instance, path, MAX_PATH);
    wchar_t* slash = wcsrchr(path, L'\\');
    if (slash != nullptr)
    {
        *slash = 0;
        wcscpy_s(g_ownDirectory, path);
    }

    wchar_t systemDirectory[MAX_PATH];
    const UINT systemLength = GetSystemDirectoryW(systemDirectory, MAX_PATH);
    if (systemLength == 0 || systemLength >= MAX_PATH)
    {
        Log(L"GetSystemDirectoryW failed.");
        return TRUE;
    }

    swprintf_s(path, L"%s\\version.dll", systemDirectory);
    g_real = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    Log(g_real != nullptr ? L"the system VERSION.dll is loaded" : L"the system VERSION.dll did not load");
    if (g_real == nullptr)
        LogCode(L"LoadLibraryExW failed", GetLastError());

    CreateThread(nullptr, 0, &StartThread, nullptr, 0, nullptr);
    return TRUE;
}

// Every export the system DLL has, forwarded. UnityPlayer.dll itself imports three of them
// (GetFileVersionInfoA, GetFileVersionInfoSizeA, VerQueryValueA); the rest exist because other
// modules in the process may bind to this file too, and an unresolved import aborts startup.
//
// The functions carry a proxy_ prefix and the export table (version.def) renames them, so these
// definitions never collide with the declarations in winver.h that <windows.h> already pulled in.
#define MYSTIA_PROXY(ret, name, params, args)                                                  \
    namespace { using fn_##name = ret(__stdcall*) params; fn_##name p_##name = nullptr; }      \
    extern "C" ret __stdcall proxy_##name params                                               \
    {                                                                                          \
        if (p_##name == nullptr && g_real != nullptr)                                          \
            p_##name = reinterpret_cast<fn_##name>(GetProcAddress(g_real, #name));             \
        if (p_##name == nullptr)                                                               \
        {                                                                                      \
            SetLastError(ERROR_PROC_NOT_FOUND);                                                \
            return 0;                                                                          \
        }                                                                                      \
        return p_##name args;                                                                  \
    }

MYSTIA_PROXY(BOOL, GetFileVersionInfoA, (LPCSTR a, DWORD b, DWORD c, LPVOID d), (a, b, c, d))
MYSTIA_PROXY(BOOL, GetFileVersionInfoW, (LPCWSTR a, DWORD b, DWORD c, LPVOID d), (a, b, c, d))
MYSTIA_PROXY(BOOL, GetFileVersionInfoByHandle, (DWORD a, HANDLE b, DWORD c, DWORD d, LPVOID e), (a, b, c, d, e))
MYSTIA_PROXY(BOOL, GetFileVersionInfoExA, (DWORD a, LPCSTR b, DWORD c, DWORD d, LPVOID e), (a, b, c, d, e))
MYSTIA_PROXY(BOOL, GetFileVersionInfoExW, (DWORD a, LPCWSTR b, DWORD c, DWORD d, LPVOID e), (a, b, c, d, e))
MYSTIA_PROXY(DWORD, GetFileVersionInfoSizeA, (LPCSTR a, LPDWORD b), (a, b))
MYSTIA_PROXY(DWORD, GetFileVersionInfoSizeW, (LPCWSTR a, LPDWORD b), (a, b))
MYSTIA_PROXY(DWORD, GetFileVersionInfoSizeExA, (DWORD a, LPCSTR b, LPDWORD c), (a, b, c))
MYSTIA_PROXY(DWORD, GetFileVersionInfoSizeExW, (DWORD a, LPCWSTR b, LPDWORD c), (a, b, c))
MYSTIA_PROXY(DWORD, VerFindFileA, (DWORD a, LPCSTR b, LPCSTR c, LPCSTR d, LPSTR e, PUINT f, LPSTR g, PUINT h), (a, b, c, d, e, f, g, h))
MYSTIA_PROXY(DWORD, VerFindFileW, (DWORD a, LPCWSTR b, LPCWSTR c, LPCWSTR d, LPWSTR e, PUINT f, LPWSTR g, PUINT h), (a, b, c, d, e, f, g, h))
MYSTIA_PROXY(DWORD, VerInstallFileA, (DWORD a, LPCSTR b, LPCSTR c, LPCSTR d, LPCSTR e, LPCSTR f, LPSTR g, PUINT h), (a, b, c, d, e, f, g, h))
MYSTIA_PROXY(DWORD, VerInstallFileW, (DWORD a, LPCWSTR b, LPCWSTR c, LPCWSTR d, LPCWSTR e, LPCWSTR f, LPWSTR g, PUINT h), (a, b, c, d, e, f, g, h))
MYSTIA_PROXY(DWORD, VerLanguageNameA, (DWORD a, LPSTR b, DWORD c), (a, b, c))
MYSTIA_PROXY(DWORD, VerLanguageNameW, (DWORD a, LPWSTR b, DWORD c), (a, b, c))
MYSTIA_PROXY(BOOL, VerQueryValueA, (LPCVOID a, LPCSTR b, LPVOID* c, PUINT d), (a, b, c, d))
MYSTIA_PROXY(BOOL, VerQueryValueW, (LPCVOID a, LPCWSTR b, LPVOID* c, PUINT d), (a, b, c, d))
