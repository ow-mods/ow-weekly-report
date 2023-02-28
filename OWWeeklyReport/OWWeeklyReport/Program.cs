using System.Diagnostics;
using System.Text.Json;

namespace OWWeeklyReport;

internal class Program
{
	static void Main(string[] args)
	{
		//var downloadCountFile = File.ReadAllText(args[0]);
		var downloadCountFile = File.ReadAllText(@"C:\Users\hpoin\Downloads\download-history.json");
		var json = JsonSerializer.Deserialize<List<Entry>>(downloadCountFile);

		if (json == null)
		{
			return;
		}

		var currentTime = DateTime.Now;



		foreach (var entry in json)
		{
			if (entry.Repo == "IGNORE_ENTRY")
			{
				continue;
			}

			if (entry.Updates == null || entry.Updates.Length == 0)
			{
				continue;
			}

			var orderedByTimestamp = entry.Updates.OrderByDescending(x => x.UnixTimestamp);

			var latestDownloadCount = orderedByTimestamp.First().DownloadCount;

			var oldestDownloadCount = 0;

			if ((currentTime - UnixTimeStampToDateTime(orderedByTimestamp.Last().UnixTimestamp)).TotalDays > 7)
			{
				oldestDownloadCount = orderedByTimestamp.Last(x => (currentTime - UnixTimeStampToDateTime(x.UnixTimestamp)).TotalDays <= 7).DownloadCount;
			}

			Console.WriteLine($"{entry.Repo} went from {oldestDownloadCount} to {latestDownloadCount}");
		}
	}

	public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
	{
		// Unix timestamp is seconds past epoch
		DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
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
