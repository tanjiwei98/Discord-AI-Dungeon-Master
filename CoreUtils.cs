using Discord;
using Discord.WebSocket;
using OpenAI.Chat;
using System.Text.Json;

public partial class DungeonMasterBot
{
    private static void ApplyConfiguration(
        BotConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.DiscordBotToken))
        {
            Environment.SetEnvironmentVariable(
                "DISCORD_BOT_TOKEN",
                configuration.DiscordBotToken);
        }

        if (!string.IsNullOrWhiteSpace(configuration.OpenAIApiKey))
        {
            Environment.SetEnvironmentVariable(
                "OPENAI_API_KEY",
                configuration.OpenAIApiKey);
        }

        if (!string.IsNullOrWhiteSpace(configuration.OpenAIModel))
        {
            Environment.SetEnvironmentVariable(
                "OPENAI_MODEL",
                configuration.OpenAIModel);
        }

        if (!string.IsNullOrWhiteSpace(configuration.DefaultLanguage))
        {
            Environment.SetEnvironmentVariable(
                "DEFAULT_LANGUAGE",
                configuration.DefaultLanguage);
        }

        if (!string.IsNullOrWhiteSpace(configuration.ChoiceTimeoutMinutes))
        {
            Environment.SetEnvironmentVariable(
                "CHOICE_TIMEOUT_MINUTES",
                configuration.ChoiceTimeoutMinutes);
        }

        if (!string.IsNullOrWhiteSpace(configuration.OpenAITTSModel))
        {
            Environment.SetEnvironmentVariable(
                "OPENAI_TTS_MODEL",
                configuration.OpenAITTSModel);
        }

        if (!string.IsNullOrWhiteSpace(configuration.OpenAITTSVoiceZh))
        {
            Environment.SetEnvironmentVariable(
                "OPENAI_TTS_VOICE_ZH",
                configuration.OpenAITTSVoiceZh);
        }

        if (!string.IsNullOrWhiteSpace(configuration.OpenAITTSVoiceEn))
        {
            Environment.SetEnvironmentVariable(
                "OPENAI_TTS_VOICE_EN",
                configuration.OpenAITTSVoiceEn);
        }
    }

    private static void SaveGame(
        GameSession game)
    {
        var file =
            Path.Combine(
                DataFolder,
                $"campaign_{game.ChannelId}.json");

        var json =
            JsonSerializer.Serialize(
                game,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(
            file,
            json);
    }

    private static void LoadEnv()
    {
        var envPath =
            Path.Combine(
                Directory.GetCurrentDirectory(),
                ".env");

        if (!File.Exists(envPath))
        {
            return;
        }

        foreach (var line in
                 File.ReadAllLines(envPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.TrimStart().StartsWith("#"))
                continue;

            var index =
                line.IndexOf('=');

            if (index <= 0)
                continue;

            var key =
                line[..index].Trim();

            if (!string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(key)))
            {
                continue;
            }

            var value =
                line[(index + 1)..]
                .Trim()
                .Trim('"');

            Environment.SetEnvironmentVariable(
                key,
                value);
        }
    }

    private static Task DiscordLog(
        LogMessage message)
    {
        Console.WriteLine(
            message.ToString());

        return Task.CompletedTask;
    }

    private static async Task<ClassAnalysis> AnalyzeClass(
        string characterClass)
    {
        var prompt =
            $"""
            You are a fantasy RPG character class analyzer.

            The player chose this class:

            {characterClass}

            Determine the two most appropriate primary abilities
            for this class.

            Valid abilities are ONLY:

            STR
            DEX
            CON
            INT
            WIS
            CHA

            Rules:

            1. PrimaryAbility must be one of the six abilities.
            2. SecondaryAbility must be one of the six abilities.
            3. Primary and Secondary cannot be the same.
            4. Consider the meaning of the class.
            5. Custom classes are allowed.
            6. Do not reject unusual classes.
            7. Return ONLY valid JSON.

            Return JSON with exactly these fields:

            primaryAbility
            secondaryAbility
            description

            Example:

            "primaryAbility": "INT",
            "secondaryAbility": "STR",
            "description": "A battle mage who combines martial combat with arcane magic."

            Do not include Markdown.
            Do not include ```json.
            Do not include explanations outside the JSON.
            """;

        var result =
            _openAI.CompleteChat(prompt);

        var rawResponse =
            result.Value.Content[0].Text.Trim();

        try
        {
            var analysis =
                JsonSerializer.Deserialize<ClassAnalysis>(
                    rawResponse,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (analysis == null)
            {
                throw new Exception(
                    "AI returned empty class analysis.");
            }

            var validAbilities =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    "STR",
                    "DEX",
                    "CON",
                    "INT",
                    "WIS",
                    "CHA"
                };

            if (!validAbilities.Contains(
                analysis.PrimaryAbility))
            {
                analysis.PrimaryAbility =
                    "INT";
            }

            if (!validAbilities.Contains(
                analysis.SecondaryAbility))
            {
                analysis.SecondaryAbility =
                    "STR";
            }

            if (analysis.PrimaryAbility.Equals(
                analysis.SecondaryAbility,
                StringComparison.OrdinalIgnoreCase))
            {
                analysis.SecondaryAbility =
                    analysis.PrimaryAbility
                        .Equals(
                            "STR",
                            StringComparison.OrdinalIgnoreCase)
                        ? "CON"
                        : "STR";
            }

            return analysis;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Class analysis error: {ex}");

            return new ClassAnalysis
            {
                PrimaryAbility = "INT",
                SecondaryAbility = "STR",
                Description =
                    "A versatile fantasy adventurer."
            };
        }
    }

    private static void AssignAbilityScores(
        Character character)
    {
        var scores =
            new[] { 15, 14, 13, 12, 10, 8 };

        var abilities =
            new[]
            {
                "STR",
                "DEX",
                "CON",
                "INT",
                "WIS",
                "CHA"
            };

        var primary =
            character.PrimaryAbility;

        var secondary =
            character.SecondaryAbility;

        var remaining =
            abilities
                .Where(x =>
                    !x.Equals(
                        primary,
                        StringComparison.OrdinalIgnoreCase) &&
                    !x.Equals(
                        secondary,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        SetAbility(
            character,
            primary,
            15);

        SetAbility(
            character,
            secondary,
            14);

        var remainingScores =
            new[] { 13, 12, 10, 8 };

        for (var i = 0;
            i < remaining.Count;
            i++)
        {
            SetAbility(
                character,
                remaining[i],
                remainingScores[i]);
        }
    }

    private static void SetAbility(
        Character character,
        string ability,
        int value)
    {
        switch (ability.ToUpperInvariant())
        {
            case "STR":
                character.STR = value;
                break;

            case "DEX":
                character.DEX = value;
                break;

            case "CON":
                character.CON = value;
                break;

            case "INT":
                character.INT = value;
                break;

            case "WIS":
                character.WIS = value;
                break;

            case "CHA":
                character.CHA = value;
                break;
        }
    }

    private static int GetAbilityScore(
        Character character,
        string ability)
    {
        return ability.ToUpperInvariant() switch
        {
            "STR" => character.STR,
            "DEX" => character.DEX,
            "CON" => character.CON,
            "INT" => character.INT,
            "WIS" => character.WIS,
            "CHA" => character.CHA,
            _ => 10
        };
    }

    private static int GetAbilityModifier(
        int score)
    {
        return (int)Math.Floor(
            (score - 10) / 2.0);
    }
}
