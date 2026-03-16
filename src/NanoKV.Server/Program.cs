using NanoKV.Server;

var server = new TcpServer("127.0.0.1", 8080);

_ = server.StartAsync(CancellationToken.None);

Console.WriteLine("Press ENTER to stop server...");
Console.ReadLine();