using Discord;
using Discord.Audio;
using Discord.WebSocket;
using OpenAI.Audio;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Text;

public partial class DungeonMasterBot
{
    private sealed class VoiceRuntime
    {
        public VoiceRuntime(
            ulong voiceChannelId,
            IAudioClient audioClient)
        {
            VoiceChannelId = voiceChannelId;
            AudioClient = audioClient;
        }

        public ulong VoiceChannelId { get; }

        public IAudioClient AudioClient { get; }

        public SemaphoreSlim PlaybackLock { get; } = new(1, 1);

        public CancellationTokenSource Cancellation { get; } = new();

        public Task PlaybackChain { get; set; } = Task.CompletedTask;

        public object PlaybackChainLock { get; } = new();
    }

    private static readonly ConcurrentDictionary<ulong, VoiceRuntime> VoiceSessions = new();

    private static AudioClient? _speechClient;
    private static readonly object SpeechClientLock = new();

    private static async Task HandleVoiceCommand(
        SocketMessage message,
        string content)
    {
        if (message.Channel is not SocketTextChannel textChannel)
        {
            await message.Channel.SendMessageAsync(
                "❌ Voice commands only work in a text channel.");

            return;
        }

        if (message.Author is not SocketGuildUser user)
        {
            await message.Channel.SendMessageAsync(
                "❌ Voice commands only work inside a Discord server.");

            return;
        }

        if (!Games.TryGetValue(
            textChannel.Id,
            out var game))
        {
            await message.Channel.SendMessageAsync(
                game?.Language == GameLanguage.Chinese
                    ? "❌ 当前没有进行中的游戏。"
                    : "❌ No active game exists in this channel.");

            return;
        }

        var parts =
            content.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            await message.Channel.SendMessageAsync(
                game.Language == GameLanguage.Chinese
                    ? "使用方法：`!voice join`、`!voice leave`、`!voice on`、`!voice off`"
                    : "Usage: `!voice join`, `!voice leave`, `!voice on`, `!voice off`");

            return;
        }

        var action =
            parts[1].ToLowerInvariant();

