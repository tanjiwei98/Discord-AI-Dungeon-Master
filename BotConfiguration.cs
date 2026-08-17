public sealed class BotConfiguration
{
    public string DiscordBotToken { get; set; } = string.Empty;

    public string OpenAIApiKey { get; set; } = string.Empty;

    public string Language { get; set; } = "English";

    public string OpenAIModel { get; set; } = string.Empty;

    public string DefaultLanguage { get; set; } = string.Empty;

    public string ChoiceTimeoutMinutes { get; set; } = string.Empty;

    public string OpenAITTSModel { get; set; } = string.Empty;

    public string OpenAITTSVoiceZh { get; set; } = string.Empty;

    public string OpenAITTSVoiceEn { get; set; } = string.Empty;

    public static BotConfiguration FromEnvironment()
    {
        return new BotConfiguration
        {
            DiscordBotToken =
                Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN")
                ?? string.Empty,
            OpenAIApiKey =
                Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? string.Empty,
            OpenAIModel =
                Environment.GetEnvironmentVariable("OPENAI_MODEL")
                ?? string.Empty,
            DefaultLanguage =
                Environment.GetEnvironmentVariable("DEFAULT_LANGUAGE")
                ?? string.Empty,
            ChoiceTimeoutMinutes =
                Environment.GetEnvironmentVariable("CHOICE_TIMEOUT_MINUTES")
                ?? string.Empty,
            OpenAITTSModel =
                Environment.GetEnvironmentVariable("OPENAI_TTS_MODEL")
                ?? string.Empty,
            OpenAITTSVoiceZh =
                Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE_ZH")
                ?? string.Empty,
            OpenAITTSVoiceEn =
                Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE_EN")
                ?? string.Empty
        };
    }
}

public sealed record BotValidationIssue(
    string Field,
    string Message);

public sealed record ConnectionStatus(
    string Service,
    bool IsConnected,
    string Message);

public sealed record BotTestResult(
    IReadOnlyList<ConnectionStatus> Statuses,
    IReadOnlyList<BotValidationIssue> Issues);

public interface IConfigurationService
{
    Task<BotConfiguration> LoadAsync();

    Task SaveAsync(BotConfiguration configuration);
}

public interface IDiscordConnectionService
{
    Task<ConnectionStatus> TestAsync(
        BotConfiguration configuration,
        CancellationToken cancellationToken);
}

public interface IOpenAIService
{
    Task<ConnectionStatus> TestAsync(
        BotConfiguration configuration,
        CancellationToken cancellationToken);
}

public interface IBotService
{
    Task<BotTestResult> TestConfigurationAsync(
        BotConfiguration configuration,
        CancellationToken cancellationToken);

    Task StartAsync(
        BotConfiguration configuration,
        IProgress<string> progress,
        CancellationToken cancellationToken);
}
