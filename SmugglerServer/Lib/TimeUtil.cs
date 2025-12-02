using System.Runtime.InteropServices;

namespace SmugglerServer.Lib;

public static class TimeUtil
{
    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern void GetSystemTimeAsFileTime(out long fileTime);

    public static long GetSystemTimeInMilliseconds()
    {
        // FILETIME 구조를 그대로 long으로 받으면 64비트 FILETIME 값이 들어온다.
        GetSystemTimeAsFileTime(out long fileTime);

        // C++: uli.QuadPart / 10000  (100ns → ms)
        return fileTime / 10_000;
    }
}