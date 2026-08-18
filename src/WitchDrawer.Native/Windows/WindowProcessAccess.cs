using System.Runtime.InteropServices;

namespace WitchDrawer.Native.Windows;

public static class WindowProcessAccess
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    public static bool CanInteractWith(uint processId)
    {
        if (processId == 0)
        {
            return false;
        }

        if (processId == (uint)Environment.ProcessId)
        {
            return true;
        }

        return TryGetIntegrityLevel((uint)Environment.ProcessId, out var currentLevel)
            && TryGetIntegrityLevel(processId, out var targetLevel)
            && targetLevel <= currentLevel;
    }

    private static bool TryGetIntegrityLevel(uint processId, out uint level)
    {
        level = 0;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == nint.Zero)
        {
            return false;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return false;
            }

            try
            {
                _ = GetTokenInformation(token, TokenIntegrityLevel, nint.Zero, 0, out var size);
                if (size <= 0)
                {
                    return false;
                }

                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
                    {
                        return false;
                    }

                    var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
                    var countPointer = GetSidSubAuthorityCount(label.Label.Sid);
                    if (countPointer == nint.Zero)
                    {
                        return false;
                    }

                    var count = Marshal.ReadByte(countPointer);
                    if (count == 0)
                    {
                        return false;
                    }

                    var levelPointer = GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
                    if (levelPointer == nint.Zero)
                    {
                        return false;
                    }

                    level = unchecked((uint)Marshal.ReadInt32(levelPointer));
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                _ = CloseHandle(token);
            }
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public nint Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public SidAndAttributes Label;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint desiredAccess, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        nint token,
        int informationClass,
        nint information,
        int informationLength,
        out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint subAuthority);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
