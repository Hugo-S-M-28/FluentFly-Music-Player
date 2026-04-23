using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        try
        {
            var asm = Assembly.LoadFile(@"C:\Users\hsm76\.nuget\packages\dubya.windowsmediacontroller\2.5.6\lib\net10.0-windows10.0.22000\WindowsMediaController.dll");
            var type = asm.GetType("WindowsMediaController.MediaManager");
            foreach (var method in type.GetMethods())
            {
                Console.WriteLine(method.Name);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
