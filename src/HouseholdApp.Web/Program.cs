using HouseholdApp.Application.Modules.Expenses;
using HouseholdApp.Application.Modules.Households;
using HouseholdApp.Application.Modules.Identity;
using HouseholdApp.Application.Modules.Identity.Application.Ports;
using HouseholdApp.Application.Shared.Authorization;
using HouseholdApp.Application.Shared.Identity;
using HouseholdApp.Web.Shared.Web;
using HouseholdApp.Application.Modules.Recipes;
using HouseholdApp.Application.Modules.Lists;
using HouseholdApp.Application.Modules.Tasks;
using HouseholdApp.Application.Shared.Events;
using HouseholdApp.Application.Shared.Persistence;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using StackExchange.Redis;
using TickerQ.Caching.StackExchangeRedis.DependencyInjection;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using Valtuutus.Core.Configuration;
using Valtuutus.Data.Postgres;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddRazorPages(options =>
{
    options.Conventions.Add(new KebabCasePageRouteModelConvention());
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/NotFound");
    options.Conventions.AllowAnonymousToPage("/SetCulture");
}).AddViewLocalization();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddEventBus();
builder.Services.AddIdentityModule();
builder.Services.AddHouseholdsModule();
builder.Services.AddListsModule();
builder.Services.AddTasksModule();
builder.Services.AddRecipesModule();

var connStr = builder.Configuration.GetConnectionString("householdapp")
    ?? throw new InvalidOperationException("Missing connection string 'householdapp'");
builder.Services.AddExpensesModule(connStr);

var redisConnStr = builder.Configuration.GetConnectionString("redis")
    ?? throw new InvalidOperationException("Missing connection string 'redis'");
var redisOptions = ConfigurationOptions.Parse(redisConnStr);
redisOptions.AbortOnConnectFail = false;
var redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisOptions);

builder.Services.AddTickerQ(options => options
    .AddStackExchangeRedis(redis => redis.ConnectionMultiplexer = redisMultiplexer)
    .AddDashboard(dashboard => dashboard.WithHostAuthentication("TickerQAdmin")));

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        options.Authority = builder.Configuration["Oidc:Authority"];
        options.ClientId = builder.Configuration["Oidc:ClientId"];
        options.ClientSecret = builder.Configuration["Oidc:ClientSecret"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.ResponseType = "code";
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.GetClaimsFromUserInfoEndpoint = true;
        options.ClaimActions.MapJsonKey("picture", "picture");
        options.SaveTokens = true;
        options.MapInboundClaims = false;

        options.Events ??= new OpenIdConnectEvents();
        options.Events.OnRedirectToIdentityProvider = context =>
        {
            if (!builder.Environment.IsDevelopment()
                && context.ProtocolMessage.RedirectUri.StartsWith("http:", StringComparison.OrdinalIgnoreCase))
            {
                var uriBuilder = new UriBuilder(context.ProtocolMessage.RedirectUri)
                {
                    Scheme = "https",
                    Port = -1
                };
                context.ProtocolMessage.RedirectUri = uriBuilder.ToString();
            }
            return Task.CompletedTask;
        };
        options.Events.OnTokenValidated = async context =>
        {
            var provisioning = context.HttpContext.RequestServices.GetRequiredService<IUserProvisioning>();
            var userQuery = context.HttpContext.RequestServices.GetRequiredService<IUserQuery>();
            var principal = context.Principal;
            var subject = principal?.FindFirst("sub")?.Value;
            if (subject is null) return;

            var email = principal?.FindFirst("email")?.Value ?? string.Empty;
            var displayName = principal?.FindFirst("name")?.Value
                ?? principal?.FindFirst("preferred_username")?.Value
                ?? email;
            var pictureUrl = principal?.FindFirst("picture")?.Value;

            await provisioning.ProvisionAsync(subject, email, displayName, pictureUrl, context.HttpContext.RequestAborted);

            var user = await userQuery.GetBySubjectAsync(subject, context.HttpContext.RequestAborted);
            if (user is not null)
            {
                var identity = (ClaimsIdentity)principal!.Identity!;
                identity.AddClaim(new Claim("app_uid", user.Id.ToString()));
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TickerQAdmin", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
              {
                  var email = ctx.User.FindFirstValue("email");
                  var admins = builder.Configuration.GetSection("Admins").Get<string[]>() ?? [];
                  return admins.Contains(email, StringComparer.OrdinalIgnoreCase);
              }));
});

builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.AddNpgsqlDataSource("householdapp");
builder.Services.AddPersistence();

var schemaStream = typeof(HouseholdsModule).Assembly
    .GetManifestResourceStream("HouseholdApp.Application.Shared.Authorization.schema.vtt")!;
builder.Services.AddValtuutusCore(schemaStream);
builder.Services.AddPostgres(
    _ => () => new NpgsqlConnection(connStr),
    new ValtuutusPostgresOptions("authz", "transactions", "relation_tuples", "attributes"));
builder.Services.AddScoped<IHouseholdGuard, ValtuutusHouseholdGuard>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.MapDefaultEndpoints();

var supportedCultures = new[] { "en", "pt-BR" };
app.UseRequestLocalization(options =>
{
    options.SetDefaultCulture("en")
           .AddSupportedCultures(supportedCultures)
           .AddSupportedUICultures(supportedCultures);
    options.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider(),
    ];
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseTickerQ();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapGet("/login", (HttpContext ctx, string? returnUrl) =>
{
    var props = new AuthenticationProperties { RedirectUri = returnUrl ?? "/" };
    return Results.Challenge(props, [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.Run();
