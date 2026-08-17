using Discord;
using System.Collections.Concurrent;
using System.Text;

public partial class DungeonMasterBot
{
    private static readonly ConcurrentDictionary<ulong, CancellationTokenSource> GameExpirationTimers = new();
    private static readonly ConcurrentDictionary<ulong, CancellationTokenSource> ChoiceTimeoutTimers = new();

    private static readonly ConcurrentDictionary<ulong, ulong> CurrentChoiceMessages = new();

    private static TimeSpan GetChoiceTimeoutDuration()
    {
        var raw =
            Environment.GetEnvironmentVariable("CHOICE_TIMEOUT_MINUTES");

        if (!int.TryParse(raw, out var minutes))
        {
            minutes = 2;
        }

        minutes = Math.Max(1, minutes);

        return TimeSpan.FromMinutes(minutes);
    }

    private static TimeSpan GetRemainingTime(GameSession game)
    {
        if (!game.StartedAt.HasValue)
            return TimeSpan.FromMinutes(game.DurationMinutes);

        var elapsed = DateTime.UtcNow - game.StartedAt.Value;
        var remaining = TimeSpan.FromMinutes(game.DurationMinutes) - elapsed;

        return remaining < TimeSpan.Zero
            ? TimeSpan.Zero
            : remaining;
    }

