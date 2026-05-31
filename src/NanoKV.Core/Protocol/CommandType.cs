namespace NanoKV.Core.Protocol;

public enum CommandType
{
    Unknown = 0,
    Set = 1,
    Get = 2,
    Delete = 3,
    Stats = 4
}