using Wombat.Network.Mqtt.Broker;

namespace Wombat.Network.MqttTest;

internal static class Program
{
    public static async Task Main(string[] args)
    {
    //    var broker = new MqttBroker(
    //new MqttBrokerOptions()
    //    .ListenTcp(1883));

    //    await broker.StartAsync();
    //    Console.WriteLine("MQTT broker started on tcp://0.0.0.0:1883");
    //    Console.ReadLine();


        var scenarioName = args.Length == 0 ? "all" : args[0];
        if (scenarioName.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            PrintScenarioList();
            return;
        }

        var selected = scenarioName.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? Scenarios.All
            : Scenarios.All.Where(x => x.Name.Equals(scenarioName, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (selected.Length == 0)
        {
            Console.Error.WriteLine($"Unknown scenario: {scenarioName}");
            PrintScenarioList();
            Environment.ExitCode = 1;
            return;
        }

        foreach (var scenario in selected)
        {
            await RunScenarioAsync(scenario);
        }
    }

    private static async Task RunScenarioAsync(Scenario scenario)
    {
        Console.WriteLine($"[RUN ] {scenario.Name} - {scenario.Description}");
        var startedAt = DateTime.UtcNow;

        try
        {
            await scenario.RunAsync();
            Console.WriteLine($"[PASS] {scenario.Name} ({(DateTime.UtcNow - startedAt).TotalSeconds:F2}s)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {scenario.Name}: {ex.GetType().Name} - {ex.Message}");
            throw;
        }
    }

    private static void PrintScenarioList()
    {
        Console.WriteLine("Usage: dotnet run --project Tests/Wombat.Network.MqttTest/Wombat.Network.MqttTest.csproj -- [all|list|ScenarioName]");
        foreach (var scenario in Scenarios.All)
        {
            Console.WriteLine($"  {scenario.Name} - {scenario.Description}");
        }
    }
}
