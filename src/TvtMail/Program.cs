using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TvtMail.Components;
using TvtMail.Data;
using TvtMail.Options;
using TvtMail.Services;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
var keyDirectory = Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(keyDirectory);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "tvtmail.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });
builder.Services.AddAuthorization();

builder.Services.Configure<AdminAuthOptions>(builder.Configuration.GetSection(AdminAuthOptions.SectionName));
builder.Services.Configure<TvtMailOptions>(builder.Configuration.GetSection(TvtMailOptions.SectionName));

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("tvtmail");

var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? "Data Source=data/tvtmail.db";

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddSingleton<CredentialProtector>();
builder.Services.AddSingleton<ImapMailService>();
builder.Services.AddHostedService<MailSyncHostedService>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

var adminAuth = app.Services.GetRequiredService<IOptions<AdminAuthOptions>>().Value;
if (string.Equals(adminAuth.Password, "ChangeMe-Immediately!", StringComparison.Ordinal))
{
    app.Logger.LogWarning("TVT Mail is using the default administrator password. Change AdminAuth__Password before exposing the application publicly.");
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/auth/login", async (HttpContext context, IOptions<AdminAuthOptions> options) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var email = form["email"].ToString().Trim();
    var password = form["password"].ToString();
    var configured = options.Value;

    var valid = !string.IsNullOrWhiteSpace(configured.Email)
                && !string.IsNullOrWhiteSpace(configured.Password)
                && FixedTimeEquals(email, configured.Email)
                && FixedTimeEquals(password, configured.Password);

    if (!valid)
        return Results.LocalRedirect("/login?error=1");

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, configured.Email),
        new(ClaimTypes.Email, configured.Email),
        new(ClaimTypes.Role, "Admin")
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        });

    return Results.LocalRedirect("/");
}).AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login");
});

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool FixedTimeEquals(string left, string right)
{
    var leftBytes = Encoding.UTF8.GetBytes(left);
    var rightBytes = Encoding.UTF8.GetBytes(right);
    return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
}
