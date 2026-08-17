namespace AIDungeonMaster.Tests;

public class BotConfigurationValidatorTests
{
    [Fact]
    public void Validate_ReturnsErrors_WhenSecretsAreMissing()
    {
        var configuration = new BotConfiguration();

        var issues = BotConfigurationValidator.Validate(configuration);

        Assert.Contains(issues, x => x.Field == nameof(BotConfiguration.DiscordBotToken));
        Assert.Contains(issues, x => x.Field == nameof(BotConfiguration.OpenAIApiKey));
    }

    [Fact]
    public void Validate_AllowsEnglishOrChineseLanguage()
    {
        var english = new BotConfiguration { DefaultLanguage = "English" };
        var chinese = new BotConfiguration { DefaultLanguage = "Chinese" };

        Assert.DoesNotContain(
            BotConfigurationValidator.Validate(english),
            x => x.Field == nameof(BotConfiguration.DefaultLanguage));

        Assert.DoesNotContain(
            BotConfigurationValidator.Validate(chinese),
            x => x.Field == nameof(BotConfiguration.DefaultLanguage));
    }

    [Fact]
    public void Validate_RejectsInvalidLanguage()
    {
        var configuration = new BotConfiguration
        {
            DefaultLanguage = "French"
        };

        var issues = BotConfigurationValidator.Validate(configuration);

        Assert.Contains(
            issues,
            x => x.Field == nameof(BotConfiguration.DefaultLanguage));
    }
}
