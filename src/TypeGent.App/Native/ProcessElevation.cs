using System;
using System.Runtime.InteropServices;

namespace TypeGent.App.Native;

/// <summary>
/// Reads the integrity level (IL) of a window's owning process (Phase 6) so TypeGent can refuse to
/// drive <c>SendInput</c> into an elevated target from a non-elevated instance. UIPI blocks
/// lower-IL → higher-IL input, so instead of silently failing we warn the user to re-run as Admin.
/// </summary>
internal static class ProcessElevation
{
    private const int SECURITY_MANDATORY_HIGH_RID = 0x3000;

    private const int TOKEN_QUERY = 0x0008;
    private const int TokenIntegrityLevel = 25; // TOKEN_INFORMATION_CLASS.TokenIntegrityLevel
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        int tokenInformationLength, out int returnLength);

    // These two return raw pointers (PUCHAR / PDWORD), not BOOL — marshalling them as bool with an
    // extra out parameter would unbalance the stack and raise AccessViolationException.
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, int subAuthorityIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int processAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>True if the process owning <paramref name="hwnd"/> runs at High IL or above.</summary>
    public static bool IsHighOrAbove(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return false;

        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
        if (process == IntPtr.Zero) return false;

        try
        {
            return IsProcessTokenHighOrAbove(process);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>True if this (TypeGent) process itself runs at High IL or above.</summary>
    public static bool IsCurrentProcessElevated()
        => IsProcessTokenHighOrAbove(GetCurrentProcess());

    private static bool IsProcessTokenHighOrAbove(IntPtr processHandle)
    {
        if (!OpenProcessToken(processHandle, TOKEN_QUERY, out var token) || token == IntPtr.Zero)
            return false;

        try
        {
            return IsTokenHighOrAbove(token);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static bool IsTokenHighOrAbove(IntPtr token)
    {
        // First call with a null buffer to learn the required size; TOKEN_MANDATORY_LABEL is variable-length.
        GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var length);
        if (length <= 0) return false;

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
                return false;

            // TOKEN_MANDATORY_LABEL begins with a SID_AND_ATTRIBUTES whose first field is the integrity SID.
            var sid = Marshal.ReadIntPtr(buffer);

            // The integrity level is the last sub-authority of that SID.
            var countPtr = GetSidSubAuthorityCount(sid);
            if (countPtr == IntPtr.Zero) return false;
            var count = Marshal.ReadByte(countPtr);
            if (count == 0) return false;

            var valuePtr = GetSidSubAuthority(sid, count - 1);
            if (valuePtr == IntPtr.Zero) return false;
            var rid = Marshal.ReadInt32(valuePtr);

            return rid >= SECURITY_MANDATORY_HIGH_RID;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
