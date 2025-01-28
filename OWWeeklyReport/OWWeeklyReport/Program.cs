using CSharpDiscordWebhook.NET.Discord;
using System.Drawing;
using System.Text.Json;

namespace OWWeeklyReport;

public class Program
{
	public static void Main(string[] args)
	{
		Task.WaitAll(new Program().MainAsync(args));
	}

	public static async Task<string> DownloadFileAsync(string uri)
	{
		Console.WriteLine($"Downloading {uri}");
		var result = "";

		using (var hc = new HttpClient())
		{
			var success = false;
			var failCount = 0;
			while (!success)
			{
				try
				{
					result = await hc.GetStringAsync(uri);
					success = true;
				}
				catch
				{
					failCount++;
					Console.WriteLine($"- Attemp {failCount} failed.");

					if (failCount >= 5)
					{
						throw;
					}
				}
			}
		}

		return result;
	}

	public async Task MainAsync(string[] args)
	{
		var database = await DownloadFileAsync(@"https://ow-mods.github.io/ow-mod-db/database.json");
		var databaseJson = JsonSerializer.Deserialize<Database>(database);

		var topTenReleases = databaseJson.releases.OrderByDescending(x => x.weeklyInstallCount).Take(10);
		var topTen = new List<(string uniqueName, int change, int oldIndex)>();
		var newMods = new List<string>();

		var timeOfLastPost = DateTime.Now.ToUniversalTime().Date.AddDays(-7);

		if (File.Exists("previousWeek.json"))
		{
			var previous = JsonSerializer.Deserialize<PreviousWeek>(File.ReadAllText("previousWeek.json"));

			newMods = databaseJson.releases.Where(x => x.firstReleaseDate >= timeOfLastPost).Select(x => x.uniqueName).ToList();

			foreach (var item in topTenReleases)
			{
				var oldIndex = !previous.previousTopTen.Contains(item.uniqueName)
					? -1
					: Array.IndexOf(previous.previousTopTen, item.uniqueName);

				topTen.Add((item.uniqueName, item.weeklyInstallCount, oldIndex));
			}
		}
		else
		{
			foreach (var item in topTenReleases)
			{
				topTen.Add((item.uniqueName, item.weeklyInstallCount, -1));
			}
		}

		var thisWeekInfo = new PreviousWeek
		{
			previousTopTen = topTenReleases.Select(x => x.uniqueName).ToArray()
		};

		File.WriteAllText("previousWeek.json", JsonSerializer.Serialize(thisWeekInfo));

		Console.WriteLine("Setting up webhook...");
		var webhook = args[0];

		var hook = new DiscordWebhook
		{
			Uri = new Uri(webhook)
		};

		var message = new DiscordMessage();

		var emojiName = new string[] { ":one:", ":two:", ":three:", ":four:", ":five:", ":six:", ":seven:", ":eight:", ":nine:", ":keycap_ten:" };

		void GenerateFieldValue(IEnumerable<(string uniqueName, int change, int oldIndex)> list, DiscordEmbed eb)
		{
			for (var i = 0; i < 10; i++)
			{
				var uniqueName = list.ElementAt(i).uniqueName;
				var oldIndex = list.ElementAt(i).oldIndex;
				var modDBEntry = databaseJson.releases.First(y => y.uniqueName == uniqueName);

				var slug = modDBEntry.slug;
				var modname = modDBEntry.name;
				var change = list.ElementAt(i).change;

				var rankingEmoji = "";

				if (oldIndex == i)
				{
					rankingEmoji = "➖";
				}
				else if (oldIndex < i && oldIndex != -1)
				{
					rankingEmoji = "<:red_down:1080545078197637240>";
				}
				else
				{
					rankingEmoji = "<:green_up:1080545075005755543>";
				}

				eb.Fields.Add(new EmbedField()
				{
					Name = $"{rankingEmoji} {emojiName[i]} {modname}",
					Value = $"+{change} installs [Mod Page](https://outerwildsmods.com/mods/{slug}/)"
				});

				Console.WriteLine($"{modname} +{change}");
			}
		}

		var desc = "";

		if (newMods == null || newMods.Count == 0)
		{
			desc = $"No new mods this week :({Environment.NewLine}";
		}
		else
		{
			desc = $"<:newhere:1182397109006254120> New Mods:{Environment.NewLine}";
			foreach (var mod in newMods)
			{
				var modDBEntry = databaseJson.releases.First(y => y.uniqueName == mod);
				var slug = modDBEntry.slug;
				var modname = modDBEntry.name;

				desc += $" - {modname} [Mod Page](https://outerwildsmods.com/mods/{slug}/){Environment.NewLine}";
			}
		}

		desc += $"📋 Total Mods : {databaseJson.releases.GroupBy(x => x.uniqueName).Select(x => x.First()).Count()}";

		Console.WriteLine(desc);

		var otherInfoEmbed = new DiscordEmbed
		{
			Title = "General Statistics",
			Color = new DiscordColor(Color.Orange),
			Description = desc
		};

		message.Embeds.Add(otherInfoEmbed);

		var topTenEmbed = new DiscordEmbed
		{
			Title = "Most Installs",
			Description = "This ranking does not include mods downloaded for dependencies, or downloads from mod updates.",
			Color = new DiscordColor(Color.Orange)
		};

		GenerateFieldValue(topTen, topTenEmbed);

		message.Embeds.Add(topTenEmbed);

		var from = timeOfLastPost.ToLongDateString();
		var to = DateTime.Now.ToUniversalTime().ToLongDateString();

		message.Content = $"Statistics from {from} to {to}. ({DateTime.Now.ToUniversalTime().ToShortTimeString()})";

		Console.WriteLine("Sending webhook message...");
		await hook.SendAsync(message);
	}
}
