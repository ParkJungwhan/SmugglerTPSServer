namespace SmugglerServer.Lib
{
    public enum MsgLevel
    {
        Debug = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    }

    public static class Log
    {
        public static void PrintLog(string msg, MsgLevel msglv = MsgLevel.Debug)
        {
            if (msglv >= MsgLevel.Information)
            {
                Console.ForegroundColor = msglv switch
                {
                    MsgLevel.Information => ConsoleColor.Green,
                    MsgLevel.Warning => ConsoleColor.Yellow,
                    MsgLevel.Error => ConsoleColor.Red,
                    _ => ConsoleColor.White,
                };
            }
            Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}\t {msg}");
            Console.ResetColor();
        }
    }
}