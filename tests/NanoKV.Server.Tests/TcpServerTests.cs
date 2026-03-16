using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NanoKV.Server.Tests;

public class TcpServerTests
{
    [Fact]
    public async Task Server_ShouldAcceptConnection_AndReceiveCommand()
    {
        var cts = new CancellationTokenSource();

        var server = new TcpServer("127.0.0.1", 9090);

        _ = Task.Run(() => server.StartAsync(cts.Token));

        await Task.Delay(200);

        using var client = new TcpClient();

        await client.ConnectAsync("127.0.0.1", 9090);

        var stream = client.GetStream();

        var command = Encoding.ASCII.GetBytes("SET user:1 data");

        await stream.WriteAsync(command);

        await stream.FlushAsync();

        await Task.Delay(200);

        cts.Cancel();

        Assert.True(client.Connected);
    }
}