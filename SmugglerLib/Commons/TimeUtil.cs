using System.Runtime.InteropServices;

namespace SmugglerLib.Commons;

public static class TimeUtil
{
    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern void GetSystemTimeAsFileTime(out long fileTime);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    public static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    public static extern uint TimeEndPeriod(uint uMilliseconds);

    public static long GetSystemTimeInMilliseconds()
    {
        // FILETIME 구조를 그대로 long으로 받으면 64비트 FILETIME 값이 들어온다.
        GetSystemTimeAsFileTime(out long fileTime);

        // C++: uli.QuadPart / 10000  (100ns → ms)
        return fileTime / 10_000;
    }
}