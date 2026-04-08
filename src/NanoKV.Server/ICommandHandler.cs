using NanoKV.Core.Protocol;

public interface ICommandHandler
{
    ValueTask<byte[]> HandleAsync(ParsedCommand command);
}