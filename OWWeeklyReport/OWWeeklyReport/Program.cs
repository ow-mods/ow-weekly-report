using Discord;
using Discord.WebSocket;
using System;
using System.Diagnostics;
using System.Net;
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

			if (UnixTimeStampToDateTime(oldest.UnixTimestamp).DayOfWeek != fromTime.DayOfWeek && oldest.DownloadCount == 0)
			{
				numberOfNewMods++;
			}

			downloadChanges.Add(entry.Repo, newest.DownloadCount - oldest.DownloadCount);
		}

		return (numberOfNewMods, downloadChanges);
	}

	public async Task MainAsync(string[] args)
	{
		var discordToken = args[0];

		var downloadCountFile = "";
		using (var hc = new HttpClient())
		{
			downloadCountFile = await hc.GetStringAsync(@"https://ow-mods.github.io/ow-mod-download-history/download-history.json");
		}
		DownloadList = JsonSerializer.Deserialize<List<Entry>>(downloadCountFile);

		if (DownloadList == null)
		{
			return;
		}

		(var numberOfNewMods, var downloadChanges) = AnalyzeMods(DateTime.Now.AddDays(-7), DateTime.Now);
		(var numberOfNewModsLastWeek, var downloadChangesLastWeek) = AnalyzeMods(DateTime.Now.AddDays(-14), DateTime.Now.AddDays(-7));

		var modsToIgnore = new string[]
		{
			"https://github.com/ow-mods/ow-mod-manager",
			"https://github.com/ow-mods/owml",
			"https://github.com/amazingalek/owml"
		};

		var database = "";
		using (var hc = new HttpClient())
		{
			database = await hc.GetStringAsync(@"https://ow-mods.github.io/ow-mod-db/database.json");
		}
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

		var orderedTW = downloadChanges.Where(x => IsValidMod(x.Key)).OrderByDescending(x => x.Value);
		var topTenTW = orderedTW.Take(NUMBER_OF_MODS);

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

		var client = new DiscordSocketClient();
		client.Log += Log;
		await client.LoginAsync(Discord.TokenType.Bot, discordToken);
		await client.StartAsync();

		// send message
		client.Ready += async () =>
		{
			var emojiName = new string[] { ":one:", ":two:", ":three:", ":four:", ":five:", ":six:", ":seven:", ":eight:", ":nine:", ":keycap_ten:" };

			var guild = client.GetGuild(929708786027999262);
			var channel = guild.GetTextChannel(933149732077985792);
			void GenerateFieldValue(IEnumerable<(string repo, int change, int oldIndex)> list, EmbedBuilder eb)
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

					eb.AddField($"{rankingEmoji} {emojiName[i]} {modname}", $"+{change} downloads [Mod Page](https://outerwildsmods.com/mods/{slug}/)");
				}
			}

			List<Embed> embeds = new();

			var otherInfo = new EmbedBuilder
			{
				Title = "General Statistics",
				Color = Color.Orange,
				Description = $"<:newhere:1079777473585229875> New Mods : {numberOfNewMods}{Environment.NewLine}" +
				$"📋 Total Mods : {databaseJson.releases.GroupBy(x => x.uniqueName).Select(x => x.First()).Count()}"
			};
			embeds.Add(otherInfo.Build());

			var topTenBuilder = new EmbedBuilder
			{
				Title = "Most Downloads",
				Description = "This ranking does not include any mod tagged with `library`.",
				Color = Color.Orange
			};
			GenerateFieldValue(topTen, topTenBuilder);
			embeds.Add(topTenBuilder.Build());

			var from = DateTime.Now.Date.AddDays(-7).ToLongDateString();
			var to = DateTime.Now.ToLongDateString();

			await channel.SendMessageAsync($"Statistics from {from} to {to}. ({DateTime.Now.ToShortTimeString()})", embeds: embeds.ToArray());

			await client.LogoutAsync();

			Environment.Exit(0);
		};

		await Task.Delay(-1);
	}

	private Task Log(LogMessage msg)
	{
		Console.WriteLine(msg.ToString());
		return Task.CompletedTask;
	}

	public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
	{
		var dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
		dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
		return dateTime;
	}
}

public class Entry
{
	public string Repo { get; set; }
	public DownloadCountUpdate[] Updates { get; set; }
}

public class DownloadCountUpdate
{
	public long UnixTimestamp { get; set; }
	public int DownloadCount { get; set; }
}