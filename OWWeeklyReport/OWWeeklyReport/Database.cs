namespace OWWeeklyReport;

public class Database
{
	public Release[] releases { get; set; }
}

public class Release
{
	public string name { get; set; }
	public string uniqueName { get; set;}
	public string repo { get; set; }
	public int downloadCount { get; set; }
	public string slug { get; set; }
	public string[] tags { get; set; }
	public int weeklyInstallCount { get; set; }
}
