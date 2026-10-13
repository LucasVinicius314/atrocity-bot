using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using System.Text;
using Whisper.net.Ggml;

namespace atrocity_bot
{
  static class Program
  {
    static double stoppingChance = .05;
    const int maxAudioBytes = 25 * 1024 * 1024;

    static string chainPath = "../resources/data.json";
    static string keysPath = "../resources/keys.json";

    static Dictionary<string, ChainEntry>? chain;
    static Dictionary<string, string> keyMap = new Dictionary<string, string>();

    static async Task Main()
    {
      var modelName = Environment.GetEnvironmentVariable("WHISPER_MODEL") ?? "Base";
      if (!Enum.TryParse<GgmlType>(modelName, true, out var modelType))
      {
        throw new ArgumentException($"Invalid WHISPER_MODEL '{modelName}'. Valid values: {string.Join(", ", Enum.GetNames<GgmlType>())}.");
      }

      var transcriber = new Transcriber($"../resources/ggml-{modelType.ToString().ToLowerInvariant()}.bin", modelType);

      var whitelistedUserIds = (Environment.GetEnvironmentVariable("WHITELISTED_USER_IDS") ?? "").Split(",").ToList();

      chain = await LoadChain();

      keyMap = await LoadKeyMap() ?? new Dictionary<string, string>();

      var client = new DiscordSocketClient(
        new DiscordSocketConfig
        {
          LogLevel = LogSeverity.Verbose,
        }
      );

      client.Log += async (msg) => await Task.Run(() =>
        {
          Console.WriteLine(msg.ToString());
          return Task.CompletedTask;
        }
      );

      await client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("TOKEN"));
      await client.StartAsync();

      client.Ready += async () =>
      {
        await client.BulkOverwriteGlobalApplicationCommandsAsync(new ApplicationCommandProperties[] {
          new SlashCommandBuilder()
            .WithName("generate-atrocity")
            .WithDescription("Generate Atrocity")
            .WithIntegrationTypes(ApplicationIntegrationType.UserInstall)
            .WithContextTypes(InteractionContextType.PrivateChannel, InteractionContextType.BotDm, InteractionContextType.Guild)
            .Build(),
          new SlashCommandBuilder()
            .WithName("gpt")
            .WithDescription("Ask GPT")
            .AddOption(name: "prompt", type: ApplicationCommandOptionType.String, description: "Prompt.")
            .WithIntegrationTypes(ApplicationIntegrationType.UserInstall)
            .WithContextTypes(InteractionContextType.PrivateChannel, InteractionContextType.BotDm, InteractionContextType.Guild)
            .Build(),
          new SlashCommandBuilder()
            .WithName("gpt-setup")
            .WithDescription("GPT Setup")
            .AddOption(name: "key", type: ApplicationCommandOptionType.String, description: "The openai api key.")
            .WithIntegrationTypes(ApplicationIntegrationType.UserInstall)
            .WithContextTypes(InteractionContextType.PrivateChannel, InteractionContextType.BotDm, InteractionContextType.Guild)
            .Build(),
          new SlashCommandBuilder()
            .WithName("setup")
            .WithDescription("Setup")
            .AddOption(name: "config-file", type: ApplicationCommandOptionType.Attachment, description: "The config json.")
            .WithIntegrationTypes(ApplicationIntegrationType.UserInstall)
            .WithContextTypes(InteractionContextType.PrivateChannel, InteractionContextType.BotDm, InteractionContextType.Guild)
            .Build(),
          new MessageCommandBuilder()
            .WithName("transcribe")
            .WithIntegrationTypes(ApplicationIntegrationType.UserInstall)
            .WithContextTypes(InteractionContextType.PrivateChannel, InteractionContextType.BotDm, InteractionContextType.Guild)
            .Build(),
        });
      };

      client.MessageCommandExecuted += async (command) =>
      {
        if (!whitelistedUserIds.Contains(command.User.Id.ToString()))
        {
          await command.RespondAsync("Not authorized.", ephemeral: true);
          return;
        }

        if (command.CommandName != "transcribe")
        {
          await command.RespondAsync("Invalid command.", ephemeral: true);
          return;
        }

        var audio = command.Data.Message.Attachments.FirstOrDefault(IsAudio);
        if (audio == null)
        {
          await command.RespondAsync("That message has no audio attachment.", ephemeral: true);
          return;
        }

        if (audio.Size > maxAudioBytes)
        {
          await command.RespondAsync($"Audio is too large (max {maxAudioBytes / 1024 / 1024} MB).", ephemeral: true);
          return;
        }

        // Transcribing can take longer than the 3 second interaction deadline.
        await command.DeferAsync();

        try
        {
          using var httpClient = new HttpClient();
          var text = await transcriber.TranscribeAsync(await httpClient.GetByteArrayAsync(audio.Url));

          // Posting as the interaction response keeps the transcript attached to the marked message
          // (Discord shows it as a reply to the command target) and works in user-install contexts.
          var chunks = ChunkText(text.Length == 0 ? "*(no speech detected)*" : text, 2000);

          await command.ModifyOriginalResponseAsync((a) => a.Content = chunks[0]);

          foreach (var chunk in chunks.Skip(1))
          {
            await command.FollowupAsync(chunk);
          }
        }
        catch (Exception e)
        {
          Console.WriteLine(e);

          await command.ModifyOriginalResponseAsync((a) => a.Content = "Transcription failed.");
        }
      };

