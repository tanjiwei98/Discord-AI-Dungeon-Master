using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIDungeonMaster.Desktop;

public sealed class SecureConfigurationService : IConfigurationService
{
    private readonly string _filePath;
    private readonly ISecretProtector _protector;

    public SecureConfigurationService()
        : this(new DpapiSecretProtector())
    {
    }

    public SecureConfigurationService(
        ISecretProtector protector,
        string? filePath = null)
    {
        _protector = protector;

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _filePath = filePath;
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return;
        }

        var folder =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "AIDungeonMaster");

        Directory.CreateDirectory(folder);

        _filePath = Path.Combine(folder, "settings.dat");
    }

    public Task<BotConfiguration> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return Task.FromResult(BotConfiguration.FromEnvironment());
        }

        var protectedBytes = File.ReadAllBytes(_filePath);
        var json = _protector.Unprotect(protectedBytes);

        var configuration =
            JsonSerializer.Deserialize<BotConfiguration>(json)
            ?? BotConfiguration.FromEnvironment();

        return Task.FromResult(configuration);
    }

    public Task SaveAsync(BotConfiguration configuration)
    {
        var json =
            JsonSerializer.Serialize(
                configuration,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        var protectedBytes =
            _protector.Protect(json);

        File.WriteAllBytes(
            _filePath,
            protectedBytes);

        return Task.CompletedTask;
    }
}

public interface ISecretProtector
{
    byte[] Protect(string value);

    string Unprotect(byte[] value);
}

public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("AI Dungeon Master");

    public byte[] Protect(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return ProtectedData.Protect(
            bytes,
            Entropy,
            DataProtectionScope.CurrentUser);
    }

    public string Unprotect(byte[] value)
    {
        var bytes = ProtectedData.Unprotect(
            value,
            Entropy,
            DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(bytes);
    }
}

public sealed class DesktopBotService : IBotService
{
    private readonly IDiscordConnectionService _discordConnectionService;
    private readonly IOpenAIService _openAIService;

    public DesktopBotService(
        IDiscordConnectionService discordConnectionService,
        IOpenAIService openAIService)
    {
        _discordConnectionService = discordConnectionService;
        _openAIService = openAIService;
    }

    public async Task<BotTestResult> TestConfigurationAsync(
        BotConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var issues =
            BotConfigurationValidator.Validate(configuration).ToList();

        if (issues.Count > 0)
        {
            return new BotTestResult(
                Array.Empty<ConnectionStatus>(),
                issues);
        }

        var discordTask =
            _discordConnectionService.TestAsync(
                configuration,
                cancellationToken);

        var openAITask =
            _openAIService.TestAsync(
                configuration,
                cancellationToken);

        await Task.WhenAll(discordTask, openAITask);

        return new BotTestResult(
            new[] { discordTask.Result, openAITask.Result },
            Array.Empty<BotValidationIssue>());
    }

    public async Task StartAsync(
        BotConfiguration configuration,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        progress.Report("Launching bot...");

        var originalOut = Console.Out;
        var originalError = Console.Error;
        var writer = new ProgressTextWriter(progress);

        try
        {
            Console.SetOut(writer);
            Console.SetError(writer);

            await DungeonMasterBot.RunAsync(
                configuration,
                cancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}

public sealed class DiscordConnectionService : IDiscordConnectionService
{
    public async Task<ConnectionStatus> TestAsync(
        BotConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            var client =
                new Discord.WebSocket.DiscordSocketClient(
                    new Discord.WebSocket.DiscordSocketConfig
                    {
                        GatewayIntents = Discord.GatewayIntents.None
                    });

            await client.LoginAsync(
                Discord.TokenType.Bot,
                configuration.DiscordBotToken);

            await client.LogoutAsync();
            client.Dispose();

            return new ConnectionStatus(
                "Discord",
                true,
                "Discord token looks valid.");
        }
        catch (Exception ex)
        {
            return new ConnectionStatus(
                "Discord",
                false,
                $"Discord test failed: {ex.Message}");
        }
    }
}

public sealed class OpenAIConnectionService : IOpenAIService
{
    public Task<ConnectionStatus> TestAsync(
        BotConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                try
                {
                    var model =
                        string.IsNullOrWhiteSpace(configuration.OpenAIModel)
                            ? "gpt-5.4-mini"
                            : configuration.OpenAIModel;

                    var client =
                        new OpenAI.Chat.ChatClient(
                            model,
                            configuration.OpenAIApiKey);

                    var result =
                        client.CompleteChat(
                            "Reply with the single word OK.");

                    var text =
                        result.Value.Content[0].Text.Trim();

                    return new ConnectionStatus(
                        "OpenAI",
                        true,
                        $"OpenAI responded: {text}");
                }
                catch (Exception ex)
                {
                    return new ConnectionStatus(
                        "OpenAI",
                        false,
                        $"OpenAI test failed: {ex.Message}");
                }
            },
            cancellationToken);
    }
}

internal sealed class ProgressTextWriter : TextWriter
{
    private readonly IProgress<string> _progress;
    private readonly StringBuilder _buffer = new();

    public ProgressTextWriter(IProgress<string> progress)
    {
        _progress = progress;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        if (value == '\r')
        {
            return;
        }

        if (value == '\n')
        {
            FlushBuffer();
            return;
        }

        _buffer.Append(value);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (var ch in value)
        {
            Write(ch);
        }
    }

    private void FlushBuffer()
    {
        if (_buffer.Length == 0)
        {
            return;
        }

        _progress.Report(_buffer.ToString());
        _buffer.Clear();
    }
}
