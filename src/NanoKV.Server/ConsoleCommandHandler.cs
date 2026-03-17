using NanoKv.Core.Protocol;
using System.Text;

public class ConsoleCommandHandler : ICommandHandler
{
    public void Handle(ParsedCommand cmd)
    {
        var command = Encoding.ASCII.GetString(cmd.Command);
        var key = Encoding.ASCII.GetString(cmd.Key);
        var value = Encoding.ASCII.GetString(cmd.Value);

        Console.WriteLine($"Command: {command}, Key: {key}, Value: {value}");
    }
}