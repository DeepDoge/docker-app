using System;
using Newtonsoft.Json;
class Debug
{
    public static void Print(string str) => Console.WriteLine(str);
    public static void Print(Exception ex) => Console.WriteLine(ex.Message);
    public static void Print(object any)
    {
        if (any == null) return;
        Console.WriteLine(any.GetType() == typeof(string) ? any.ToString() : JsonConvert.SerializeObject(any, Formatting.Indented));
    }
}