using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using TUnit.Aspire;
using TUnit.Core;

namespace HouseholdApp.E2ETests.Infrastructure;

public class AppFixture : AspireFixture<Projects.HouseholdApp_AppHost>
{
    [ClassDataSource<FakeOidcFixture>(Shared = SharedType.PerTestSession)]
    public required FakeOidcFixture FakeOidc { get; init; }

    protected override ResourceWaitBehavior WaitBehavior => ResourceWaitBehavior.Named;
    protected override IEnumerable<string> ResourcesToWaitFor() => ["web"];
    protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(3);

    protected override IEnumerable<string> ResourcesToRemove()
        => ["pgadmin"];

    // Disable TUnit.Aspire's OTLP telemetry receiver — when enabled it injects
    // OTEL_EXPORTER_OTLP_ENDPOINT which triggers Polly/ILoggerFactory circular dep.
    protected override bool EnableTelemetryCollection => false;

    public override async Task InitializeAsync()
    {
        // Prepend dotnet's directory to PATH so the Aspire DCP can find it when
        // starting the web project subprocess (dotnet may be in a non-standard path).
        var dotnetExe = Environment.ProcessPath;
        if (dotnetExe is not null)
        {
            var dotnetDir = Path.GetDirectoryName(dotnetExe)!;
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!currentPath.Split(':').Any(p => string.Equals(p, dotnetDir, StringComparison.OrdinalIgnoreCase)))
                Environment.SetEnvironmentVariable("PATH", $"{dotnetDir}:{currentPath}");
        }

        // Disable SSL for all Npgsql connections in this process and any subprocess
        // it spawns (web, DCP). The local Postgres Docker container has no SSL cert;
        // Npgsql 10.x drops the connection during SSL negotiation by default.
        Environment.SetEnvironmentVariable("PGSSLMODE", "disable");

        await base.InitializeAsync();
    }

    protected override void ConfigureBuilder(IDistributedApplicationTestingBuilder builder)
    {
        // Supply dummy values for parameters that come from user secrets in local dev.
        // The web resource's actual OIDC env vars are overridden below with FakeOidc values.
        builder.Configuration["Parameters:google-client-id"] = "test-client-id";
        builder.Configuration["Parameters:google-client-secret"] = "test-client-secret";
        builder.Configuration["Parameters:admin-email"] = "test@example.com";

        // Strip named volume mounts so each test run starts with a clean DB state.
        foreach (var resource in builder.Resources)
        {
            var volumes = resource.Annotations
                .OfType<ContainerMountAnnotation>()
                .Where(m => m.Type == ContainerMountType.Volume)
                .ToList();
            foreach (var v in volumes)
                resource.Annotations.Remove(v);
        }

        var web = builder.Resources.OfType<ProjectResource>().First(r => r.Name == "web");

        // Remove the HTTPS endpoint so UseHttpsRedirection() has nothing to redirect
        // to and Playwright can communicate over plain HTTP (no dev-cert needed).
        var httpsEndpoints = web.Annotations
            .OfType<EndpointAnnotation>()
            .Where(e => e.UriScheme == "https")
            .ToList();
        foreach (var e in httpsEndpoints)
            web.Annotations.Remove(e);

        // Append SslMode=Disable to every Postgres connection string the web process
        // receives. Npgsql 10.x defaults to SslMode=Require; the local Docker Postgres
        // container has no SSL cert so it drops the SSL negotiation → EndOfStreamException.
        // Using ReferenceExpression.Create preserves the dynamically-allocated host/port
        // while appending the SSL override at resolve-time.
        var appDb = (IResourceWithConnectionString)builder.Resources.First(r => r.Name == "householdapp");
        var pg    = (IResourceWithConnectionString)builder.Resources.First(r => r.Name == "postgres");

        builder.CreateResourceBuilder(web)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("Oidc__Authority", FakeOidc.Url)
            .WithEnvironment("Oidc__ClientId", "household-app")
            .WithEnvironment("Oidc__ClientSecret", "test-secret")
            .WithEnvironment("ConnectionStrings__householdapp",
                ReferenceExpression.Create($"{appDb.ConnectionStringExpression};SslMode=Disable"))
            .WithEnvironment("ConnectionStrings__postgres",
                ReferenceExpression.Create($"{pg.ConnectionStringExpression};SslMode=Disable"));
    }
}
