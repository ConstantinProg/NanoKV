using NanoKV.LoadTests;
using NBomber.Contracts;
using NBomber.CSharp;
using System.Collections.Concurrent;
using System.Text;

const string Host = "127.0.0.1";
const int Port = 8080;

const int SetOnlyClients = 32;
const int GetOnlyClients = 32;
const int MixedClients = 32;

const int PreloadedKeys = 10_000;
const int TestDurationSeconds = 30;
const int WarmupSeconds = 10;

ReadOnlyMemory<byte> Payload =
    Encoding.UTF8.GetBytes(
        """{"id":42,"username":"constantin","createdAt":"2026-06-01T00:00:00Z"}""");

var setOnlyClients = new ConcurrentDictionary<int, NanoKvClient>();
var getOnlyClients = new ConcurrentDictionary<int, NanoKvClient>();
var mixedClients = new ConcurrentDictionary<int, NanoKvClient>();

await PreloadDataAsync();

var setOnlyScenario = Scenario.Create("set_only", async context =>
{
    NanoKvClient client = GetClient(
        setOnlyClients,
        context.ScenarioInfo.InstanceNumber);

    string key =
        $"set-only:{context.ScenarioInfo.InstanceNumber}:{context.InvocationNumber}";

    try
    {
        await client.SetAsync(key, Payload, CancellationToken.None);
        return Response.Ok();
    }
    catch (Exception ex)
    {
        return Response.Fail(message: ex.Message);
    }
})
.WithInit(async _ =>
{
    await InitializeClientsAsync(setOnlyClients, SetOnlyClients);
})
.WithClean(async _ =>
{
    await DisposeClientsAsync(setOnlyClients);
})
.WithWarmUpDuration(TimeSpan.FromSeconds(WarmupSeconds))
.WithLoadSimulations(
    Simulation.KeepConstant(
        copies: SetOnlyClients,
        during: TimeSpan.FromSeconds(TestDurationSeconds)));

var getOnlyScenario = Scenario.Create("get_after_preloaded_data", async context =>
{
    NanoKvClient client = GetClient(
        getOnlyClients,
        context.ScenarioInfo.InstanceNumber);

    int keyIndex = Random.Shared.Next(PreloadedKeys);
    string key = $"get-preloaded:{keyIndex}";

    try
    {
        string? value = await client.GetAsync(key, CancellationToken.None);

        return value is not null
            ? Response.Ok()
            : Response.Fail(message: $"Missing preloaded key: {key}");
    }
    catch (Exception ex)
    {
        return Response.Fail(message: ex.Message);
    }
})
.WithInit(async _ =>
{
    await InitializeClientsAsync(getOnlyClients, GetOnlyClients);
})
.WithClean(async _ =>
{
    await DisposeClientsAsync(getOnlyClients);
})
.WithWarmUpDuration(TimeSpan.FromSeconds(WarmupSeconds))
.WithLoadSimulations(
    Simulation.KeepConstant(
        copies: GetOnlyClients,
        during: TimeSpan.FromSeconds(TestDurationSeconds)));

var mixedScenario = Scenario.Create("mixed_70_get_20_set_10_delete", async context =>
{
    NanoKvClient client = GetClient(
        mixedClients,
        context.ScenarioInfo.InstanceNumber);

    int operation = Random.Shared.Next(100);

    try
    {
        if (operation < 70)
        {
            int keyIndex = Random.Shared.Next(PreloadedKeys);
            string key = $"mixed-get-preloaded:{keyIndex}";

            string? value = await client.GetAsync(key, CancellationToken.None);

            return value is not null
                ? Response.Ok()
                : Response.Fail(message: $"Missing mixed GET key: {key}");
        }

        if (operation < 90)
        {
            string key =
                $"mixed-set:{context.ScenarioInfo.InstanceNumber}:{context.InvocationNumber}";

            await client.SetAsync(key, Payload, CancellationToken.None);

            return Response.Ok();
        }

        {
            int keyIndex = Random.Shared.Next(PreloadedKeys);
            string key = $"mixed-delete:{keyIndex}";

            await client.DeleteAsync(key, CancellationToken.None);

            return Response.Ok();
        }
    }
    catch (Exception ex)
    {
        return Response.Fail(message: ex.Message);
    }
})
.WithInit(async _ =>
{
    await InitializeClientsAsync(mixedClients, MixedClients);
})
.WithClean(async _ =>
{
    await DisposeClientsAsync(mixedClients);
})
.WithWarmUpDuration(TimeSpan.FromSeconds(WarmupSeconds))
.WithLoadSimulations(
    Simulation.KeepConstant(
        copies: MixedClients,
        during: TimeSpan.FromSeconds(TestDurationSeconds)));

NBomberRunner
    .RegisterScenarios(
        SelectScenarios(
            args,
            setOnlyScenario,
            getOnlyScenario,
            mixedScenario))
    .Run();

static ScenarioProps[] SelectScenarios(
    string[] args,
    ScenarioProps setOnlyScenario,
    ScenarioProps getOnlyScenario,
    ScenarioProps mixedScenario)
{
    if (args.Length == 0)
    {
        return
        [
            setOnlyScenario,
            getOnlyScenario,
            mixedScenario
        ];
    }

    string scenarioName = args[0];

    return scenarioName switch
    {
        "set_only" =>
        [
            setOnlyScenario
        ],

        "get_after_preloaded_data" =>
        [
            getOnlyScenario
        ],

        "mixed_70_get_20_set_10_delete" =>
        [
            mixedScenario
        ],

        _ => throw new ArgumentException(
            $"Unknown scenario '{scenarioName}'. " +
            "Allowed values: set_only, get_after_preloaded_data, mixed_70_get_20_set_10_delete.")
    };
}

static NanoKvClient GetClient(
    ConcurrentDictionary<int, NanoKvClient> clients,
    int instanceNumber)
{
    if (clients.TryGetValue(instanceNumber, out NanoKvClient? client))
        return client;

    throw new InvalidOperationException(
        $"Client for scenario instance {instanceNumber} was not initialized.");
}

static async Task InitializeClientsAsync(
    ConcurrentDictionary<int, NanoKvClient> clients,
    int count)
{
    for (int i = 0; i < count; i++)
    {
        var client = new NanoKvClient(Host, Port);

        await client.ConnectAsync(CancellationToken.None);

        clients[i] = client;
    }
}

static async Task DisposeClientsAsync(
    ConcurrentDictionary<int, NanoKvClient> clients)
{
    foreach (NanoKvClient client in clients.Values)
    {
        await client.DisposeAsync();
    }

    clients.Clear();
}

async Task PreloadDataAsync()
{
    await using var client = new NanoKvClient(Host, Port);

    await client.ConnectAsync(CancellationToken.None);

    for (int i = 0; i < PreloadedKeys; i++)
    {
        await client.SetAsync(
            key: $"get-preloaded:{i}",
            value: Payload,
            cancellationToken: CancellationToken.None);

        await client.SetAsync(
            key: $"mixed-get-preloaded:{i}",
            value: Payload,
            cancellationToken: CancellationToken.None);

        await client.SetAsync(
            key: $"mixed-delete:{i}",
            value: Payload,
            cancellationToken: CancellationToken.None);
    }
}