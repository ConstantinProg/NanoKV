using NanoKV.Core.Storage;
using NanoKV.Server;

var store = new SimpleStore();
var handler = new StoreCommandHandler(store);

var server = new TcpServer("127.0.0.1", 8080, handler);
var cts = new CancellationTokenSource();

var serverTask = server.StartAsync(cts.Token);

Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("Server started. Press Ctrl+C to stop.");

await serverTask;