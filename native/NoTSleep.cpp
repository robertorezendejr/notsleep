#define UNICODE
#define _UNICODE
#include <windows.h>
#include <powrprof.h>
#include <shellapi.h>

#pragma comment(lib, "powrprof.lib")
#pragma comment(lib, "shell32.lib")

namespace {
constexpr UINT WM_TRAYICON = WM_APP + 1;
constexpr UINT TIMER_ID = 1;
constexpr UINT TIMER_INTERVAL_MS = 250;
constexpr ULONGLONG JIGGLE_INTERVAL_MS = 10ULL * 60ULL * 1000ULL;
constexpr GUID SUB_BUTTONS = {0x4f971e89, 0xeebd, 0x4455, {0xa8, 0xde, 0x9e, 0x59, 0x04, 0x0e, 0x73, 0x47}};
constexpr GUID LID_ACTION = {0x5ca83367, 0x6e45, 0x459f, {0xa2, 0x7b, 0x47, 0x6b, 0x1d, 0x01, 0xc9, 0x36}};

struct AppState {
    HWND window{};
    NOTIFYICONDATAW tray{};
    HICON inactiveIcon{};
    HICON activeIcon{};
    bool active{};
    bool lidActionChanged{};
    DWORD originalAcLidAction{};
    DWORD originalDcLidAction{};
    ULONGLONG nextJiggleAt{};
};

AppState app;

HICON CreateClockIcon(COLORREF outline, COLORREF face) {
    HDC screen = GetDC(nullptr);
    HDC colorDc = CreateCompatibleDC(screen);
    HDC maskDc = CreateCompatibleDC(screen);
    HBITMAP colorBitmap = CreateCompatibleBitmap(screen, 32, 32);
    HBITMAP maskBitmap = CreateBitmap(32, 32, 1, 1, nullptr);
    ReleaseDC(nullptr, screen);

    auto oldColor = SelectObject(colorDc, colorBitmap);
    auto oldMask = SelectObject(maskDc, maskBitmap);
    RECT bounds{0, 0, 32, 32};
    FillRect(colorDc, &bounds, static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
    FillRect(maskDc, &bounds, static_cast<HBRUSH>(GetStockObject(WHITE_BRUSH)));

    HBRUSH faceBrush = CreateSolidBrush(face);
    HPEN outlinePen = CreatePen(PS_SOLID, 3, outline);
    auto oldBrush = SelectObject(colorDc, faceBrush);
    auto oldPen = SelectObject(colorDc, outlinePen);
    Ellipse(colorDc, 3, 3, 29, 29);
    MoveToEx(colorDc, 16, 16, nullptr);
    LineTo(colorDc, 16, 8);
    LineTo(colorDc, 22, 19);

    SelectObject(maskDc, GetStockObject(BLACK_BRUSH));
    SelectObject(maskDc, GetStockObject(BLACK_PEN));
    Ellipse(maskDc, 3, 3, 29, 29);

    ICONINFO iconInfo{};
    iconInfo.fIcon = TRUE;
    iconInfo.hbmColor = colorBitmap;
    iconInfo.hbmMask = maskBitmap;
    HICON icon = CreateIconIndirect(&iconInfo);

    SelectObject(colorDc, oldBrush);
    SelectObject(colorDc, oldPen);
    SelectObject(colorDc, oldColor);
    SelectObject(maskDc, oldMask);
    DeleteObject(faceBrush);
    DeleteObject(outlinePen);
    DeleteObject(colorBitmap);
    DeleteObject(maskBitmap);
    DeleteDC(colorDc);
    DeleteDC(maskDc);
    return icon;
}

void UpdateTrayVisual() {
    app.tray.hIcon = app.active ? app.activeIcon : app.inactiveIcon;
    lstrcpynW(app.tray.szTip, app.active ? L"NoTSleep: LIGADO" : L"NoTSleep: desligado", ARRAYSIZE(app.tray.szTip));
    Shell_NotifyIconW(NIM_MODIFY, &app.tray);
}

bool SetLidActions(DWORD acAction, DWORD dcAction) {
    GUID* scheme = nullptr;
    if (PowerGetActiveScheme(nullptr, &scheme) != ERROR_SUCCESS || scheme == nullptr) return false;
    const auto acResult = PowerWriteACValueIndex(nullptr, scheme, &SUB_BUTTONS, &LID_ACTION, acAction);
    const auto dcResult = PowerWriteDCValueIndex(nullptr, scheme, &SUB_BUTTONS, &LID_ACTION, dcAction);
    const auto activateResult = (acResult == ERROR_SUCCESS && dcResult == ERROR_SUCCESS)
        ? PowerSetActiveScheme(nullptr, scheme)
        : ERROR_ACCESS_DENIED;
    LocalFree(scheme);
    return acResult == ERROR_SUCCESS && dcResult == ERROR_SUCCESS && activateResult == ERROR_SUCCESS;
}

void SaveAndDisableLidActions() {
    GUID* scheme = nullptr;
    if (PowerGetActiveScheme(nullptr, &scheme) != ERROR_SUCCESS || scheme == nullptr) return;
    const auto acResult = PowerReadACValueIndex(nullptr, scheme, &SUB_BUTTONS, &LID_ACTION, &app.originalAcLidAction);
    const auto dcResult = PowerReadDCValueIndex(nullptr, scheme, &SUB_BUTTONS, &LID_ACTION, &app.originalDcLidAction);
    LocalFree(scheme);
    if (acResult == ERROR_SUCCESS && dcResult == ERROR_SUCCESS) {
        app.lidActionChanged = SetLidActions(0, 0); // 0 = Do nothing.
    }
}

void JiggleMouse() {
    INPUT inputs[2]{};
    inputs[0].type = INPUT_MOUSE;
    inputs[0].mi.dwFlags = MOUSEEVENTF_MOVE;
    inputs[0].mi.dx = 1;
    inputs[1].type = INPUT_MOUSE;
    inputs[1].mi.dwFlags = MOUSEEVENTF_MOVE;
    inputs[1].mi.dx = -1;
    SendInput(ARRAYSIZE(inputs), inputs, sizeof(INPUT));
}

void Deactivate() {
    if (!app.active) return;
    if (app.lidActionChanged) SetLidActions(app.originalAcLidAction, app.originalDcLidAction);
    app.lidActionChanged = false;
    SetThreadExecutionState(ES_CONTINUOUS);
    app.active = false;
    UpdateTrayVisual();
}

void Activate() {
    if (app.active) return;
    SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
    app.active = true;
    app.nextJiggleAt = GetTickCount64() + JIGGLE_INTERVAL_MS;
    SaveAndDisableLidActions();
    UpdateTrayVisual();
}

void RefreshState() {
    const bool capsLockIsOn = (GetKeyState(VK_CAPITAL) & 1) != 0;
    if (capsLockIsOn && !app.active) Activate();
    if (!capsLockIsOn && app.active) Deactivate();
    if (app.active && GetTickCount64() >= app.nextJiggleAt) {
        JiggleMouse();
        app.nextJiggleAt = GetTickCount64() + JIGGLE_INTERVAL_MS;
    }
}

void ShowMenu() {
    HMENU menu = CreatePopupMenu();
    AppendMenuW(menu, MF_STRING | MF_GRAYED, 1, app.active ? L"NoTSleep ligado (Caps Lock)" : L"NoTSleep desligado");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, 2, L"Sair e restaurar energia");
    POINT point{};
    GetCursorPos(&point);
    SetForegroundWindow(app.window);
    if (TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.x, point.y, 0, app.window, nullptr) == 2) {
        DestroyWindow(app.window);
    }
    DestroyMenu(menu);
}

LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    switch (message) {
        case WM_TIMER:
            RefreshState();
            return 0;
        case WM_TRAYICON:
            if (lParam == WM_RBUTTONUP || lParam == WM_CONTEXTMENU) ShowMenu();
            return 0;
        case WM_DESTROY:
            KillTimer(window, TIMER_ID);
            Deactivate();
            Shell_NotifyIconW(NIM_DELETE, &app.tray);
            PostQuitMessage(0);
            return 0;
        default:
            return DefWindowProcW(window, message, wParam, lParam);
    }
}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    constexpr wchar_t CLASS_NAME[] = L"NoTSleepTrayWindow";
    WNDCLASSW windowClass{};
    windowClass.hInstance = instance;
    windowClass.lpszClassName = CLASS_NAME;
    windowClass.lpfnWndProc = WindowProc;
    RegisterClassW(&windowClass);

    app.window = CreateWindowExW(0, CLASS_NAME, L"NoTSleep", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, instance, nullptr);
    app.inactiveIcon = CreateClockIcon(RGB(103, 113, 122), RGB(238, 242, 245));
    app.activeIcon = CreateClockIcon(RGB(21, 128, 61), RGB(232, 255, 239));
    app.tray.cbSize = sizeof(app.tray);
    app.tray.hWnd = app.window;
    app.tray.uID = 1;
    app.tray.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    app.tray.uCallbackMessage = WM_TRAYICON;
    app.tray.hIcon = app.inactiveIcon;
    lstrcpynW(app.tray.szTip, L"NoTSleep: desligado", ARRAYSIZE(app.tray.szTip));
    Shell_NotifyIconW(NIM_ADD, &app.tray);
    SetTimer(app.window, TIMER_ID, TIMER_INTERVAL_MS, nullptr);
    RefreshState();

    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    DestroyIcon(app.inactiveIcon);
    DestroyIcon(app.activeIcon);
    return 0;
}
