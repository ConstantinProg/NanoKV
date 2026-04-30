using System.Text;
using NBomber.CSharp;
using NanoKV.LoadTests;

var scenario = Scenario.Create("nanokv_set_load", async context =>
{
    var key = $"key-{context.InvocationNumber}";
    var value = Encoding.ASCII.GetBytes($"value-{Random.Shared.Next()}");

    try
    {
        await using var client = new NanoKvClient("127.0.0.1", 8080);

        await client.ConnectAsync();
        await client.SetAsync(key, value);

        return Response.Ok();
    }
    catch (Exception ex)
    {
        return Response.Fail(message: ex.Message);
    }
})
.WithWarmUpDuration(TimeSpan.FromSeconds(10))
.WithLoadSimulations(
    Simulation.Inject(
        rate: 100,
        interval: TimeSpan.FromSeconds(1),
        during: TimeSpan.FromSeconds(30)
    )
);

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();