      client.SlashCommandExecuted += async (command) =>
      {
        if (!whitelistedUserIds.Contains(command.User.Id.ToString()))
        {
          await command.RespondAsync("Not authorized.", ephemeral: true);
          return;
        }

        switch (command.CommandName)
        {
          case "generate-atrocity":
            if (chain == null)
            {
              await command.RespondAsync("Missing setup.", ephemeral: true);
              break;
            }

            await command.RespondAsync(GenerateOutput(chain));

            break;
          case "gpt":
            var prompt = command.Data.Options.FirstOrDefault(o => o.Type == ApplicationCommandOptionType.String)?.Value as string;
            if (prompt == null)
            {
              await command.RespondAsync("Missing prompt.", ephemeral: true);
              break;
            }

            if (!keyMap.ContainsKey(command.User.Id.ToString()))
            {
              await command.RespondAsync("Missing gpt-setup.", ephemeral: true);
              break;
            }

            await command.RespondAsync("Generating response.");

            var res = await TextCompletion(prompt, keyMap[command.User.Id.ToString()]);

            var embed = new EmbedBuilder()
                        .WithTitle(prompt)
                        .WithDescription(res.Content.First().Text)
                        .WithFooter($"Input: {res.Usage.InputTokens} tokens.\nOutput: {res.Usage.OutputTokens} tokens.\nTotal: {res.Usage.TotalTokens} tokens.")
                        .Build();

            await command.ModifyOriginalResponseAsync((a) =>
              {
                a.Embed = embed;
                a.Content = "";
              }
            );

            break;
          case "gpt-setup":
            var key = command.Data.Options.FirstOrDefault(o => o.Type == ApplicationCommandOptionType.String)?.Value as string;
            if (key == null)
            {
              await command.RespondAsync("Missing key.", ephemeral: true);
              break;
            }

            keyMap[command.User.Id.ToString()] = key;

            await SaveKeyMap(keyMap);

            await command.RespondAsync("Key set.", ephemeral: true);

            break;
          case "setup":
            var attachment = command.Data.Options.FirstOrDefault(o => o.Type == ApplicationCommandOptionType.Attachment)?.Value as Attachment;
            if (attachment != null)
            {
              using (var httpClient = new HttpClient())
              {
                await command.RespondAsync("Setup running.", ephemeral: true);

                await SaveChain(await httpClient.GetByteArrayAsync(attachment.Url));

                chain = await LoadChain();

                await command.ModifyOriginalResponseAsync((a) => a.Content = "Setup done.");
              }
            }

            break;
          default:
            await command.RespondAsync("Invalid command.", ephemeral: true);
            break;
        }
      };

      await Task.Delay(-1);
    }

    static bool IsAudio(IAttachment attachment)
    {
      return attachment.ContentType?.StartsWith("audio/") == true
        || attachment.ContentType?.StartsWith("video/") == true
        || attachment.Waveform != null;
    }

    static List<string> ChunkText(string text, int size)
    {
      var chunks = new List<string>();

      for (var i = 0; i < text.Length; i += size)
      {
        chunks.Add(text.Substring(i, Math.Min(size, text.Length - i)));
      }

      return chunks;
    }

    static string GenerateOutput(Dictionary<string, ChainEntry> chain)
    {
      var outValue = "";

      var keys = chain.Keys.ToList();

      var currentEntry = chain[keys[new Random().Next(keys.Count)]];

      while (true)
      {
        outValue = $"{outValue} {currentEntry.token}";

        var nextToken = currentEntry.PickNext();
        if (nextToken == "")
        {
          break;
        }

        if (!chain.TryGetValue(nextToken, out var nextEntry))
        {
          break;
        }

        currentEntry = nextEntry;

        if (new Random().NextDouble() < stoppingChance)
        {
          break;
        }
      }

      return outValue.Trim();
    }

    static async Task<OpenAI.Chat.ChatCompletion> TextCompletion(string prompt, string apiKey)
    {
      return (await new OpenAI.OpenAIClient(apiKey).GetChatClient("gpt-4o").CompleteChatAsync(prompt)).Value;
    }

    static async Task<Dictionary<string, ChainEntry>?> LoadChain()
    {
      if (!File.Exists(chainPath))
      {
        return null;
      }

      return JsonConvert.DeserializeObject<Dictionary<string, ChainEntry>>(
        Encoding.UTF8.GetString(await File.ReadAllBytesAsync(chainPath))
      );
    }

    static async Task<Dictionary<string, string>?> LoadKeyMap()
    {
      if (!File.Exists(keysPath))
      {
        return null;
      }

      return JsonConvert.DeserializeObject<Dictionary<string, string>>(
        Encoding.UTF8.GetString(await File.ReadAllBytesAsync(keysPath))
      );
    }

    static async Task SaveChain(byte[] fileContent)
    {
      await File.WriteAllBytesAsync(chainPath, fileContent);
    }

    static async Task SaveKeyMap(Dictionary<string, string> keyMap)
    {
      await File.WriteAllBytesAsync(keysPath, System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(keyMap)));
    }
  }
}
