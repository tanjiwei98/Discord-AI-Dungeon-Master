using AIDungeonMaster.Desktop;
using System.Text;

namespace AIDungeonMaster.Tests;

public class SecureConfigurationServiceTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsConfiguration()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(tempDir, "settings.dat");
        var protector = new FakeProtector();
        var service = new SecureConfigurationService(protector, filePath);

        try
        {
            var configuration = new BotConfiguration
            {
                DiscordBotToken = "discord-token",
                OpenAIApiKey = "openai-key",
                DefaultLanguage = "Chinese",
                OpenAIModel = "gpt-5.4-mini"
            };

            await service.SaveAsync(configuration);
            var loaded = await service.LoadAsync();

            Assert.Equal(configuration.DiscordBotToken, loaded.DiscordBotToken);
            Assert.Equal(configuration.OpenAIApiKey, loaded.OpenAIApiKey);
            Assert.Equal(configuration.DefaultLanguage, loaded.DefaultLanguage);
            Assert.Equal(configuration.OpenAIModel, loaded.OpenAIModel);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private sealed class FakeProtector : ISecretProtector
    {
        public byte[] Protect(string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        public string Unprotect(byte[] value)
        {
            return Encoding.UTF8.GetString(value);
        }
    }
}
