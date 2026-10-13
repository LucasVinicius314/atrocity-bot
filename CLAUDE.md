# CLAUDE.md

Discord bot (C#, .NET 8, Discord.Net 3.15) with a Markov chain generator, a GPT command and local Whisper transcription.

## Layout

- `src/Program.cs` – entry point; registers commands in `Ready` and handles `SlashCommandExecuted` / `MessageCommandExecuted`. Static state: `chain`, `keyMap`.
- `src/Transcriber.cs` – ffmpeg (to 16kHz mono wav) + Whisper.net transcription. Lazily downloads the ggml model into `resources/`; serialised with a semaphore because whisper processors are not thread safe.
- `src/model/` – `ChainEntry` / `MatchCondition`: Markov chain JSON schema (weighted `conditions` ranges over `total`).
- `resources/` – runtime data, gitignored (`data.json` chain, `keys.json` per-user OpenAI keys, `ggml-*.bin` models). Paths are relative to the working directory (`../resources`).

## Commands

- Build: `dotnet build`
- Run: `dotnet run` from `src/` (as the VS Code launch config does) (so `../resources` resolves), env vars from `.env.example`.
- No tests exist.

## Conventions and gotchas

- 2-space indentation, namespace `atrocity_bot`, block-scoped namespace.
- All commands are user-install and gated by `WHITELISTED_USER_IDS`; keep that check for any new command.
- Interactions must be acknowledged within 3s: use `DeferAsync` for slow work (transcription).
- `/gpt` keys are stored in plaintext in `resources/keys.json` – never log or commit them.
- ffmpeg must be on `PATH` (installed in the Dockerfile).
- Whisper.net native runtime comes from the `Whisper.net.Runtime` package (CPU); bump Whisper.net and the runtime package versions together.
