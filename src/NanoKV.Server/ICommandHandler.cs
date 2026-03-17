using NanoKv.Core.Protocol;

public interface ICommandHandler
{
    void Handle(ParsedCommand command);
}