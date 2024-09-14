using System.Text;
using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;

namespace atrocity_bot
{
  static class Program
  {
    static Dictionary<string, ChainEntry>? chain;

    static async Task Main()
    {
      var whitelistedUserIds = (Environment.GetEnvironmentVariable("WHITELISTED_USER_IDS") ?? "").Split(",").ToList();

      chain = await LoadChain();

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
          case "setup":
            var attachment = command.Data.Options.FirstOrDefault(o => o.Type == ApplicationCommandOptionType.Attachment)?.Value as Attachment;

            if (attachment != null)
            {
              using (var httpClient = new HttpClient())
              {
                var fileContent = await httpClient.GetByteArrayAsync(attachment.Url);

                await File.WriteAllBytesAsync("../resources/data.json", fileContent);

                chain = await LoadChain();

                await command.RespondAsync("Setup done.", ephemeral: true);
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

      var stoppingChance = .05;

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

    static async Task<Dictionary<string, ChainEntry>?> LoadChain()
    {
      if (!File.Exists("../resources/data.json"))
      {
        return null;
      }

      var fileBytes = await File.ReadAllBytesAsync("../resources/data.json");
      return JsonConvert.DeserializeObject<Dictionary<string, ChainEntry>>(
        Encoding.UTF8.GetString(fileBytes)
      );
    }
  }
}
