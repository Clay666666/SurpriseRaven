using System.Text.Json;
using SurpriseRaven.Hooks.Raven;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: CheckGameplayOdds <gameplay-odds.json>");
    return 2;
}

try
{
    var odds = RavenGameplayOddsConfigHooks.Parse(File.ReadAllText(args[0]));
    Console.WriteLine($"Gameplay odds verified: lightUpPercent={odds.LightUpPercent}.");
    return 0;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
{
    Console.Error.WriteLine($"Invalid gameplay odds in {args[0]}: {error.Message}");
    return 1;
}