    private static void StartGameExpirationTimer(GameSession game)
    {
        CancelGameExpirationTimer(game.ChannelId);

        var remaining = GetRemainingTime(game);
        if (remaining <= TimeSpan.Zero)
        {
            _ = Task.Run(async () =>
            {
                await TryEndGameForTimeLimitAsync(game.ChannelId);
            });

            return;
        }

        var cts = new CancellationTokenSource();
        GameExpirationTimers[game.ChannelId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(remaining, cts.Token);
                await TryEndGameForTimeLimitAsync(game.ChannelId);
            }
            catch (TaskCanceledException)
            {
                // Game ended early or was reset.
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Timer error for channel {game.ChannelId}: {ex}");
            }
        });
    }

    private static void CancelGameExpirationTimer(ulong channelId)
    {
        if (GameExpirationTimers.TryRemove(channelId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static void StartChoiceTimeoutTimer(GameSession game)
    {
        CancelChoiceTimeoutTimer(game.ChannelId);

        if (game.Ended || !game.Started || game.Players.Count == 0)
        {
            return;
        }

        var turnNumber = game.Turn;
        var expectedPlayerId = GetCurrentTurnPlayerId(game);

        if (!expectedPlayerId.HasValue)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        ChoiceTimeoutTimers[game.ChannelId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(GetChoiceTimeoutDuration(), cts.Token);
                await HandleChoiceTimeoutAsync(
                    game.ChannelId,
                    turnNumber,
                    expectedPlayerId.Value);
            }
            catch (TaskCanceledException)
            {
                // A new turn started or the game ended.
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Choice timeout error for channel {game.ChannelId}: {ex}");
            }
        });
    }

    private static void CancelChoiceTimeoutTimer(ulong channelId)
    {
        if (ChoiceTimeoutTimers.TryRemove(channelId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static ulong? GetCurrentTurnPlayerId(GameSession game)
    {
        if (game.Players.Count == 0)
        {
            return null;
        }

        var index = game.Turn % game.Players.Count;
        return game.Players[index].PlayerId;
    }

    private static Player? GetCurrentTurnPlayer(GameSession game)
    {
        if (game.Players.Count == 0)
        {
            return null;
        }

        var index = game.Turn % game.Players.Count;
        return game.Players[index];
    }

    private static async Task HandleChoiceTimeoutAsync(
        ulong channelId,
        int expectedTurn,
        ulong expectedPlayerId)
    {
        if (!Games.TryGetValue(channelId, out var game))
        {
            CancelChoiceTimeoutTimer(channelId);
            return;
        }

        if (game.Ended || !game.Started || game.Turn != expectedTurn)
        {
            CancelChoiceTimeoutTimer(channelId);
            return;
        }

        var expectedPlayer =
            game.Players.FirstOrDefault(x => x.PlayerId == expectedPlayerId)
            ?? GetCurrentTurnPlayer(game);

        if (expectedPlayer == null)
        {
            CancelChoiceTimeoutTimer(channelId);
            return;
        }

        await ClearCurrentChoiceButtonsAsync(channelId);

        if (_discord.GetChannel(channelId) is not IMessageChannel channel)
        {
            CancelChoiceTimeoutTimer(channelId);
            return;
        }

        var timeoutNotice =
            game.Language == GameLanguage.Chinese
                ? $"⏱️ **{expectedPlayer.Username}** 选择超时，本回合自动跳过。"
                : $"⏱️ **{expectedPlayer.Username}** timed out. Skipping this turn.";

        await channel.SendMessageAsync(timeoutNotice);

        game.Turn++;
        game.CurrentScene =
            game.Language == GameLanguage.Chinese
                ? "当前回合无人选择，剧情继续向前推进。"
                : "No one chose in time, so the story moves on.";

        game.AdventureHistory.Add(
            $"Turn {game.Turn}: {expectedPlayer.Username} timed out and the turn was skipped.");

        SaveGame(game);

        var nextPlayer =
            GetCurrentTurnPlayer(game);

        if (nextPlayer == null)
        {
            CancelChoiceTimeoutTimer(channelId);
            return;
        }

        await GenerateChoices(
            channel,
            game,
            nextPlayer);
    }

    private static async Task ClearCurrentChoiceButtonsAsync(ulong channelId)
    {
        CancelChoiceTimeoutTimer(channelId);

        if (!CurrentChoiceMessages.TryRemove(channelId, out var messageId))
        {
            return;
        }

        if (_discord.GetChannel(channelId) is not IMessageChannel channel)
        {
            return;
        }

        try
        {
            var message = await channel.GetMessageAsync(messageId) as IUserMessage;
            if (message == null)
            {
                return;
            }

            await message.ModifyAsync(props =>
            {
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Failed to clear choice buttons for channel {channelId}: {ex.Message}");
        }
    }

    private static bool IsGameExpired(GameSession game)
    {
        return game.Ended || game.IsTimeLimitReached;
    }

    private static string GetStoryPhase(GameSession game)
    {
        var totalMinutes = Math.Max(1, game.DurationMinutes);
        var remainingMinutes = GetRemainingMinutes(game);
        var elapsedMinutes = totalMinutes - remainingMinutes;
        var progress = (double)elapsedMinutes / totalMinutes;

        if (progress >= 0.90)
            return "FINAL SCENE";

        if (progress >= 0.75)
            return "FINAL ARC";

        if (progress >= 0.50)
            return "CLIMAX PREPARATION";

        if (progress >= 0.20)
            return "MAIN ADVENTURE";

        return "OPENING / EXPLORATION";
    }

    private static string BuildTimeManagementGuidance(GameSession game)
    {
        var totalMinutes = Math.Max(1, game.DurationMinutes);
        var remainingMinutes = GetRemainingMinutes(game);
        var remainingRatio = (double)remainingMinutes / totalMinutes;

        if (remainingMinutes <= 0)
        {
            return "The adventure has reached its time limit. Conclude the story immediately and do not introduce any new plot threads.";
        }

        if (remainingRatio <= 0.10)
        {
            return "Only a few minutes remain. Resolve the main conflict now, close loose ends, and steer toward a definitive ending.";
        }

        if (remainingRatio <= 0.25)
        {
            return "The adventure is in its final stretch. Avoid new subplots, compress the pacing, and start wrapping up the central conflict.";
        }

        if (remainingRatio <= 0.50)
        {
            return "Time is getting tight. Keep the scene focused on the main conflict, raise urgency, and avoid detours.";
        }

        if (remainingRatio <= 0.75)
        {
            return "The adventure is moving into the middle game. Develop the core conflict and keep every scene purposeful.";
        }

        return "There is still time to explore, but introduce the central conflict early and keep momentum moving forward.";
    }

    private static async Task<bool> TryEndGameForTimeLimitAsync(ulong channelId)
    {
        if (!Games.TryGetValue(channelId, out var game))
        {
            CancelGameExpirationTimer(channelId);
            return false;
        }

        if (game.Ended || !game.Started || !game.IsTimeLimitReached)
        {
            CancelGameExpirationTimer(channelId);
            return false;
        }

        game.Ended = true;
        game.Started = false;

        CancelGameExpirationTimer(channelId);

        string closingText;

        try
        {
            closingText = await BuildTimeLimitConclusionAsync(game);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Time-limit conclusion error: {ex}");

            closingText = game.Language == GameLanguage.Chinese
                ? "时间到了。冒险在余韵中落幕，故事暂时收束于此。"
                : "Time is up. The adventure fades to a close, and the story settles here for now.";
        }

        if (string.IsNullOrWhiteSpace(closingText))
        {
            closingText = game.Language == GameLanguage.Chinese
                ? "时间到了。冒险在余韵中落幕，故事暂时收束于此。"
                : "Time is up. The adventure fades to a close, and the story settles here for now.";
        }

        game.CurrentScene = closingText;
        game.AdventureHistory.Add(
            $"Turn {game.Turn + 1}: {closingText}");

        SaveGame(game);
        await ClearCurrentChoiceButtonsAsync(channelId);
        Games.Remove(channelId);
        CurrentChoices.Remove(channelId);
        CancelChoiceTimeoutTimer(channelId);

        var suffix = game.Language == GameLanguage.Chinese
            ? "\n\n⏳ 冒险时间已结束。"
            : "\n\n⏳ The adventure time limit has been reached.";

        if (_discord.GetChannel(channelId) is IMessageChannel channel)
        {
            await SendNarrationAsync(
                channel,
                game,
                closingText + suffix);
        }

        return true;
    }

    private static async Task<string> BuildTimeLimitConclusionAsync(GameSession game)
    {
        var languageInstruction = game.Language == GameLanguage.Chinese
            ? "Write the entire response in Simplified Chinese."
            : "Write the entire response in English.";

        var history = game.AdventureHistory.Count > 0
            ? string.Join("\n\n", game.AdventureHistory.TakeLast(10))
            : "(No previous adventure history.)";

        var prompt =
            $"""
            You are the Dungeon Master bringing a time-limited adventure to a satisfying close.

            {languageInstruction}

            ADVENTURE HISTORY:
            {history}

            CURRENT SCENE:
            {game.CurrentScene}

            TARGET DURATION:
            {game.DurationMinutes} minutes

            REMAINING TIME:
            0 minutes

            STORY PHASE:
            FINAL SCENE

            TIME-LIMIT RULES:

            1. Resolve the main conflict or leave a strong dramatic ending.
            2. Do not introduce any new major characters, factions, or side quests.
            3. Do not ask the player to choose from new options.
            4. Keep the ending concise and evocative.
            5. Make the final scene feel complete even if it ends on a cliffhanger.

            Return ONLY the Dungeon Master narration.
            Do not include Markdown.
            """;

        var result = _openAI.CompleteChat(prompt);
        var narration = result.Value.Content[0].Text.Trim();

        if (string.IsNullOrWhiteSpace(narration))
        {
            narration = game.Language == GameLanguage.Chinese
                ? "时间到了。故事在关键时刻落幕，所有人的命运都悬在最终回响之中。"
                : "Time is up. The story ends at a decisive moment, leaving the fate of the party hanging in the final echo.";
        }

        return narration;
    }

}
