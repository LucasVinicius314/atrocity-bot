# atrocity-bot

A Discord user-install bot (works in servers, DMs and group DMs) with three features:

| Command | Type | What it does |
| --- | --- | --- |
| `/generate-atrocity` | slash | Generates a sentence from a Markov chain loaded from `resources/data.json`. |
| `/setup config-file:<json>` | slash | Uploads a new Markov chain JSON. |
| `/gpt prompt:<text>` / `/gpt-setup key:<key>` | slash | Asks GPT-4o using your own OpenAI API key (stored per user in `resources/keys.json`). |
| **Apps → transcribe** | message | Right-click (or long-press) a voice note / audio message and the bot replies with a local Whisper transcription. |

Only users listed in `WHITELISTED_USER_IDS` can use any command.

## Configuration

Copy `.env.example` to `.env` and fill it in (environment variables):

- `TOKEN` – Discord bot token.
- `WHITELISTED_USER_IDS` – comma separated Discord user ids allowed to use the bot.
- `WHISPER_MODEL` – optional, a Whisper.net `GgmlType` name (`Tiny`, `Base`, `Small`, `Medium`, `LargeV3`, ...). Defaults to `Base`. The model is downloaded to `resources/` on first use.

## Running

Requirements: .NET 8 SDK and [ffmpeg](https://ffmpeg.org/) on your `PATH` (used to decode voice notes).

```sh
dotnet run
```

The bot reads files from `../resources` relative to the working directory, so run it from a subfolder of the repo such as `src` (the VS Code launch config does this), or use Docker:

```sh
docker build -t atrocity-bot .
docker run --env-file .env -v atrocity-resources:/resources atrocity-bot
```

The Docker image already bundles ffmpeg. Mount `/resources` as a volume so the Markov data, API keys and downloaded Whisper model survive restarts.

## Transcription notes

- Whisper runs locally on CPU through [Whisper.net](https://github.com/sandrohanea/whisper.net); no audio leaves your machine. Language is auto-detected.
- Long transcripts are split across multiple messages (2000 char Discord limit). Audio over 25 MB is rejected.
- Bigger models are more accurate but slower and use more RAM. `Base` is a good default for a small VPS.
- The reply is sent as the response to the command, which Discord shows attached to the message you marked. Bots installed to a user cannot post a true message reply in channels they are not in.
