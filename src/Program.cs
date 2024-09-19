using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using System.Text;

namespace atrocity_bot
{
  static class Program
  {
    static double stoppingChance = .05;

    static string chainPath = "../resources/data.json";
    static string keysPath = "../resources/keys.json";

    static Dictionary<string, ChainEntry>? chain;
    static Dictionary<string, string> keyMap = new Dictionary<string, string>();

    static async Task Main()
    {
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
        await client.BulkOverwriteGlobalApplicationCommandsAsync(new[] {
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
        });
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

        var nextEntry = chain[nextToken];
        if (nextEntry == null)
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
