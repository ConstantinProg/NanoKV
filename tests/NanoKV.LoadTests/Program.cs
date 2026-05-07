using NanoKV.Core.Models;
using NanoKV.LoadTests;
using NBomber.CSharp;

var scenario = Scenario.Create("nanokv_set_user_profile_load", async context =>
{
    int invocationNumber = checked((int)context.InvocationNumber);

    string key = $"user:{invocationNumber}";

    var profile = new UserProfile
    {
        Id = invocationNumber,
        Username = $"user-{invocationNumber}",
        CreatedAt = DateTime.UtcNow
    };

    try
    {
        await using var client = new NanoKvClient("127.0.0.1", 8080);

        await client.ConnectAsync();
        await client.SetAsync(key, profile);

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