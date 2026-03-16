using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NanoKv.Core.Protocol;

namespace NanoKV.Server;

public sealed class TcpServer
{
    private readonly IPEndPoint _endpoint;

    public TcpServer(string ip, int port)
    {
        _endpoint = new IPEndPoint(IPAddress.Parse(ip), port);
    }

    public async Task StartAsync(CancellationToken token)
    {
        var server = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);

        server.Bind(_endpoint);
        server.Listen(100);

        while (!token.IsCancellationRequested)
        {
            var client = await server.AcceptAsync(token);

            _ = ProcessClientAsync(client, token);
        }
    }

    private async Task ProcessClientAsync(Socket client, CancellationToken token)
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(4096);

        try
        {
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await client.ReceiveAsync(buffer, token);

                if (bytesRead == 0)
                    break;

                var span = new ReadOnlySpan<byte>(buffer, 0, bytesRead);
                var command = CommandParser.Parse(span);

                if (command.IsEmpty)
                {
                    Console.WriteLine("Invalid command");
                    continue;
                }

                PrintCommand(command);
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"Socket error: {ex.Message}");
        }
        finally
        {
            pool.Return(buffer);

            try
            {
                client.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            client.Close();
            client.Dispose();
        }
    }

    private static void PrintCommand(ParsedCommand cmd)
    {
        string command = Encoding.ASCII.GetString(cmd.Command);
        string key = Encoding.ASCII.GetString(cmd.Key);
        string value = Encoding.ASCII.GetString(cmd.Value);

        Console.WriteLine($"Command: {command}");

        if (!string.IsNullOrEmpty(key))
            Console.WriteLine($"Key: {key}");

        if (!string.IsNullOrEmpty(value))
            Console.WriteLine($"Value: {value}");
    }
}
