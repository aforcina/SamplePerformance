using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Puma.MDE.Trader.WebUI.Components;
using Puma.MDE.Trader.WebUI.Services;
using Radzen;
using Serilog;
using System.Security.Claims;


Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();


var builder = WebApplication.CreateBuilder(args);

//Add logging
builder.Host.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    );

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRadzenComponents();

// Authentication & Authorization Services
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "PumaMDE.Trader.Auth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

// Background Poller & In-Memory Cache Services
builder.Services.Configure<PubRequestCacheOptions>(builder.Configuration.GetSection(PubRequestCacheOptions.SectionName));
builder.Services.AddSingleton<IPubRequestCache, PubRequestCache>();
builder.Services.AddHostedService<PubRequestPollerHostedService>();

// Market Data Repositories & Services (EQMX.MarketDataService.Repository)
builder.Services.AddSingleton<EQMX.MarketDataService.Repository.RepositoryConnectionFactory>(sp =>
{
    var factory = sp.GetRequiredService<IDbConnectionFactory>();
    return new EQMX.MarketDataService.Repository.RepositoryConnectionFactory(factory.ConnectionString);
});
builder.Services.AddSingleton<EQMX.Database.Oracle.EQMXIConnectionFactory>(sp =>
{
    var factory = sp.GetRequiredService<IDbConnectionFactory>();
    return new EQMX.Database.Oracle.EQMXOracleConnectionFactory(factory.ConnectionString);
});
builder.Services.AddScoped<EQMX.MarketDataService.Base.Logging.Logger, EqmxLoggerAdapter>();
builder.Services.AddScoped<EQMX.MarketDataService.Repository.VolatilitySurfaceRepository>();
builder.Services.AddScoped<IVolatilityMarketDataService, VolatilityMarketDataService>();
builder.Services.AddScoped<EQMX.MarketDataService.Repository.DividendRepository>();
builder.Services.AddScoped<IDividendMarketDataService, DividendMarketDataService>();
builder.Services.AddScoped<EQMX.MarketDataService.Repository.RepoCurveRepository>();
builder.Services.AddScoped<IRepoCurveMarketDataService, RepoCurveMarketDataService>();
builder.Services.AddScoped<EQMX.MarketDataService.Repository.CorrelationRepository>();
builder.Services.AddScoped<ICorrelationMarketDataService, CorrelationMarketDataService>();
builder.Services.AddScoped<EQMX.MarketDataService.Repository.CalibrationRepository>();
builder.Services.AddScoped<EQMX.MarketDataService.Repository.CalibrationXmlRepository>();
builder.Services.AddScoped<ICalibrationMarketDataService, CalibrationMarketDataService>();

// Database, User & Dapper Instrument Services
builder.Services.AddSingleton<IDbConnectionFactory, OracleDbConnectionFactory>();
builder.Services.AddScoped<IUserRepository, DapperUserRepository>();
builder.Services.AddScoped<IInstrumentRepository, DapperInstrumentRepository>();
builder.Services.AddScoped<IPubRequestRepository, DapperPubRequestRepository>();
builder.Services.AddScoped<InstrumentSelectionState>();
builder.Services.AddScoped<LatestRequestsViewState>();
builder.Services.AddScoped<LiveRequestsViewState>();
builder.Services.AddScoped<ArchiveRequestsViewState>();

var app = builder.Build();

app.UseSerilogRequestLogging();

// Configure virtual directory / PathBase support if explicitly configured or provided in environment
var configuredPathBase = builder.Configuration["PathBase"] ?? builder.Configuration["ASPNETCORE_PATHBASE"];
if (!string.IsNullOrWhiteSpace(configuredPathBase))
{
    app.UsePathBase(configuredPathBase.TrimEnd('/'));
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseMiddleware<WindowsAuthMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

// Auth Endpoints
app.MapPost("/api/auth/login", async (
    HttpContext httpContext,
    IUserRepository userRepository) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    var isValid = await userRepository.ValidatePasswordAsync(username, password);
    if (isValid)
    {
        var user = await userRepository.GetUserByNameAsync(username);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user?.Name ?? username),
            new Claim(ClaimTypes.NameIdentifier, user?.Ident.ToString() ?? "0"),
            new Claim(ClaimTypes.Role, (user?.IsTechnicalUser ?? false) ? "TechnicalUser" : "Trader")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        var basePath = httpContext.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
        var redirectTarget = string.IsNullOrEmpty(basePath) ? "/" : $"{basePath}/";

        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            !returnUrl.StartsWith("//") &&
            !returnUrl.StartsWith("/\\"))
        {
            if (!string.IsNullOrEmpty(basePath) && returnUrl.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                var relativeAfterBase = returnUrl.Substring(basePath.Length).TrimStart('/');
                if (!relativeAfterBase.StartsWith("login", StringComparison.OrdinalIgnoreCase) &&
                    !relativeAfterBase.StartsWith("access-denied", StringComparison.OrdinalIgnoreCase))
                {
                    redirectTarget = returnUrl;
                }
            }
            else
            {
                var relative = returnUrl.TrimStart('/');
                if (!relative.StartsWith("login", StringComparison.OrdinalIgnoreCase) &&
                    !relative.StartsWith("access-denied", StringComparison.OrdinalIgnoreCase))
                {
                    redirectTarget = string.IsNullOrEmpty(basePath) ? $"/{relative}" : $"{basePath}/{relative}";
                }
            }
        }

        return Results.Redirect(redirectTarget);
    }

    // If login is not successful, a courtesy page will be shown and user cannot see anything
    var accessDeniedBase = httpContext.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
    return Results.Redirect(string.IsNullOrEmpty(accessDeniedBase) ? "/access-denied" : $"{accessDeniedBase}/access-denied");
}).DisableAntiforgery();

app.MapGet("/api/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var logoutBase = httpContext.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
    return Results.Redirect(string.IsNullOrEmpty(logoutBase) ? "/login" : $"{logoutBase}/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program { }
