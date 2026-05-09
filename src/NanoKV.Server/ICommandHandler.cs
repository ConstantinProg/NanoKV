using NanoKV.Core.Protocol;

public interface ICommandHandler
{
    byte[] Handle(ParsedCommand command);
}