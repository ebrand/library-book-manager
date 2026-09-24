namespace LibraryBookManager.Api.Tests.Infrastructure;

public static class RepoPaths
{
    /// <summary>The api component root: the directory holding LibraryBookManager.sln.</summary>
    public static string ApiRoot { get; } = FindUp("LibraryBookManager.sln");

    private static string FindUp(string marker)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, marker))) return dir.FullName;
        throw new InvalidOperationException($"{marker} not found above {AppContext.BaseDirectory}");
    }
}
