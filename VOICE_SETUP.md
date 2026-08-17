# Voice And TTS Setup

This bot reads configuration from `.env` in the project root.

## Required

- `DISCORD_BOT_TOKEN`
- `OPENAI_API_KEY`
- `OPENAI_MODEL`

## Optional TTS Settings

- `OPENAI_TTS_MODEL`
- `OPENAI_TTS_VOICE_ZH`
- `OPENAI_TTS_VOICE_EN`

## Defaults

- `OPENAI_TTS_MODEL` defaults to `tts-1-hd`
- Chinese narration defaults to `shimmer`
- English narration defaults to `alloy`

## How Voice Works

- `!voice join` joins the caller's current voice channel
- `!voice leave` disconnects the bot
- `!voice on` enables narration playback for the current game channel
- `!voice off` disables narration playback for the current game channel

Text narration is always sent to the Discord text channel.
If voice is enabled, the same narration is also queued and played in the voice channel.

## Notes

- Long narration is split into smaller speech chunks before TTS.
- Voice state is tracked per game text channel.
- The bot still needs Discord voice permissions to connect and speak.