        switch (action)
        {
            case "join":
                await JoinVoiceChannelAsync(
                    textChannel,
                    user,
                    game);
                return;

            case "leave":
                await LeaveVoiceChannelAsync(
                    textChannel,
                    game);
                return;

            case "on":
                await SetVoiceEnabledAsync(
                    textChannel,
                    user,
                    game,
                    true);
                return;

            case "off":
                await SetVoiceEnabledAsync(
                    textChannel,
                    user,
                    game,
                    false);
                return;

            default:
                await message.Channel.SendMessageAsync(
                    game.Language == GameLanguage.Chinese
                        ? "未知的 voice 命令。请使用 `!voice join`、`!voice leave`、`!voice on`、`!voice off`。"
                        : "Unknown voice command. Use `!voice join`, `!voice leave`, `!voice on`, or `!voice off`.");
                return;
        }
    }

    private static async Task HandleLegacyVoiceCommand(
        SocketMessage message,
        string content)
    {
        var parts =
            content.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            await message.Channel.SendMessageAsync(
                "❌ Usage: `!tts on` or `!tts off`.");

            return;
        }

        if (parts[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            await HandleVoiceCommand(
                message,
                "!voice on");
            return;
        }

        if (parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            await HandleVoiceCommand(
                message,
                "!voice off");
            return;
        }

        await message.Channel.SendMessageAsync(
            "❌ Usage: `!tts on` or `!tts off`.");
    }

    private static async Task SendNarrationAsync(
        IMessageChannel channel,
        GameSession game,
        string narration)
    {
        if (string.IsNullOrWhiteSpace(narration))
            return;

        await channel.SendMessageAsync(
            text: narration);

        QueueVoiceNarration(
            game,
            narration);
    }

    private static void QueueVoiceNarration(
        GameSession game,
        string narration)
    {
        if (!game.VoiceEnabled)
            return;

        if (!game.VoiceChannelId.HasValue)
            return;

        if (!VoiceSessions.TryGetValue(
            game.ChannelId,
            out var runtime))
        {
            return;
        }

        Task queuedTask;

        lock (runtime.PlaybackChainLock)
        {
            runtime.PlaybackChain =
                runtime.PlaybackChain
                    .ContinueWith(
                        _ => SpeakNarrationAsync(
                            game,
                            runtime,
                            narration),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default)
                    .Unwrap();

            queuedTask = runtime.PlaybackChain;
        }

        _ = queuedTask.ContinueWith(
            task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    Console.WriteLine(
                        $"Voice narration error: {task.Exception.GetBaseException()}");
                }
            },
            TaskContinuationOptions.ExecuteSynchronously);
    }

    private static async Task SpeakNarrationAsync(
        GameSession game,
        VoiceRuntime runtime,
        string narration)
    {
        if (!game.VoiceEnabled ||
            !game.VoiceChannelId.HasValue)
        {
            return;
        }

        if (runtime.Cancellation.IsCancellationRequested)
            return;

        await runtime.PlaybackLock.WaitAsync();

        try
        {
            if (runtime.Cancellation.IsCancellationRequested)
                return;

            await using var discordStream =
                runtime.AudioClient.CreatePCMStream(AudioApplication.Mixed);

            await runtime.AudioClient.SetSpeakingAsync(true);

            var speechText =
                PrepareSpeechText(
                    narration,
                    game.Language);

            var chunks =
                SplitNarrationForSpeech(speechText);

            foreach (var chunk in chunks)
            {
                if (!game.VoiceEnabled ||
                    !game.VoiceChannelId.HasValue)
                {
                    break;
                }

                if (runtime.Cancellation.IsCancellationRequested)
                    break;

                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                var tempFile =
                    await GenerateSpeechFileAsync(
                        chunk,
                        game.Language,
                        runtime.Cancellation.Token);

                try
                {
                    await PlaySpeechFileAsync(
                        discordStream,
                        tempFile,
                        runtime.Cancellation.Token);
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            }

            await discordStream.FlushAsync(
                runtime.Cancellation.Token);
        }
        finally
        {
            try
            {
                await runtime.AudioClient.SetSpeakingAsync(false);
            }
            finally
            {
                runtime.PlaybackLock.Release();
            }
        }
    }

    private static async Task JoinVoiceChannelAsync(
        SocketTextChannel textChannel,
        SocketGuildUser user,
        GameSession game)
    {
        var voiceChannel = user.VoiceChannel;

        if (voiceChannel == null)
        {
            await textChannel.SendMessageAsync(
                game.Language == GameLanguage.Chinese
                    ? "❌ 你现在不在任何语音频道里。请先加入一个 Voice Channel。"
                    : "❌ You are not in a voice channel. Join one first, then use `!voice join`.");
            return;
        }

        if (VoiceSessions.TryRemove(
            textChannel.Id,
            out var previousRuntime))
        {
            previousRuntime.Cancellation.Cancel();
            previousRuntime.AudioClient.Dispose();
        }

        try
        {
            var audioClient =
                await voiceChannel.ConnectAsync();

            VoiceSessions[textChannel.Id] =
                new VoiceRuntime(
                    voiceChannel.Id,
                    audioClient);

            game.VoiceChannelId = voiceChannel.Id;
            game.VoiceEnabled = false;
            SaveGame(game);

            await textChannel.SendMessageAsync(
                game.Language == GameLanguage.Chinese
                    ? $"✅ 已加入语音频道：**{voiceChannel.Name}**"
                    : $"✅ Joined voice channel: **{voiceChannel.Name}**");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Voice join error: {ex}");

            game.VoiceEnabled = false;
            game.VoiceChannelId = null;
            SaveGame(game);

            await textChannel.SendMessageAsync(
                game.Language == GameLanguage.Chinese
                    ? "❌ 无法加入语音频道，请确认 Bot 权限、语音频道可用。"
                    : "❌ Could not join the voice channel. Check bot permissions and voice channel availability.");
        }
    }

    private static async Task LeaveVoiceChannelAsync(
        SocketTextChannel textChannel,
        GameSession game)
    {
        if (!VoiceSessions.TryRemove(
            textChannel.Id,
            out var runtime))
        {
            if (!game.VoiceChannelId.HasValue)
            {
                await textChannel.SendMessageAsync(
                    game.Language == GameLanguage.Chinese
                        ? "❌ Bot 目前没有在这个游戏里加入任何语音频道。"
                        : "❌ The bot is not connected to a voice channel for this game.");
                return;
            }
        }
        else
        {
            runtime.Cancellation.Cancel();
            runtime.AudioClient.Dispose();
        }

        game.VoiceEnabled = false;
        game.VoiceChannelId = null;
        SaveGame(game);

        await textChannel.SendMessageAsync(
            game.Language == GameLanguage.Chinese
                ? "👋 已离开语音频道，语音叙事也已关闭。"
                : "👋 Left the voice channel and turned voice narration off.");
    }

    private static async Task SetVoiceEnabledAsync(
        SocketTextChannel textChannel,
        SocketGuildUser user,
        GameSession game,
        bool enabled)
    {
        if (enabled)
        {
            var runtimeExists =
                VoiceSessions.ContainsKey(textChannel.Id);

            if (!runtimeExists)
            {
                if (user.VoiceChannel == null)
                {
                    await textChannel.SendMessageAsync(
                        game.Language == GameLanguage.Chinese
                            ? "⚠️ 你现在不在任何语音频道里。请先加入一个 Voice Channel，再使用 `!voice on`。"
                            : "⚠️ You are not in a voice channel. Join one first, or use `!voice join`.");
                    return;
                }

                await JoinVoiceChannelAsync(
                    textChannel,
                    user,
                    game);

                runtimeExists =
                    VoiceSessions.ContainsKey(textChannel.Id);

                if (!runtimeExists)
                {
                    return;
                }
            }
        }

        game.VoiceEnabled = enabled;
        SaveGame(game);

        await textChannel.SendMessageAsync(
            enabled
                ? (game.Language == GameLanguage.Chinese
                    ? "🔊 语音旁白已开启。"
                    : "🔊 Voice narration is ON.")
                : (game.Language == GameLanguage.Chinese
                    ? "🔇 语音旁白已关闭。"
                    : "🔇 Voice narration is OFF."));
    }

    private static async Task EnsureVoiceAutoJoinAsync(
        SocketTextChannel textChannel,
        SocketGuildUser user,
        GameSession game)
    {
        if (game.VoiceEnabled &&
            VoiceSessions.ContainsKey(textChannel.Id))
        {
            return;
        }

        if (user.VoiceChannel == null)
        {
            return;
        }

        await JoinVoiceChannelAsync(
            textChannel,
            user,
            game);

        if (VoiceSessions.ContainsKey(textChannel.Id))
        {
            game.VoiceEnabled = true;
            SaveGame(game);

            await textChannel.SendMessageAsync(
                game.Language == GameLanguage.Chinese
                    ? "🔊 已自动开启语音旁白。"
                    : "🔊 Voice narration has been auto-enabled.");
        }
    }

    private static void ClearVoiceSession(
        ulong textChannelId)
    {
        if (!VoiceSessions.TryRemove(
            textChannelId,
            out var runtime))
        {
            return;
        }

        runtime.Cancellation.Cancel();
        runtime.AudioClient.Dispose();
    }
    private static IEnumerable<string> SplitNarrationForSpeech(string narration)
    {
        const int maxChunkLength = 240;

        var paragraphs =
            narration.Split(
                new[] { "\r\n\r\n", "\n\n" },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var paragraph in paragraphs)
        {
            foreach (var chunk in SplitParagraph(paragraph, maxChunkLength))
            {
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    yield return chunk.Trim();
                }
            }
        }
    }

    private static IEnumerable<string> SplitParagraph(
        string paragraph,
        int maxChunkLength)
    {
        if (paragraph.Length <= maxChunkLength)
        {
            yield return paragraph;
            yield break;
        }

        var sentencePieces =
            Regex.Split(
                paragraph,
                @"(?<=[\.\!\?\u3002\uff01\uff1f])\s+")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (sentencePieces.Count == 0)
        {
            foreach (var chunk in BreakLongText(paragraph, maxChunkLength))
                yield return chunk;

            yield break;
        }

        var builder = new StringBuilder();

        foreach (var sentence in sentencePieces)
        {
            if (sentence.Length > maxChunkLength)
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString().Trim();
                    builder.Clear();
                }

                foreach (var chunk in BreakLongText(sentence, maxChunkLength))
                {
                    yield return chunk;
                }

                continue;
            }

            if (builder.Length > 0 &&
                builder.Length + sentence.Length + 1 > maxChunkLength)
            {
                yield return builder.ToString().Trim();
                builder.Clear();
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(sentence.Trim());
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString().Trim();
        }
    }

    private static IEnumerable<string> BreakLongText(
        string text,
        int maxChunkLength)
    {
        for (var index = 0; index < text.Length; index += maxChunkLength)
        {
            yield return text.Substring(
                index,
                Math.Min(maxChunkLength, text.Length - index));
        }
    }

    private static async Task<string> GenerateSpeechFileAsync(
        string text,
        GameLanguage language,
        CancellationToken cancellationToken)
    {
        var client =
            GetSpeechClient();

        var voice =
            GetVoiceForLanguage(language);

        var options =
            new SpeechGenerationOptions
            {
#pragma warning disable OPENAI001
                ResponseFormat = GeneratedSpeechFormat.Wav,
                Instructions = GetSpeechInstructions(language)
#pragma warning restore OPENAI001
            };

        var result =
            await client.GenerateSpeechAsync(
                text,
                voice,
                options);

        var tempFile =
            Path.Combine(
                Path.GetTempPath(),
                $"aidm_tts_{Guid.NewGuid():N}.wav");

        await using var source =
            result.Value.ToStream();

        await using var destination =
            File.Create(tempFile);

        await source.CopyToAsync(
            destination,
            cancellationToken);

        return tempFile;
    }

    private static async Task PlaySpeechFileAsync(
        Stream discordStream,
        string audioFile,
        CancellationToken cancellationToken)
    {
        using var reader =
            new WaveFileReader(audioFile);

        using var resampler =
            new MediaFoundationResampler(
                reader,
                new WaveFormat(48000, 16, 2))
            {
                ResamplerQuality = 60
            };

        var buffer = new byte[81920];

        int bytesRead;
        while ((bytesRead = resampler.Read(
                   buffer,
                   0,
                   buffer.Length)) > 0)
        {
            await discordStream.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);
        }
    }

    private static AudioClient GetSpeechClient()
    {
        lock (SpeechClientLock)
        {
            _speechClient ??=
                new AudioClient(
                    Environment.GetEnvironmentVariable("OPENAI_TTS_MODEL")
                    ?? "tts-1-hd",
                    Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? throw new InvalidOperationException(
                        "OPENAI_API_KEY is missing."));

            return _speechClient;
        }
    }

    private static GeneratedSpeechVoice GetVoiceForLanguage(
        GameLanguage language)
    {
        var envVoice =
            language == GameLanguage.Chinese
                ? Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE_ZH")
                : Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE_EN");

        if (!string.IsNullOrWhiteSpace(envVoice) &&
            TryGetVoiceByName(
                envVoice,
                out var parsedVoice))
        {
            return parsedVoice;
        }

        return language == GameLanguage.Chinese
            ? GeneratedSpeechVoice.Shimmer
            : GeneratedSpeechVoice.Alloy;
    }

    private static bool TryGetVoiceByName(
        string voiceName,
        out GeneratedSpeechVoice voice)
    {
        switch (voiceName.Trim().ToLowerInvariant())
        {
            case "alloy":
                voice = GeneratedSpeechVoice.Alloy;
                return true;

            case "shimmer":
                voice = GeneratedSpeechVoice.Shimmer;
                return true;

            default:
                voice = default!;
                return false;
        }
    }

    private static string GetSpeechInstructions(GameLanguage language)
    {
        return language == GameLanguage.Chinese
            ? "请用自然、清晰、沉稳的简体中文朗读。若文本里出现英文缩写、数字代号或专有名词，请尽量用中文说法表达，不要逐字母拼读英文。"
            : "Please speak in natural, clear, steady English.";
    }

    private static string PrepareSpeechText(
        string text,
        GameLanguage language)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Trim();

        if (language != GameLanguage.Chinese)
        {
            return normalized;
        }

        normalized =
            Regex.Replace(
                normalized,
                @"[#*_`>~]",
                string.Empty);

        normalized =
            normalized.Replace("**", string.Empty);

        var replacements =
            new (string Pattern, string Replacement)[]
            {
                (@"\bD20\b", "二十面骰"),
                (@"\bDC\b", "难度等级"),
                (@"\bHP\b", "生命值"),
                (@"\bSTR\b", "力量"),
                (@"\bDEX\b", "敏捷"),
                (@"\bCON\b", "体质"),
                (@"\bINT\b", "智力"),
                (@"\bWIS\b", "感知"),
                (@"\bCHA\b", "魅力"),
                (@"\bNPC\b", "非玩家角色"),
                (@"\bDM\b", "地下城主")
            };

        foreach (var (pattern, replacement) in replacements)
        {
            normalized =
                Regex.Replace(
                    normalized,
                    pattern,
                    replacement,
                    RegexOptions.IgnoreCase);
        }

        normalized =
            Regex.Replace(
                normalized,
                @"\s+",
                " "
            );

        return normalized.Trim();
    }
}
