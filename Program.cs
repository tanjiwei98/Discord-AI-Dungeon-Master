using Discord;
using Discord.WebSocket;
using OpenAI.Chat;
using System.Text.Json;

public partial class DungeonMasterBot
{
    private static DiscordSocketClient _discord = null!;
    private static ChatClient _openAI = null!;

    private static readonly Dictionary<ulong, GameSession> Games = new();
	
	private static readonly Dictionary<ulong, List<AdventureChoice>> CurrentChoices = new();

    private static string DataFolder =>
        Path.Combine(
            Directory.GetCurrentDirectory(),
            "data");

    public static async Task RunAsync(
        BotConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        configuration ??=
            BotConfiguration.FromEnvironment();

        ApplyConfiguration(configuration);

        LoadEnv();

        var discordToken =
            Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");

        var openAIKey =
            Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        var model =
            Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? "gpt-5.4-mini";

        if (string.IsNullOrWhiteSpace(discordToken))
        {
            Console.WriteLine("ERROR: DISCORD_BOT_TOKEN is missing.");
            return;
        }

        if (string.IsNullOrWhiteSpace(openAIKey))
        {
            Console.WriteLine("ERROR: OPENAI_API_KEY is missing.");
            return;
        }

        Directory.CreateDirectory(DataFolder);

        _discord = new DiscordSocketClient(
            new DiscordSocketConfig
            {
                GatewayIntents =
                    GatewayIntents.AllUnprivileged |
                    GatewayIntents.GuildVoiceStates |
                    GatewayIntents.MessageContent,
                EnableVoiceDaveEncryption = true
            });

        _discord.Log += DiscordLog;
        _discord.MessageReceived += MessageReceived;
		_discord.ButtonExecuted += ButtonExecuted;

        _openAI = new ChatClient(
            model,
            openAIKey);

        Console.WriteLine("Connecting to Discord...");

        await _discord.LoginAsync(
            TokenType.Bot,
            discordToken);

        await _discord.StartAsync();

        Console.WriteLine(
            "AI Dungeon Master is online!");

        try
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // The host requested shutdown.
        }
        finally
        {
            try
            {
                await _discord.StopAsync();
                await _discord.LogoutAsync();
            }
            catch
            {
                // Best effort shutdown.
            }
        }
    }

    // =========================================================
    // DISCORD MESSAGE HANDLER
    // =========================================================

	private static Task MessageReceived(
    SocketMessage message)
	{
		_ = Task.Run(async () =>
		{
			try
			{
				await ProcessMessage(message);
			}
			catch (Exception ex)
			{
				Console.WriteLine(
					$"Message processing error: {ex}");
			}
		});
	
		return Task.CompletedTask;
	}

    private static async Task ProcessMessage(
        SocketMessage message)
    {
        if (message.Author.IsBot)
            return;

        var content = message.Content.Trim();

		// Character creation response
		if (CharacterRequest.WaitingFor.TryGetValue(
			message.Author.Id,
			out var characterChannelId))
		{
			if (message.Channel.Id == characterChannelId)
			{
				var parts = content.Split(
					'|',
					2,
					StringSplitOptions.TrimEntries);
		
				if (parts.Length != 2)
				{
					await message.Channel.SendMessageAsync(
						"❌ Please use this format:\n\n" +
						"`Name | Class`\n\n" +
						"Example:\n" +
						"`JW | Battle Mage`");
		
					return;
				}
		
				var name = parts[0];
				var characterClass = parts[1];
		
				if (!Games.TryGetValue(
					message.Channel.Id,
					out var game))
				{
					CharacterRequest.WaitingFor.Remove(
						message.Author.Id);
		
					await message.Channel.SendMessageAsync(
						"❌ No active game found.");
		
					return;
				}
		
				var player =
					game.Players.FirstOrDefault(
						p => p.PlayerId == message.Author.Id);
		
				if (player == null)
				{
					CharacterRequest.WaitingFor.Remove(
						message.Author.Id);
		
					await message.Channel.SendMessageAsync(
						"❌ You need to join the game first.");
		
					return;
				}
		
				var classAnalysis =
					await AnalyzeClass(
						characterClass);
				
				player.Character = new Character
				{
					Name = name,
					Class = characterClass,
					Level = 1,
					HP = 20,
					MaxHP = 20,
				
					STR = 10,
					DEX = 10,
					CON = 10,
					INT = 10,
					WIS = 10,
					CHA = 10,
				
					PrimaryAbility =
						classAnalysis.PrimaryAbility,
				
					SecondaryAbility =
						classAnalysis.SecondaryAbility
				};
				
				AssignAbilityScores(
					player.Character);
		
				player.Character.Inventory.Add(
					"Basic Adventurer Gear");
		
				CharacterRequest.WaitingFor.Remove(
					message.Author.Id);
		
				SaveGame(game);
		
				await message.Channel.SendMessageAsync(
					GameText.CharacterCreated(
						game.Language,
						player.Character.Name,
						player.Character.Class,
						player.Character.HP,
						player.Character.MaxHP,
						player.Character.Level));
		
				return;
			}
		}


        Console.WriteLine(
            $"[{message.Author.Username}] {content}");

        if (content.Equals(
            "!ping",
            StringComparison.OrdinalIgnoreCase))
        {
            await message.Channel.SendMessageAsync(
                "🏰 AI Dungeon Master is online!");

            return;
        }
		
		if (content.Equals(
			"!reset",
			StringComparison.OrdinalIgnoreCase))
		{
			await ResetGame(message);
			return;
		}
		
        if (content.StartsWith(
			"!game",
			StringComparison.OrdinalIgnoreCase))
		{
			var parts =
				content.Split(
					' ',
					StringSplitOptions.RemoveEmptyEntries);
		
            var defaultLanguage =
                Environment.GetEnvironmentVariable("DEFAULT_LANGUAGE");

            var language =
                string.Equals(
                    defaultLanguage,
                    "Chinese",
                    StringComparison.OrdinalIgnoreCase)
                    ? GameLanguage.Chinese
                    : GameLanguage.English;
		
			var duration =
				parts.Length > 2
					? ParseDuration(parts[2])
					: 60;
		
			if (parts.Length >= 2)
			{
				if (parts[1].Equals(
					"cn",
					StringComparison.OrdinalIgnoreCase))
				{
					language =
						GameLanguage.Chinese;
				}
				else if (parts[1].Equals(
					"en",
					StringComparison.OrdinalIgnoreCase))
				{
					language =
						GameLanguage.English;
				}
				else
				{
					await message.Channel.SendMessageAsync(
						"❌ Usage: `!game en [1m|2m|5m|15m|30m|1h]` or `!game cn [1m|2m|5m|15m|30m|1h]`");
		
					return;
				}
			}
		
			await CreateGame(
				message,
				language,
				duration);
		
			return;
		}

        if (content.Equals(
            "!join",
            StringComparison.OrdinalIgnoreCase))
        {
            await JoinGame(message);
            return;
        }

        if (content.Equals(
            "!character",
            StringComparison.OrdinalIgnoreCase))
        {
            await CreateCharacter(message);
            return;
        }

        if (content.Equals(
            "!start",
            StringComparison.OrdinalIgnoreCase))
        {
            await StartGame(message);
            return;
        }

        if (content.Equals(
            "!status",
            StringComparison.OrdinalIgnoreCase))
        {
            await ShowStatus(message);
            return;
        }

        if (content.Equals(
            "!help",
            StringComparison.OrdinalIgnoreCase))
        {
            await ShowHelp(message);
            return;
        }

		if (content.StartsWith(
			"!voice",
			StringComparison.OrdinalIgnoreCase))
		{
			await HandleVoiceCommand(
				message,
				content);

			return;
		}

		if (content.StartsWith(
			"!tts",
			StringComparison.OrdinalIgnoreCase))
		{
			await HandleLegacyVoiceCommand(
				message,
				content);

			return;
		}

        // Free roleplay
        if (content.StartsWith(
            "!action ",
            StringComparison.OrdinalIgnoreCase))
        {
            var action =
                content.Substring(8).Trim();

            await HandlePlayerAction(
                message,
                action);

            return;
        }
    }
	
	private static int ParseDuration(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return 60;
	
		input =
			input.Trim()
				.ToLowerInvariant();
	
		// 1h / 1.5h / 2h
		if (input.EndsWith("h"))
		{
			if (double.TryParse(
				input[..^1],
				out var hours))
			{
				return Math.Max(
					1,
					(int)Math.Round(hours * 60));
			}
		}
	
		// 15m / 30m / 90m
		if (input.EndsWith("m"))
		{
			if (int.TryParse(
				input[..^1],
				out var minutes))
			{
				return Math.Max(
					1,
					minutes);
			}
		}
	
		// Invalid input defaults to 60 minutes
		return 60;
	}	
	private static async Task ResetGame(
	SocketMessage message)
	{
		if (message.Author is not SocketGuildUser user)
		{
			await message.Channel.SendMessageAsync(
				"❌ This command can only be used inside a Discord server.");

			return;
		}

		// Only administrators can reset the game
		if (!user.GuildPermissions.Administrator)
		{
			await message.Channel.SendMessageAsync(
				"❌ Only server administrators can reset the game.");

			return;
		}

		var channel =
			message.Channel as SocketTextChannel;

		if (channel == null)
		{
			await message.Channel.SendMessageAsync(
				"❌ This command only works in a text channel.");

			return;
		}

		var channelId =
			channel.Id;

		await channel.SendMessageAsync(
			"🧹 **Resetting Dungeon...**\n\n" +
			"Clearing game data and recent messages...");

		// ---------------------------------------------------------
		// Remove game from memory
		// ---------------------------------------------------------

		CancelGameExpirationTimer(channelId);
		CancelChoiceTimeoutTimer(channelId);

		Games.Remove(channelId);
		CurrentChoices.Remove(channelId);
		await ClearCurrentChoiceButtonsAsync(channelId);

		ClearVoiceSession(channelId);

		// ---------------------------------------------------------
		// Remove character creation state
		// ---------------------------------------------------------

		var waitingPlayers =
			CharacterRequest.WaitingFor
				.Where(x => x.Value == channelId)
				.Select(x => x.Key)
				.ToList();

		foreach (var playerId in waitingPlayers)
		{
			CharacterRequest.WaitingFor.Remove(
				playerId);
		}

		// ---------------------------------------------------------
		// Delete local campaign JSON
		// ---------------------------------------------------------

		var campaignFile =
			Path.Combine(
				DataFolder,
				$"campaign_{channelId}.json");

		if (File.Exists(campaignFile))
		{
			File.Delete(campaignFile);
		}

		// ---------------------------------------------------------
		// Delete Discord messages
		// ---------------------------------------------------------

		try
		{
			var messages =
				await channel
					.GetMessagesAsync(100)
					.FlattenAsync();

			var messageList =
				messages.ToList();

			if (messageList.Count > 0)
			{
				// Bulk delete messages that Discord allows us to delete
				var recentMessages =
					messageList
						.Where(x =>
							DateTimeOffset.UtcNow - x.Timestamp
							< TimeSpan.FromDays(14))
						.ToList();

				if (recentMessages.Count > 0)
				{
					await channel.DeleteMessagesAsync(
						recentMessages);
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine(
				$"Discord cleanup error: {ex.Message}");
		}

		// ---------------------------------------------------------
		// Fresh game message
		// ---------------------------------------------------------

		await channel.SendMessageAsync(
			"""
			🧹 **Dungeon Reset Complete!**

			The previous adventure has been cleared.

			🏰 **Ready for a new adventure?**

			Create a new game with:

			`!game`

			Then:

			`!join`

			`!character`

			`!start`
			""");
}


    // =========================================================
    // CREATE GAME
    // =========================================================

    private static async Task CreateGame(
		SocketMessage message,
		GameLanguage language,
		int durationMinutes)
	{
		var channelId = message.Channel.Id;
	
		if (Games.ContainsKey(channelId))
		{
			await message.Channel.SendMessageAsync(
				"⚠️ A game already exists in this channel.");
	
			return;
		}
	
		var game = new GameSession
		{
			ChannelId = channelId,
			DungeonMaster = "AI Dungeon Master",
			Started = false,
			Turn = 0,
			Language = language,
			DurationMinutes = durationMinutes,
			VoiceEnabled = false,
			VoiceChannelId = null
		};
	
		Games[channelId] = game;
	
		SaveGame(game);

		var languageText =
			language == GameLanguage.Chinese
				? "🇨🇳 中文"
				: "🇬🇧 English";
		
		var durationText =
			language == GameLanguage.Chinese
				? $"⏱️ 冒险时长：{game.DurationMinutes} 分钟"
				: $"⏱️ Adventure Duration: {game.DurationMinutes} minutes";
		
		await message.Channel.SendMessageAsync(
			text:
				GameText.NewAdventure(language) +
				"\n\n" +
				GameText.Language(language) +
				"\n" +
				durationText +
				"\n" +
				GameText.VoiceOn(language));

		if (message.Channel is SocketTextChannel textChannel &&
			message.Author is SocketGuildUser user)
		{
			await EnsureVoiceAutoJoinAsync(
				textChannel,
				user,
				game);
		}
		
		Console.WriteLine(
			$"GAME CREATED: {durationMinutes} minutes");
		
		Console.WriteLine(
			$"GAME SESSION DURATION: {game.DurationMinutes} minutes");
	}

    // =========================================================
    // JOIN GAME
    // =========================================================

    private static async Task JoinGame(
        SocketMessage message)
    {
        var channelId = message.Channel.Id;

        if (!Games.TryGetValue(
            channelId,
            out var game))
        {
			await message.Channel.SendMessageAsync(
				"❌ No game exists here. Use `!game` first.");

            return;
        }

        var playerId =
            message.Author.Id;

        if (game.Players.Any(
            x => x.PlayerId == playerId))
        {
            await message.Channel.SendMessageAsync(
                "You are already in the adventure!");

            return;
        }

        game.Players.Add(
            new Player
            {
                PlayerId = playerId,
                Username = message.Author.Username
            });

        SaveGame(game);

		await message.Channel.SendMessageAsync(
			$"⚔️ **{message.Author.Username}** joined the adventure!");
    }

    // =========================================================
    // CHARACTER
    // =========================================================

    private static async Task CreateCharacter(
        SocketMessage message)
    {
        var channelId = message.Channel.Id;

        if (!Games.TryGetValue(
            channelId,
            out var game))
        {
			await message.Channel.SendMessageAsync(
				"❌ Create a game first with `!game`.");

            return;
        }

        var player =
            game.Players.FirstOrDefault(
                x => x.PlayerId == message.Author.Id);

        if (player == null)
        {
			await message.Channel.SendMessageAsync(
				"❌ Join the game first with `!join`.");

            return;
        }

        if (player.Character != null)
        {
			await message.Channel.SendMessageAsync(
				"⚠️ You already have a character.");

            return;
        }

        await message.Channel.SendMessageAsync(
			GameText.CreateCharacter(
				game.Language));

        CharacterRequest.WaitingFor[message.Author.Id] =
            message.Channel.Id;
    }

    // =========================================================
    // START GAME
    // =========================================================

    private static async Task StartGame(
        SocketMessage message)
    {
        var channelId = message.Channel.Id;

        if (!Games.TryGetValue(
            channelId,
            out var game))
        {
			await message.Channel.SendMessageAsync(
				"❌ No game exists.");

            return;
        }

        if (game.Started)
        {
			await message.Channel.SendMessageAsync(
				"⚠️ The adventure has already started.");

            return;
        }

        if (game.Players.Count == 0)
        {
			await message.Channel.SendMessageAsync(
				"❌ Nobody has joined.");

            return;
        }

        if (game.Players.Any(
            x => x.Character == null))
        {
			await message.Channel.SendMessageAsync(
				"❌ Everyone needs a character first.");

            return;
        }

        game.Started = true;
		game.StartedAt = DateTime.UtcNow;
        game.Turn = 1;

        SaveGame(game);

        StartGameExpirationTimer(game);

		if (message.Channel is SocketTextChannel textChannel &&
			message.Author is SocketGuildUser user)
		{
			await EnsureVoiceAutoJoinAsync(
				textChannel,
				user,
				game);
		}
		
        await GenerateOpeningScene(
            message.Channel,
            game);
    }
	
	private static int GetRemainingMinutes(
		GameSession game)
	{
		if (!game.StartedAt.HasValue)
			return game.DurationMinutes;
	
		var elapsed =
			DateTime.UtcNow -
			game.StartedAt.Value;
	
		var remaining =
			game.DurationMinutes -
			(int)elapsed.TotalMinutes;
	
		return Math.Max(
			0,
			remaining);
	}

	private static async Task ButtonExecuted(
		SocketMessageComponent component)
	{
		try
		{
			// Acknowledge the Discord interaction immediately.
			await component.DeferAsync();
	
			var channelId =
				component.Channel.Id;
	
			if (!Games.TryGetValue(
				channelId,
				out var game))
			{
				await component.FollowupAsync(
					game?.Language == GameLanguage.Chinese
						? "❌ 当前没有进行中的游戏。"
						: "❌ No active game.",
					ephemeral: true);

				return;
			}

			if (game.Ended)
			{
				await component.FollowupAsync(
					game.Language == GameLanguage.Chinese
						? "⏳ 冒险已经结束了。"
						: "⏳ The adventure has already ended.",
					ephemeral: true);

				return;
			}

			if (!game.Started)
			{
				await component.FollowupAsync(
					game.Language == GameLanguage.Chinese
						? "❌ 冒险还没有开始。"
						: "❌ The adventure hasn't started yet.",
					ephemeral: true);
	
				return;
			}
	
			var player =
				game.Players.FirstOrDefault(
					x => x.PlayerId == component.User.Id);
	
			if (player == null)
			{
				await component.FollowupAsync(
					game.Language == GameLanguage.Chinese
						? "❌ 你不属于这个冒险。"
						: "❌ You are not part of this adventure.",
					ephemeral: true);
	
				return;
			}

			var currentTurnPlayer =
				GetCurrentTurnPlayer(game);

			if (currentTurnPlayer == null)
			{
				await component.FollowupAsync(
					game.Language == GameLanguage.Chinese
						? "❌ 当前没有可用的回合。"
						: "❌ There is no active turn right now.",
					ephemeral: true);

				return;
			}

			if (currentTurnPlayer.PlayerId != component.User.Id)
			{
				await component.FollowupAsync(
					game.Language == GameLanguage.Chinese
						? $"⌛ 现在不是你的回合。轮到 **{currentTurnPlayer.Username}** 了。"
						: $"⌛ It is not your turn. **{currentTurnPlayer.Username}** should choose now.",
					ephemeral: true);

				return;
			}
	
			var customId =
				component.Data.CustomId;
	
			if (!customId.StartsWith(
				"choice_",
				StringComparison.OrdinalIgnoreCase))
			{
				await component.FollowupAsync(
					game.Language == GameLanguage.Chinese
						? "❌ 未知的选择。"
						: "❌ Unknown choice.",
					ephemeral: true);
	
				return;
			}
	
			var choiceId =
				customId.Substring(
					"choice_".Length);
	
			if (!CurrentChoices.TryGetValue(
				channelId,
				out var choices))
			{
				await component.FollowupAsync(
					game.Language == GameLanguage.Chinese
						? "❌ 这些选择已经失效了。"
						: "❌ These choices are no longer available.",
					ephemeral: true);
	
				return;
			}
	
			var choice =
				choices.FirstOrDefault(
					x => x.Id == choiceId);
	
			if (choice == null)
			{
				await component.FollowupAsync(
					game.Language == GameLanguage.Chinese
						? "❌ 找不到这个选择。"
						: "❌ Choice not found.",
					ephemeral: true);
	
				return;
			}
	
			if (choice.Risky)
			{
				await ResolveChoiceWithRoll(
					component.Channel,
					game,
					player,
					choice);
			}
			else
			{
				await ResolveAction(
					component.Channel,
					game,
					player,
					choice.Action,
					choice.Risky,
					choice.Ability,
					choice.DC);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine(
				$"Button error: {ex}");
	
			try
			{
				await component.FollowupAsync(
					"❌ Something went wrong while processing your action.",
					ephemeral: true);
			}
			catch
			{
				// Ignore if Discord interaction has already expired.
			}
		}
	}
	
	private static async Task ResolveChoiceWithRoll(
		IMessageChannel channel,
		GameSession game,
		Player player,
		AdventureChoice choice)
	{
		var character = player.Character;
	
		if (character == null)
			return;
	
		var ability =
			choice.Ability
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
				character,
				ability);
	
		var modifier =
			GetAbilityModifier(
				abilityScore);
	
		var roll =
			Random.Shared.Next(1, 21);
	
		var total =
			roll + modifier;
	
		var dc =
			choice.DC;
	
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
	
		var chineseAbilityName =
			ability switch
			{
				"STR" => "力量",
				"DEX" => "敏捷",
				"CON" => "体质",
				"INT" => "智力",
				"WIS" => "感知",
				"CHA" => "魅力",
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
	
		var rollMessage =
			game.Language == GameLanguage.Chinese
				? $"""
				🎲 **{chineseAbilityName}检定**
	
				D20: **{roll}**
				{ability}: **{modifierText}**
				总计: **{total}**
				DC: **{dc}**
	
				{resultText}
				"""
				: $"""
				🎲 **{abilityName} Check**
	
				D20: **{roll}**
				{ability}: **{modifierText}**
				Total: **{total}**
				DC: **{dc}**
	
				{resultText}
				""";
	
		await channel.SendMessageAsync(
			rollMessage);
	
		// =========================================================
		// AI resolves the actual dice result
		// =========================================================
	
		var narration =
			await ContinueAfterRoll(
				channel,
				game,
				player,
				choice.Action,
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
	
		// =========================================================
		// Update history
		// =========================================================
	
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
	
		// =========================================================
		// Generate next choices
		// =========================================================
	
		await GenerateChoices(
			channel,
			game,
			GetCurrentTurnPlayer(game) ?? player);
	}


    // =========================================================
    // STATUS
    // =========================================================

    private static async Task ShowStatus(
        SocketMessage message)
    {
        if (!Games.TryGetValue(
            message.Channel.Id,
            out var game))
        {
            await message.Channel.SendMessageAsync(
                "❌ No game exists.");

            return;
        }

        var players =
            string.Join(
                "\n",
                game.Players.Select(
                    p =>
                        $"👤 {p.Username} — {p.Character?.Name ?? "No character"}"));

        await message.Channel.SendMessageAsync(
            $"""
            🏰 **Adventure Status**

            Players:
            {players}

            Turn:
            {game.Turn}

            Started:
            {game.Started}

            Remaining Time:
            {GetRemainingMinutes(game)} minutes

            Story Phase:
            {GetStoryPhase(game)}

            Voice Enabled:
            {game.VoiceEnabled}

            Voice Channel:
            {(game.VoiceChannelId?.ToString() ?? "None")}
            """);
    }

    // =========================================================
    // HELP
    // =========================================================

    private static async Task ShowHelp(
    SocketMessage message)
	{
		var help =
			"🧙 **AI Dungeon Master Commands**\n\n" +
			"`!game`\n" +
			"Create an adventure.\n\n" +
			"`!join`\n" +
			"Join the adventure.\n\n" +
			"`!character`\n" +
			"Create your character.\n\n" +
			"`!start`\n" +
			"Start the adventure.\n\n" +
			"`!voice join`\n" +
			"Join your current voice channel.\n\n" +
			"`!voice on / off`\n" +
			"Enable or disable voice narration.\n\n" +
			"`!voice leave`\n" +
			"Leave the voice channel.\n\n" +
			"`!action <your action>`\n" +
			"Freely roleplay.\n\n" +
			"`!status`\n" +
			"Show game status.\n\n" +
			"`!reset`\n" +
			"Reset the current adventure.\n\n" +
			"`!ping`\n" +
			"Test the bot.";
	
		await message.Channel.SendMessageAsync(help);
	}

}


