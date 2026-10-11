using ezZkvi.Data;
using ezZkvi.Models;
using ezZkvi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Isolated integration-test host: never read or overwrite the user's DPAPI keys.
if (builder.Environment.IsEnvironment("Verification"))
{
    var verificationConnection = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
    if (verificationConnection.Host != "localhost" ||
        !verificationConnection.Database!.StartsWith("ezkvi_verify_", StringComparison.Ordinal))
        throw new InvalidOperationException("Verification requires an isolated local test database.");
    builder.Services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(
        new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
}

builder.Services.AddDbContext<ApplicationDbContext>(
    options => options.UseNpgsql(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentity<Korisnik, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
})
    .AddErrorDescriber<ezZkvi.Services.BosanskiIdentityErrorDescriber>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";

    options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    options.SlidingExpiration = true;

    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.Redirect("/Account/NijeUlogovan");
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.Redirect("/Account/AccessDenied");
        return Task.CompletedTask;
    };
});

builder.Services.AddControllersWithViews(options =>
{
    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));

    options.Filters.Add(new Microsoft.AspNetCore.Mvc.ResponseCacheAttribute
    {
        NoStore = true,
        Location = Microsoft.AspNetCore.Mvc.ResponseCacheLocation.None
    });
});

builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings")
);

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ScoreService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Lijepa stranica za HTTP greške (npr. 404 kad se upiše nepostojeći/POST-only URL)
app.UseStatusCodePagesWithReExecute("/Home/Greska", "?kod={0}");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<Korisnik>>();
        var user = await userManager.GetUserAsync(context.User);
        if (user != null)
        {
            var now = DateTime.UtcNow;
            if (!user.LastActivity.HasValue || now - user.LastActivity.Value > TimeSpan.FromMinutes(1))
            {
                user.LastActivity = now;
                await userManager.UpdateAsync(user);
            }
        }
    }
    await next();
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = { "Admin", "Moderator", "Student" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

}

app.Run();
