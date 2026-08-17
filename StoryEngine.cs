using Discord;
using Discord.WebSocket;
using System.Text.Json;

public partial class DungeonMasterBot
{
    private static string GetChineseAbilityName(
        string ability)
    {
        return ability switch
        {
            "STR" => "力量",
            "DEX" => "敏捷",
            "CON" => "体质",
            "INT" => "智力",
            "WIS" => "感知",
            "CHA" => "魅力",
            _ => ability
        };
    }

    private static async Task GenerateOpeningScene(
        IMessageChannel channel,
        GameSession game)
    {
        if (game.Ended)
            return;

        var remainingMinutes =
            game.StartedAt.HasValue
                ? Math.Max(
                    0,
                    game.DurationMinutes -
                    (int)(DateTime.UtcNow - game.StartedAt.Value).TotalMinutes)
                : game.DurationMinutes;

        var storyPhase =
            GetStoryPhase(game);
        var timeGuidance =
            BuildTimeManagementGuidance(game);

        var languageInstruction =
            game.Language == GameLanguage.Chinese
                ? "Write the entire adventure in Simplified Chinese."
                : "Write the entire adventure in English.";

        var characters =
            string.Join(
                "\n",
                game.Players.Select(
                    p =>
                        $"- {p.Character!.Name}, {p.Character.Class}"));

        var prompt =
            $"""
            You are the Dungeon Master for a fantasy role-playing game.

            {languageInstruction}

            TARGET DURATION:
            {game.DurationMinutes} minutes

            REMAINING TIME:
            {remainingMinutes} minutes

            STORY PHASE:
            {storyPhase}

            TIME PRESSURE GUIDANCE:
            {timeGuidance}

            Create the opening scene for the adventure.

            Players:
            {characters}

            IMPORTANT:

            1. Introduce an immersive fantasy location.
            2. Introduce an immediate problem or danger.
            3. Give the players meaningful things they can react to.
            4. Do not control the players' actions.
            5. Create exactly 3 or 4 choices based specifically on the scene.
            6. Do NOT use generic choices such as:
            Attack, Investigate, Talk, Explore.
            7. Choices must be specific to what is happening.
            8. Different adventures should have different choices.
            9. Keep choice labels short enough for Discord buttons.
            10. Do not require a dice roll for the opening scene.
            11. The opening scene should introduce at least one potential source of danger, conflict, mystery, or urgency.
            12. The opening scene should create situations where the players can make meaningful decisions.
            13. Do not resolve the players' actions during the opening scene.
            14. If the remaining time is already short, make the opening scene feel urgent and close to the main conflict.

            Return ONLY valid JSON.

            The JSON must contain these fields:

            requires_roll:
            true or false

            roll_type:
            A short description of the required roll, or an empty string.

            ability:
            If requires_roll is true, choose exactly one:
            STR, DEX, CON, INT, WIS, CHA

            If requires_roll is false:
            return an empty string.

            dc:
            If requires_roll is true:
            choose a difficulty between 8 and 20.

            If requires_roll is false:
            return 0.

            narration:
            The Dungeon Master's narration describing what happens.

            choices:
            An array containing 3 or 4 choices.

            Each choice must contain:

            id:
            A unique short identifier.

            label:
            A short Discord button label.

            action:
            The action that the player would attempt.

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
            var json =
                JsonSerializer.Deserialize<AdventureResponse>(
                    rawResponse,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (json == null)
            {
                throw new Exception(
                    "AI returned an empty response.");
            }

            var scene =
                json.Narration?.Trim();

            if (string.IsNullOrWhiteSpace(scene))
            {
                scene =
                    "The adventure begins...";
            }

            if (game.Ended)
                return;

            game.CurrentScene =
                scene;

            game.Turn = 0;

            SaveGame(game);

            await SendNarrationAsync(
                channel,
                game,
                scene);

            Console.WriteLine(
                $"DURATION: {game.DurationMinutes}");

            Console.WriteLine(
                $"START TIME: {game.StartedAt}");

            Console.WriteLine(
                $"REMAINING: {remainingMinutes}");

            Console.WriteLine(
                $"STORY PHASE: {storyPhase}");

            if (json.Choices != null &&
                json.Choices.Count > 0)
            {
                if (game.Ended)
                    return;

                CurrentChoices[channel.Id] =
                    json.Choices
                        .Take(4)
                        .ToList();

                await SendChoices(
                    channel,
                    game,
                    CurrentChoices[channel.Id],
                    GetCurrentTurnPlayer(game));
            }
            else
            {
                await channel.SendMessageAsync(
                    game.Language == GameLanguage.Chinese
                        ? "❌ 地下城主没有生成任何选择。"
                        : "❌ The Dungeon Master did not generate any choices.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Opening scene parsing error: {ex}");

            Console.WriteLine(
                $"Raw AI response: {rawResponse}");

            await channel.SendMessageAsync(
                "❌ I couldn't understand the Dungeon Master's opening scene.");
        }
    }

    private static async Task HandlePlayerAction(
        SocketMessage message,
        string action)
    {
        var channelId =
            message.Channel.Id;

        if (!Games.TryGetValue(
            channelId,
            out var game))
        {
            await message.Channel.SendMessageAsync(
                "❌ No active game.");

            return;
        }

        if (game.Ended)
        {
            await message.Channel.SendMessageAsync(
                game.Language == GameLanguage.Chinese
                    ? "⏳ 冒险已经结束了。"
                    : "⏳ The adventure has already ended.");

            return;
        }

        if (!game.Started)
        {
            await message.Channel.SendMessageAsync(
                "❌ The adventure hasn't started yet.");

            return;
        }

        var player =
            game.Players.FirstOrDefault(
                x => x.PlayerId == message.Author.Id);

        if (player == null)
        {
            await message.Channel.SendMessageAsync(
                "❌ You are not part of this adventure.");

            return;
        }

        await message.Channel.SendMessageAsync(
            game.Language == GameLanguage.Chinese
                ? "❌ 请使用下方的行动按钮。"
                : "❌ Please use the action buttons below.");
    }

    private static async Task ResolveAction(
        IMessageChannel channel,
        GameSession game,
        Player player,
        string action,
        bool risky,
        string choiceAbility,
        int choiceDC)
    {
        if (game.Ended)
            return;

        var languageInstruction =
            game.Language == GameLanguage.Chinese
                ? "Write the entire adventure response in Simplified Chinese."
                : "Write the entire adventure response in English.";

        var remainingMinutes =
            GetRemainingMinutes(game);

        var storyPhase =
            GetStoryPhase(game);
        var timeGuidance =
            BuildTimeManagementGuidance(game);

        if (risky)
        {
            var ability =
                (choiceAbility ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

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

            if (!validAbilities.Contains(ability))
            {
                ability = "INT";
            }

            var abilityScore =
                GetAbilityScore(
                    player.Character!,
                    ability);

            var modifier =
                GetAbilityModifier(
                    abilityScore);

            var roll =
                Random.Shared.Next(1, 21);

            var total =
                roll + modifier;

            var dc =
                choiceDC;

            if (dc < 8)
                dc = 8;

            if (dc > 20)
                dc = 20;

            var success =
                total >= dc;

            var abilityName =
                ability switch
                {
                    "STR" => "Strength",
                    "DEX" => "Dexterity",
                    "CON" => "Constitution",
                    "INT" => "Intelligence",
                    "WIS" => "Wisdom",
                    "CHA" => "Charisma",
                    _ => ability
                };

            var modifierText =
                modifier >= 0
                    ? $"+{modifier}"
                    : modifier.ToString();

            var resultText =
                success
                    ? "✅ SUCCESS"
                    : "❌ FAILURE";

            await channel.SendMessageAsync(
                $"""
                🎲 **{abilityName} Check**

                D20: **{roll}**
                {ability}: **{modifierText}**
                Total: **{total}**
                DC: **{dc}**

                {resultText}
                """);

            var narration =
                await ContinueAfterRoll(
                    channel,
                    game,
                    player,
                    action,
                    ability,
                    roll,
                    modifier,
                    total,
                    dc,
                    success);

            if (string.IsNullOrWhiteSpace(narration))
            {
                narration =
                    success
                        ? "The action succeeds."
                        : "The action fails.";
            }

            if (game.Ended)
                return;

            game.Turn++;

            game.CurrentScene =
                narration;

            game.AdventureHistory.Add(
                $"Turn {game.Turn}: {narration}");

            SaveGame(game);

            await SendNarrationAsync(
                channel,
                game,
                narration);

            await GenerateChoices(
                channel,
                game,
                GetCurrentTurnPlayer(game) ?? player);

            return;
        }

        var history =
            game.AdventureHistory.Count > 0
                ? string.Join(
                    "\n\n",
                    game.AdventureHistory.TakeLast(10))
                : "(No previous adventure history.)";

        var prompt =
            $"""
            You are the Dungeon Master for a fantasy role-playing game.

            {languageInstruction}

            ADVENTURE HISTORY:
            {history}

            CURRENT SCENE:
            {game.CurrentScene}

            ADVENTURE TIME:

            Target duration:
            {game.DurationMinutes} minutes

            Remaining time:
            {remainingMinutes} minutes

            Story phase:
            {storyPhase}

            TIME PRESSURE GUIDANCE:
            {timeGuidance}

            PLAYER:
            {player.Character!.Name}

            CLASS:
            {player.Character.Class}

            HP:
            {player.Character.HP}/{player.Character.MaxHP}

            STR:
            {player.Character.STR}

            DEX:
            {player.Character.DEX}

            CON:
            {player.Character.CON}

            INT:
            {player.Character.INT}

            WIS:
            {player.Character.WIS}

            CHA:
            {player.Character.CHA}

            PLAYER ACTION:
            {action}

            TIME MANAGEMENT:

            The adventure is designed to last approximately
            {game.DurationMinutes} minutes.

            Current remaining time:
            {remainingMinutes} minutes.

            {timeGuidance}

            If little time remains, resolve existing story threads
            instead of introducing major new adventures.

            Do not introduce a completely unrelated plot,
            new continent, new faction, or long side quest
            when the adventure is approaching its ending.

            IMPORTANT:

            This action has already been determined to be SAFE.

            Do NOT create a dice roll.

            Respect the player's action.

            Do not control the player's thoughts,
            feelings, or decisions.

            Do not invent additional actions for the player.

            Do not invent actions the player did not choose.

            Keep the narration immersive but concise.

            Continue the existing adventure.
            Use the adventure history to maintain continuity.

            Return ONLY the Dungeon Master narration.

            Do not include Markdown.
            """;

        var result =
            _openAI.CompleteChat(prompt);

        var narrationSafe =
            result.Value.Content[0].Text.Trim();

        if (string.IsNullOrWhiteSpace(narrationSafe))
        {
            narrationSafe =
                "The situation changes...";
        }

        if (game.Ended)
            return;

        game.Turn++;

        game.CurrentScene =
            narrationSafe;

        game.AdventureHistory.Add(
            $"Turn {game.Turn}: {narrationSafe}");

        SaveGame(game);

        await SendNarrationAsync(
            channel,
            game,
            narrationSafe);

        await GenerateChoices(
            channel,
            game,
            GetCurrentTurnPlayer(game) ?? player);
    }

    private static async Task GenerateChoices(
        IMessageChannel channel,
        GameSession game,
        Player player)
    {
        if (game.Ended)
            return;

        var languageInstruction =
            game.Language == GameLanguage.Chinese
                ? "Write the entire response in Simplified Chinese."
                : "Write the entire response in English.";

        var history =
            game.AdventureHistory.Count > 0
                ? string.Join(
                    "\n\n",
                    game.AdventureHistory.TakeLast(10))
                : "(No previous adventure history.)";

        var remainingMinutes =
            GetRemainingMinutes(game);

        var storyPhase = GetStoryPhase(game);
        var timeGuidance = BuildTimeManagementGuidance(game);

        var prompt =
            $"""
            You are the Dungeon Master for a fantasy role-playing game.

            {languageInstruction}

            ADVENTURE HISTORY:
            {history}

            ADVENTURE TIME LIMIT:

            Target adventure duration:
            {game.DurationMinutes} minutes

            Remaining time:
            {remainingMinutes} minutes

            CURRENT SCENE:
            {game.CurrentScene}

            TIME AND STORY PACING RULES:

            The adventure has a limited time.

            Current story phase:
            {storyPhase}

            Remaining time:
            {GetRemainingMinutes(game)} minutes

            Time pressure guidance:
            {timeGuidance}

            Adjust the pacing according to the remaining time.

            If the phase is OPENING / EXPLORATION:
            - Introduce locations, mysteries, characters, and early problems.
            - Do not rush toward the ending.

            If the phase is MAIN ADVENTURE:
            - Develop the main conflict.
            - Reveal important information.
            - Increase meaningful consequences.

            If the phase is CLIMAX PREPARATION:
            - Start connecting the major plot threads.
            - Increase danger and urgency.
            - Avoid introducing completely unrelated major storylines.

            If the phase is FINAL ARC:
            - Focus on resolving the main conflict.
            - Avoid unnecessary side quests.
            - Choices should move the adventure toward a meaningful conclusion.

            If the phase is FINAL SCENE:
            - The adventure should be approaching its conclusion.
            - Do not introduce a completely new major storyline.
            - Prioritize resolving the main conflict.

            PLAYER:
            {player.Character!.Name}

            CLASS:
            {player.Character.Class}

            HP:
            {player.Character.HP}/{player.Character.MaxHP}

            STR: {player.Character.STR}
            DEX: {player.Character.DEX}
            CON: {player.Character.CON}
            INT: {player.Character.INT}
            WIS: {player.Character.WIS}
            CHA: {player.Character.CHA}

            Generate the next 3 or 4 meaningful choices.

            IMPORTANT RULES:

            1. Choices must be based specifically on the CURRENT SCENE.

            2. Use the ADVENTURE HISTORY to maintain continuity.

            3. Do not forget why the player is in the current location.

            4. Do not use generic choices such as:
            Attack
            Investigate
            Talk
            Explore

            5. Choices must be specific to the current situation.

            6. Do not invent an enemy unless an enemy actually exists
            in the current scene or adventure history.

            7. Do not force combat.

            8. Some choices should be safe.

            9. Some choices may be risky.

            10. A risky choice must contain:
                risky = true
                ability = STR, DEX, CON, INT, WIS, or CHA
                dc = a number between 8 and 20

            11. A safe choice must contain:
                risky = false
                ability = empty string
                dc = 0

            12. Do not make every choice risky.

            13. Normally provide a mixture of safe and risky choices.

            14. Keep button labels short enough for Discord buttons.

            15. The action field must describe exactly what the player
                would attempt if they select that choice.

            TIME MANAGEMENT RULES:

            1. The adventure has a target duration of {game.DurationMinutes} minutes.

            2. Current remaining time is approximately {remainingMinutes} minutes.

            3. Story phase is: {storyPhase}.

            4. If more than 75% of the adventure remains:
            Continue exploration, mystery, character development,
            and gradually build toward the main conflict.

            5. If between 50% and 75% of the adventure remains:
            Begin moving the adventure toward its main conflict or climax.

            6. If between 10% and 50% of the adventure remains:
            Do not introduce major new locations, unrelated mysteries,
            or completely new story arcs.
            Start resolving existing conflicts.

            7. If 10% or less of the adventure remains:
            Focus on the final confrontation, escape, revelation,
            or resolution.
            Do not introduce a new major quest.

            8. The ending should resolve important events from the adventure history.

            9. Never abruptly end the adventure merely because time is running low.
            Create a natural conclusion using existing story elements.

            RETURN ONLY VALID JSON.

            The JSON must contain:

            choices:
            An array containing 3 or 4 AdventureChoice objects.

            Each choice must contain these fields:

            id:
            A unique short identifier.

            label:
            A short Discord button label.

            action:
            The action the player would attempt.

            risky:
            true or false.

            ability:
            If risky is true, exactly one of:
            STR
            DEX
            CON
            INT
            WIS
            CHA

            If risky is false, use an empty string.

            dc:
            If risky is true, a number between 8 and 20.

            If risky is false, use 0.

            Do not include Markdown.
            Do not include ```json.
            Do not include explanations outside the JSON.
            """;

        var result =
            _openAI.CompleteChat(prompt);

        var rawResponse =
            result.Value.Content[0].Text.Trim();

        Console.WriteLine("=================================");
        Console.WriteLine("GENERATE CHOICES RAW RESPONSE");
        Console.WriteLine(rawResponse);
        Console.WriteLine("=================================");

        try
        {
            var json =
                JsonSerializer.Deserialize<AdventureResponse>(
                    rawResponse,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (json?.Choices == null ||
                json.Choices.Count == 0)
            {
                await channel.SendMessageAsync(
                    game.Language == GameLanguage.Chinese
                        ? "❌ 地下城主没有生成新的选择。"
                        : "❌ The Dungeon Master did not generate new choices.");

                return;
            }

            if (game.Ended)
                return;

            CurrentChoices[channel.Id] =
                json.Choices
                    .Take(4)
                    .ToList();

            Console.WriteLine(
                $"Generated choices: {CurrentChoices[channel.Id].Count}");

            await SendChoices(
                channel,
                game,
                CurrentChoices[channel.Id],
                GetCurrentTurnPlayer(game) ?? player);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Choice generation error: {ex}");

            Console.WriteLine(
                $"Raw AI response: {rawResponse}");

            await channel.SendMessageAsync(
                game.Language == GameLanguage.Chinese
                    ? "❌ 无法生成下一步选择。"
                    : "❌ Failed to generate the next choices.");
        }
    }

    private static string GetRecentAdventureHistory(
        GameSession game,
        int count = 10)
    {
        if (game.AdventureHistory == null ||
            game.AdventureHistory.Count == 0)
        {
            return "No previous adventure history.";
        }

        return string.Join(
            "\n\n",
            game.AdventureHistory
                .TakeLast(count));
    }

    private static async Task<string> ContinueAfterRoll(
        IMessageChannel channel,
        GameSession game,
        Player player,
        string action,
        string ability,
        int roll,
        int modifier,
        int total,
        int dc,
        bool success)
    {
        if (game.Ended)
            return string.Empty;

        var remainingMinutes =
            GetRemainingMinutes(game);

        var storyPhase =
            GetStoryPhase(game);
        var timeGuidance =
            BuildTimeManagementGuidance(game);

        var languageInstruction =
            game.Language == GameLanguage.Chinese
                ? "Write the response entirely in Simplified Chinese."
                : "Write the response entirely in English.";

        var outcome =
            success
                ? "SUCCESS"
                : "FAILURE";

        var history =
            game.AdventureHistory.Count > 0
                ? string.Join(
                    "\n\n",
                    game.AdventureHistory.TakeLast(10))
                : "(No previous adventure history.)";

        var prompt =
            "You are the Dungeon Master.\n\n" +

            languageInstruction + "\n\n" +

            "ADVENTURE HISTORY:\n" +
            history + "\n\n" +

            "CURRENT SCENE:\n" +
            game.CurrentScene + "\n\n" +

            "PLAYER:\n" +
            player.Character!.Name + "\n\n" +

            "CLASS:\n" +
            player.Character.Class + "\n\n" +

            "HP:\n" +
            player.Character.HP +
            "/" +
            player.Character.MaxHP + "\n\n" +

            "The player attempted:\n" +
            action + "\n\n" +

            "Ability check:\n" +
            ability + "\n\n" +

            "D20:\n" +
            roll + "\n\n" +

            "Ability modifier:\n" +
            modifier + "\n\n" +

            "Total:\n" +
            total + "\n\n" +

            "Difficulty Class:\n" +
            dc + "\n\n" +

            "FINAL RESULT:\n" +
            outcome + "\n\n" +

            "IMPORTANT:\n\n" +

            "ADVENTURE TIME:\n\n" +

            "Target duration:\n" +
            game.DurationMinutes + " minutes\n\n" +

            "Remaining time:\n" +
            remainingMinutes + " minutes\n\n" +

            "Story phase:\n" +
            storyPhase + "\n\n" +

            "Time pressure guidance:\n" +
            timeGuidance + "\n\n" +

            "The dice result has already been determined by the game engine.\n" +
            "You MUST NOT change, reroll, reinterpret, or ignore the result.\n\n" +

            "If the result is SUCCESS:\n" +
            "- The player's attempted action succeeds.\n" +
            "- Describe a believable successful outcome.\n" +
            "- Do not give the player additional actions they did not choose.\n\n" +

            "If the result is FAILURE:\n" +
            "- The player's attempted action fails or only partially succeeds.\n" +
            "- Introduce a believable consequence when appropriate.\n" +
            "- Do not kill the player unless the situation genuinely warrants it.\n" +
            "- Do not pretend the action succeeded.\n\n" +

            "Do not control the player's thoughts, feelings, or decisions.\n" +
            "Do not invent actions for the player.\n" +
            "Keep the narration immersive but concise.\n\n" +

            "Return ONLY the Dungeon Master narration.";

        var result =
            _openAI.CompleteChat(prompt);

        var narration =
            result.Value.Content[0].Text.Trim();

        if (string.IsNullOrWhiteSpace(narration))
        {
            narration =
                success
                    ? "The action succeeds."
                    : "The action fails.";
        }

        return narration;
    }

    private static async Task SendChoices(
        IMessageChannel channel,
        GameSession game,
        List<AdventureChoice> choices,
        Player? currentTurnPlayer)
    {
        await ClearCurrentChoiceButtonsAsync(game.ChannelId);

        foreach (var player in game.Players)
        {
            await SendPlayerStatus(
                channel,
                game,
                player);
        }

        var builder =
            new ComponentBuilder();

        foreach (var choice in choices.Take(4))
        {
            var label = choice.Label;

            if (choice.Risky)
            {
                label += " 🎲";
            }

            builder.WithButton(
                label: label,
                customId: $"choice_{choice.Id}",
                style: ButtonStyle.Primary);
        }

        var question =
            currentTurnPlayer == null
                ? game.Language == GameLanguage.Chinese
                    ? "### 你要怎么做？"
                    : "### What do you do next?"
                : game.Language == GameLanguage.Chinese
                    ? $"### 你要怎么做？\n\n轮到：**{currentTurnPlayer.Character?.Name ?? currentTurnPlayer.Username}** (<@{currentTurnPlayer.PlayerId}>)"
                    : $"### What do you do next?\n\nTurn: **{currentTurnPlayer.Character?.Name ?? currentTurnPlayer.Username}** (<@{currentTurnPlayer.PlayerId}>)";

        var message = await channel.SendMessageAsync(
            question,
            components: builder.Build());

        CurrentChoiceMessages[game.ChannelId] = message.Id;
        StartChoiceTimeoutTimer(game);
    }

    private static async Task SendPlayerStatus(
        IMessageChannel channel,
        GameSession game,
        Player player)
    {
        var character = player.Character;

        if (character == null)
            return;

        string ModText(int modifier)
        {
            return modifier >= 0
                ? $"+{modifier}"
                : modifier.ToString();
        }

        var strMod = GetAbilityModifier(character.STR);
        var dexMod = GetAbilityModifier(character.DEX);
        var conMod = GetAbilityModifier(character.CON);
        var intMod = GetAbilityModifier(character.INT);
        var wisMod = GetAbilityModifier(character.WIS);
        var chaMod = GetAbilityModifier(character.CHA);

        var text =
            $"""
            ⚔️ **{character.Name}**
            {character.Class} · Lv.{character.Level}

            ❤️ HP: **{character.HP} / {character.MaxHP}**

            💪 STR: **{character.STR} ({ModText(strMod)})**
            🏃 DEX: **{character.DEX} ({ModText(dexMod)})**
            🫀 CON: **{character.CON} ({ModText(conMod)})**
            🧠 INT: **{character.INT} ({ModText(intMod)})**
            👁️ WIS: **{character.WIS} ({ModText(wisMod)})**
            🗣️ CHA: **{character.CHA} ({ModText(chaMod)})**

            ✨ Primary: **{character.PrimaryAbility}**
            🔮 Secondary: **{character.SecondaryAbility}**
            """;

        await channel.SendMessageAsync(text);
    }
}
