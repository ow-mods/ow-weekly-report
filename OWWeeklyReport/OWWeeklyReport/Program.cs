using CSharpDiscordWebhook.NET.Discord;
using System.Drawing;
using System.Text.Json;

namespace OWWeeklyReport;

public class Program
{
	public const int NUMBER_OF_MODS = 10;

	public static void Main(string[] args)
	{
		Task.WaitAll(new Program().MainAsync(args));
	}

	public List<Entry> DownloadList = new();

	public (int, Dictionary<string, int>) AnalyzeMods(DateTime fromTime, DateTime toTime)
	{
		Dictionary<string, int> downloadChanges = new();
		var numberOfNewMods = 0;

		foreach (var entry in DownloadList)
		{
			if (entry.Repo == "IGNORE_ENTRY"
				|| entry.Updates == null
				|| entry.Updates.Length == 0)
			{
				continue;
			}

			var onlyInTimeSpan = entry.Updates.Where(x => UnixTimeStampToDateTime(x.UnixTimestamp) >= fromTime && UnixTimeStampToDateTime(x.UnixTimestamp) <= toTime);

			if (!onlyInTimeSpan.Any())
			{
				continue;
			}

			var updatesBeforeTimeSpan = entry.Updates.Any(x => UnixTimeStampToDateTime(x.UnixTimestamp) < fromTime);

			if (!updatesBeforeTimeSpan)
			{
				numberOfNewMods++;
			}

			var ordered = onlyInTimeSpan.OrderByDescending(x => x.UnixTimestamp).ToList();

			// filter outliers

			List<DownloadCountUpdate> newOrdered = new();

			var orderedByDownloadCount = ordered.OrderBy(x => x.DownloadCount).ToList();

			float Quartile(List<int> orderedData, int whichQuartile)
			{
				var k = (int)MathF.Truncate(whichQuartile * (orderedData.Count + 1) / 4);

				var alpha = (whichQuartile * (orderedData.Count + 1) / 4) - MathF.Truncate(whichQuartile * (orderedData.Count + 1) / 4);

				// indexes have had 1 subtracted from original formula because computers
				return orderedData[k - 1] + (alpha * (orderedData[k] - orderedData[k - 1]));
			}

			if (orderedByDownloadCount.Count >= 4)
			{
				var downloadCounts = orderedByDownloadCount.Select(x => x.DownloadCount).ToList();
				var q3 = Quartile(downloadCounts, 3);
				var q1 = Quartile(downloadCounts, 1);
				var interQuartileRange = q3 - q1;
				var upperFence = q3 + (1.5f * interQuartileRange);
				var lowerFence = q1 - (1.5f * interQuartileRange);

				bool IsOutlier(int x) => x < lowerFence || x > upperFence;

				foreach (var item in ordered)
				{
					if (!IsOutlier(item.DownloadCount))
					{
						newOrdered.Add(item);
					}
				}
			}
			else
			{
				newOrdered = ordered;
			}

			var newest = newOrdered.First();
			var oldest = newOrdered.Last();

			downloadChanges.Add(entry.Repo, newest.DownloadCount - oldest.DownloadCount);
		}

		return (numberOfNewMods, downloadChanges);
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
		var downloadCountFile = await DownloadFileAsync(@"https://ow-mods.github.io/ow-mod-download-history/download-history.json");
		DownloadList = JsonSerializer.Deserialize<List<Entry>>(downloadCountFile);

		if (DownloadList == null)
		{
			return;
		}

		Console.WriteLine("Analyze this weeks mods...");
		(var numberOfNewMods, var downloadChanges) = AnalyzeMods(DateTime.Now.AddDays(-7), DateTime.Now);
		Console.WriteLine("Analyze last weeks mods...");
		(var numberOfNewModsLastWeek, var downloadChangesLastWeek) = AnalyzeMods(DateTime.Now.AddDays(-14), DateTime.Now.AddDays(-7));

		var modsToIgnore = new string[]
		{
			"https://github.com/ow-mods/ow-mod-manager",
			"https://github.com/ow-mods/owml",
			"https://github.com/amazingalek/owml"
		};

		var database = await DownloadFileAsync(@"https://ow-mods.github.io/ow-mod-db/database.json");
		var databaseJson = JsonSerializer.Deserialize<Database>(database);

		bool IsValidMod(string repo)
		{
			if (modsToIgnore.Contains(repo))
			{
				return false;
			}

			var modDbEntry = databaseJson.releases.FirstOrDefault(x => x.repo == repo);

			if (modDbEntry == null)
			{
				// :HUHH:
				return false;
			}

			if (modDbEntry.tags.Contains("library"))
			{
				return false;
			}

			return true;
		}

		Console.WriteLine("Selecting top 10 of this weeks mods");
		var orderedTW = downloadChanges.Where(x => IsValidMod(x.Key)).OrderByDescending(x => x.Value);
		var topTenTW = orderedTW.Take(NUMBER_OF_MODS);

		Console.WriteLine("Selecting top 10 of last weeks mods");
		var orderedLW = downloadChangesLastWeek.Where(x => IsValidMod(x.Key)).OrderByDescending(x => x.Value);
		var topTenLW = orderedLW.Take(NUMBER_OF_MODS);

		var topTenLWRepos = topTenLW.Select(x => x.Key).ToList();

		var topTen = new List<(string repo, int change, int oldIndex)>();
		for (var i = 0; i < topTenTW.Count(); i++)
		{
			var repo = topTenTW.ElementAt(i).Key;

			var oldIndex = -1;
			if (topTenLW.Any(x => x.Key == repo))
			{
				oldIndex = topTenLWRepos.IndexOf(repo);
			}

			topTen.Add((repo, topTenTW.ElementAt(i).Value, oldIndex));
		}

		Console.WriteLine("Setting up webhook...");
		var webhook = args[0];

		var hook = new DiscordWebhook
		{
			Uri = new Uri(webhook)
		};

		var message = new DiscordMessage();

		var emojiName = new string[] { ":one:", ":two:", ":three:", ":four:", ":five:", ":six:", ":seven:", ":eight:", ":nine:", ":keycap_ten:" };

		void GenerateFieldValue(IEnumerable<(string repo, int change, int oldIndex)> list, DiscordEmbed eb)
		{
			for (var i = 0; i < 10; i++)
			{
				var repo = list.ElementAt(i).repo;
				var oldIndex = list.ElementAt(i).oldIndex;
				var modDBEntry = databaseJson.releases.First(y => y.repo == repo);

				var slug = modDBEntry.slug;
				var modname = modDBEntry.name;
				var change = list.ElementAt(i).change;
				var uniqueName = modDBEntry.uniqueName;

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
					Value = $"+{change} downloads [Mod Page](https://outerwildsmods.com/mods/{slug}/)"
				});
			}
		}

		var otherInfoEmbed = new DiscordEmbed
		{
			Title = "General Statistics",
			Color = new DiscordColor(Color.Orange),
			Description = $"<:newhere:1079777473585229875> New Mods : {numberOfNewMods}{Environment.NewLine}" +
				$"📋 Total Mods : {databaseJson.releases.GroupBy(x => x.uniqueName).Select(x => x.First()).Count()}"
		};

		message.Embeds.Add(otherInfoEmbed);

		var topTenEmbed = new DiscordEmbed
		{
			Title = "Most Downloads",
			Description = "This ranking does not include any mod tagged with `library`.",
			Color = new DiscordColor(Color.Orange)
		};

		GenerateFieldValue(topTen, topTenEmbed);

		message.Embeds.Add(topTenEmbed);

		var from = DateTime.Now.Date.AddDays(-7).ToLongDateString();
		var to = DateTime.Now.ToLongDateString();

		message.Content = $"Statistics from {from} to {to}. ({DateTime.Now.ToShortTimeString()})";

		Console.WriteLine("Sending webhook message...");
		await hook.SendAsync(message);
	}

	public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
	{
		var dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
		dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
		return dateTime;
	}
}