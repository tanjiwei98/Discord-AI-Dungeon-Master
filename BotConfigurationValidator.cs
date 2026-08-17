public static class BotConfigurationValidator
{
    public static IReadOnlyList<BotValidationIssue> Validate(
        BotConfiguration configuration)
    {
        var issues = new List<BotValidationIssue>();

        if (string.IsNullOrWhiteSpace(configuration.DiscordBotToken))
        {
            issues.Add(new BotValidationIssue(
                nameof(configuration.DiscordBotToken),
                "Discord Bot Token is required."));
        }

        if (string.IsNullOrWhiteSpace(configuration.OpenAIApiKey))
        {
            issues.Add(new BotValidationIssue(
                nameof(configuration.OpenAIApiKey),
                "OpenAI API Key is required."));
        }

        if (!string.IsNullOrWhiteSpace(configuration.OpenAIModel) &&
            configuration.OpenAIModel.Length > 128)
        {
            issues.Add(new BotValidationIssue(
                nameof(configuration.OpenAIModel),
                "OpenAI model name is too long."));
        }

        if (!string.IsNullOrWhiteSpace(configuration.DefaultLanguage) &&
            !string.Equals(configuration.DefaultLanguage, "English", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(configuration.DefaultLanguage, "Chinese", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new BotValidationIssue(
                nameof(configuration.DefaultLanguage),
                "Language must be English or Chinese."));
        }

        if (!string.IsNullOrWhiteSpace(configuration.ChoiceTimeoutMinutes) &&
            !int.TryParse(configuration.ChoiceTimeoutMinutes, out var timeoutMinutes))
        {
            issues.Add(new BotValidationIssue(
                nameof(configuration.ChoiceTimeoutMinutes),
                "Choice timeout must be a whole number of minutes."));
        }
        else if (int.TryParse(configuration.ChoiceTimeoutMinutes, out timeoutMinutes) &&
                 timeoutMinutes < 1)
        {
            issues.Add(new BotValidationIssue(
                nameof(configuration.ChoiceTimeoutMinutes),
                "Choice timeout must be at least 1 minute."));
        }

        return issues;
    }
}
