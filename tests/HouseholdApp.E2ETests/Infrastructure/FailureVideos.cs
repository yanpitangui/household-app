namespace HouseholdApp.E2ETests.Infrastructure;

/// <summary>
/// With E2E_VIDEO_DIR set (CI does), every browser context records a video into one folder per test, and the folders
/// of tests that pass are deleted afterwards, so the folder ends up holding only the failures to look at.
/// </summary>
public static class FailureVideos
{
    private const string StateKey = "e2e-video-dir";

    private static readonly string? Root =
        Environment.GetEnvironmentVariable("E2E_VIDEO_DIR") is { Length: > 0 } dir ? dir : null;

    public static string? DirectoryForCurrentTest()
    {
        if (Root is null || TestContext.Current is not { } test) return null;
        var name = string.Concat(test.Metadata.TestName.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '-'));
        return test.StateBag.GetOrAdd<string>(StateKey, _ => Path.Combine(Root, $"{name}-{Guid.NewGuid().ToString("N")[..8]}"));
    }

    [AfterEvery(HookType.Test)]
    public static void DeletePassedTestVideos(TestContext context)
    {
        if (context.Execution.Result?.State != TestState.Passed) return;
        if (context.StateBag.TryGetValue<string>(StateKey, out var path) && Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
