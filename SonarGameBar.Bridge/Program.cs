using System.Globalization;
using System.Text.Json;
using SonarGameBar.Bridge;
using SonarGameBar.Core;

return await BridgeProgram.RunAsync(args);

internal static class BridgeProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        BridgeLog.Write($"Bridge started with arguments: {string.Join(" ", args)}.");

        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            if (args.Any(argument =>
                    argument.Equals("appservice", StringComparison.OrdinalIgnoreCase)))
            {
                using var host = new AppServiceBridgeHost();
                return await host.RunAsync();
            }

            using var sonar = new SonarClient();

            switch (args[0].ToLowerInvariant())
            {
                case "status":
                    Console.WriteLine(JsonSerializer.Serialize(await sonar.GetStateAsync(), JsonOptions));
                    break;

                case "set-volume" when args.Length == 3:
                    await sonar.SetVolumeAsync(args[1], ParseDouble(args[2], "volume"));
                    break;

                case "set-mute" when args.Length == 3:
                    await sonar.SetMuteAsync(args[1], bool.Parse(args[2]));
                    break;

                case "set-chatmix" when args.Length == 2:
                    await sonar.SetChatMixAsync(ParseDouble(args[1], "balance"));
                    break;

                default:
                    Console.Error.WriteLine("Invalid command or arguments.");
                    PrintHelp();
                    return 2;
            }

            return 0;
        }
        catch (Exception exception)
        {
            BridgeLog.Write($"Bridge failed: {exception}");
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static double ParseDouble(string value, string name)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            throw new ArgumentException($"Invalid {name} value '{value}'.");
        }

        return result;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            SonarGameBar Bridge diagnostics

              status
              appservice
              set-volume <master|game|chatRender> <0..1>
              set-mute <master|game|chatRender> <true|false>
              set-chatmix <-1..1>
            """);
    }
}
