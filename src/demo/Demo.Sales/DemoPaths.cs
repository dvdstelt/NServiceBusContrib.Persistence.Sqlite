namespace Demo.Sales;

static class DemoPaths
{
    public static string EnsureDataDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nservicebuscontrib-sqlite-demo");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string TransportDirectory(string dataDir) => Path.Combine(dataDir, "transport");

    public static string SalesConnectionString(string dataDir) =>
        $"Data Source={Path.Combine(dataDir, "demo-sales.db")}";
}
