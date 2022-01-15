using System.Linq;

class Program
{
    public static Commands Commands = new Commands();
    public static string[] Args { get; private set; }

    public delegate void CliExitHandler();
    public static event CliExitHandler CliExitEvent;

    static void Main(string[] args)
    {
        if (args.Length == 0) args = new[] { "help" };
        Args = args;
        Debug.Print(Commands.Run(args[0], args.Skip(1).ToArray()));
        CliExitEvent?.Invoke();
    }
}
