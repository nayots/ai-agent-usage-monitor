using AiUsageMonitor.Infrastructure.Providers.Cursor;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class CursorExecutableLocatorTests
{
    [Fact]
    public void TheUserScopedInstallIsCheckedFirstBecauseThatIsWhereCursorActuallyInstalls()
    {
        IReadOnlyList<string> candidates = CursorExecutableLocator.CandidatePaths(
            localAppData: @"C:\Users\someone\AppData\Local",
            programFiles: @"C:\Program Files",
            pathEnvironment: null);

        Assert.Equal(@"C:\Users\someone\AppData\Local\Programs\cursor\Cursor.exe", candidates[0]);
        Assert.Equal(@"C:\Program Files\cursor\Cursor.exe", candidates[1]);
    }

    [Fact]
    public void PathEntriesContributeBothExecutableForms()
    {
        IReadOnlyList<string> candidates = CursorExecutableLocator.CandidatePaths(
            @"C:\local", @"C:\pf", $@"C:\tools{Path.PathSeparator}C:\other");

        Assert.Contains(@"C:\tools\Cursor.exe", candidates);
        Assert.Contains(@"C:\tools\cursor.cmd", candidates);
        Assert.Contains(@"C:\other\Cursor.exe", candidates);
    }

    [Fact]
    public void BlankPathEntriesAreSkipped()
    {
        IReadOnlyList<string> candidates = CursorExecutableLocator.CandidatePaths(
            @"C:\local", @"C:\pf", $"{Path.PathSeparator}   {Path.PathSeparator}");

        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void AVersionCannotBeReadFromAFileThatIsNotAnExecutable()
    {
        using var directory = new TempDirectory();
        string path = directory.File("Cursor.exe");
        File.WriteAllText(path, "not a real executable");

        Assert.Null(CursorExecutableLocator.TryReadVersion(path));
    }

    [Fact]
    public void AVersionCannotBeReadFromAMissingFile()
    {
        using var directory = new TempDirectory();

        Assert.Null(CursorExecutableLocator.TryReadVersion(directory.File("absent.exe")));
    }
}
