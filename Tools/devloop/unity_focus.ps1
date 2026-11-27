# Brings the Unity Editor window to the front (restoring it if minimised). An unfocused Editor stops ticking:
# EditorApplication.delayCall/update, live baking and closed-tile streaming all wait until it has focus again.
#   powershell -NoProfile -File Tools/devloop/unity_focus.ps1
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UnityWindowFocus
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr windowHandle);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr windowHandle, int showCommand);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr windowHandle);
    [DllImport("user32.dll")] public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
"@
$unityProcess = Get-Process -Name Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $unityProcess) { "NO UNITY"; exit 2 }
$windowHandle = $unityProcess.MainWindowHandle
if ([UnityWindowFocus]::IsIconic($windowHandle)) { [void][UnityWindowFocus]::ShowWindow($windowHandle, 9) }
# Windows refuses SetForegroundWindow from a background process unless a key event came first; a lone Alt tap satisfies it.
[UnityWindowFocus]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
[UnityWindowFocus]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
if ([UnityWindowFocus]::SetForegroundWindow($windowHandle)) { "FOCUSED" } else { "FOCUS REFUSED" }
