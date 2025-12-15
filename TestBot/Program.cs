using ENet;

namespace TestBot;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Hello, World! - Client Bot");

        Library.Initialize();

        Library.Deinitialize();
    }
}