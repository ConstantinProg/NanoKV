using NanoKV.Server;

var server = new TcpServer("127.0.0.1", 8080, new ConsoleCommandHandler());

var cts = new CancellationTokenSource();

var serverTask = server.StartAsync(cts.Token);

Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("Server started. Press Ctrl+C to stop.");

await serverTask;