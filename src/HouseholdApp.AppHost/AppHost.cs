var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var googleClientId = builder.AddParameter("google-client-id", secret: true);
var googleClientSecret = builder.AddParameter("google-client-secret", secret: true);
var adminEmail = builder.AddParameter("admin-email", secret: false);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImageTag("18-alpine")
    .WithPgAdmin()
    .WithDataVolume("household-postgres-data")
    .WithEnvironment("POSTGRES_DB", "householdapp")
    .WithInitFiles("./init-sql");

var appDb = postgres.AddDatabase("householdapp");

var redis = builder.AddRedis("redis");

builder.AddProject<Projects.HouseholdApp_Web>("web")
    .WithReference(appDb)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WithHttpEndpoint(port: 9000, name: "http")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithEnvironment("Oidc__ClientId", googleClientId)
    .WithEnvironment("Oidc__ClientSecret", googleClientSecret)
    .WithEnvironment("Admins__0", adminEmail);

builder.Build().Run();
