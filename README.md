# AI Dungeon Master

AI Dungeon Master is a Discord bot that runs a turn-based tabletop-style adventure with AI narration, player character setup, and optional voice narration.

It currently supports English and Chinese adventures, time-limited campaigns, button-based story choices, local campaign saving, and a Windows setup app that stores credentials locally.

## Kick Start AI Dungeon Master

1. Install the .NET 10 SDK.
2. Create a Discord bot in the Discord Developer Portal and copy its token.
3. Create an OpenAI API key.
4. Launch the Windows setup app and enter your Discord and OpenAI credentials.
5. Use the Test Configuration button to verify your setup.
6. Start the bot from the desktop app when you are ready.

## Requirements

- .NET 10 SDK
- A Discord bot application and token
- An OpenAI API key
- A Discord server where the bot can send messages
- Voice channel permissions if you want to use voice narration
- Windows is the tested target for the bundled native voice libraries and `.bat` launchers
- The desktop setup app stores credentials locally on the machine using secure Windows storage

## Configure Discord Bot Token

Enter your Discord token in the desktop setup app or save it locally in your own environment variables:

```env
DISCORD_BOT_TOKEN=
```

## Configure OpenAI API Key

Enter your OpenAI key in the desktop setup app or save it locally in your own environment variables:

```env
OPENAI_API_KEY=
```

Optional settings can also be configured locally if you want to override the defaults:

```env
DEFAULT_LANGUAGE=
OPENAI_MODEL=
CHOICE_TIMEOUT_MINUTES=
OPENAI_TTS_MODEL=
OPENAI_TTS_VOICE_ZH=
OPENAI_TTS_VOICE_EN=
```

## Run the Bot

From the project root, launch the Windows setup app:

```bash
dotnet run --project src/AIDungeonMaster.Desktop/AIDungeonMaster.Desktop.csproj
```

On Windows, you can also use:

```bat
start-desktop.bat
```

The original console bot is still available for development with:

```bash
dotnet run --project AIDungeonMaster.csproj
```

## Supported Commands

- `!game en [duration]` starts a new English adventure. Supported durations include `1m`, `2m`, `5m`, `15m`, `30m`, and `1h`.
- `!game cn [duration]` starts a new Chinese adventure. Supported durations include `1m`, `2m`, `5m`, `15m`, `30m`, and `1h`.
- `!join` joins the current adventure.
- `!character` begins character creation and expects `Name | Class`.
- `!start` starts the adventure after everyone has joined and created characters.
- `!status` shows the current game state, turn, timing, and voice status.
- `!help` lists the bot commands.
- `!reset` resets the current adventure. This is restricted to Discord server administrators.
- `!voice join` joins the caller's current voice channel.
- `!voice leave` disconnects the bot from the voice channel.
- `!voice on` enables voice narration for the current game.
- `!voice off` disables voice narration for the current game.
- `!tts on` and `!tts off` are legacy aliases for the voice narration toggle.

## Supported Features

- Turn-based story generation with AI narration
- Button-based choices for each turn
- Character creation with class-based ability assignment
- Adventure time limits
- Local campaign persistence in `data/`
- Optional OpenAI text-to-speech narration in voice channels

## Configuration Example

Use placeholders only in local config files:

```env
DISCORD_BOT_TOKEN=
OPENAI_API_KEY=
DEFAULT_LANGUAGE=
OPENAI_MODEL=
CHOICE_TIMEOUT_MINUTES=
OPENAI_TTS_MODEL=
OPENAI_TTS_VOICE_ZH=
OPENAI_TTS_VOICE_EN=
```

## Support the Project

If you enjoy the bot, donations or other support are always welcome and appreciated.

## What's Next

This project is still being actively improved. Future updates may expand the adventure flow, improve voice behavior, and refine the gameplay experience.

If you want to follow the project, please star the GitHub repository to stay up to date with future releases.

## Disclaimer

AI Dungeon Master is an independent AI-powered tabletop RPG project. It is not affiliated with or endorsed by Wizards of the Coast.
