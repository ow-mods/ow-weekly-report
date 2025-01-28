namespace OWWeeklyReport;

public class Database
{
	public Release[] releases { get; set; }
}

public class Release
{
	public string name { get; set; }
	public string uniqueName { get; set;}
	public string slug { get; set; }
	public int weeklyInstallCount { get; set; }
	public DateTime firstReleaseDate { get; set; }
}
