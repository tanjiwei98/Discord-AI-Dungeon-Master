public static class Program
{
    public static async Task Main(string[] args)
    {
        var configuration =
            BotConfiguration.FromEnvironment();

        await DungeonMasterBot.RunAsync(configuration);
    }
}
