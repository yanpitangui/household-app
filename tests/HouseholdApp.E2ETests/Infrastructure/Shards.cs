namespace HouseholdApp.E2ETests.Infrastructure;

// CI splits the suite across two jobs: classes tagged [Category(Shards.Second)] run in the second job and every other
// class runs in the first, so a new class always runs somewhere. Keep both halves roughly equal in total test time.
public static class Shards
{
    public const string Second = "E2EShard2";
}
