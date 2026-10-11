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
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = { "Admin", "Moderator", "Student" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    var stareOpceniteOblasti = await db.Oblast
        .Where(o => o.Naziv == "Općenito" || o.Naziv == "Opcenito" || o.Naziv == "općenito" || o.Naziv == "opcenito")
        .ToListAsync();

    if (stareOpceniteOblasti.Count > 0)
    {
        foreach (var oblast in stareOpceniteOblasti)
        {
            oblast.Naziv = "Oblast 1";
        }

        await db.SaveChangesAsync();
    }

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Korisnik>>();

    async Task SeedUserAsync(string email, string password, string role)
    {
        var user = await userManager.FindByEmailAsync(email);

        if (user == null)
        {
            user = new Korisnik
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                IsApproved = true
            };

            var result = await userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
        else
        {
            user.EmailConfirmed = true;
            user.IsApproved = true;

            var updateResult = await userManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
            {
                throw new Exception(string.Join(", ", updateResult.Errors.Select(e => e.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            var roleResult = await userManager.AddToRoleAsync(user, role);

            if (!roleResult.Succeeded)
            {
                throw new Exception(string.Join(", ", roleResult.Errors.Select(e => e.Description)));
            }
        }
    }

    await SeedUserAsync("admin@ezzkvi.ba", "Admin123!", "Admin");
    await SeedUserAsync("moderator@ezzkvi.ba", "Moderator123!", "Moderator");
    await SeedUserAsync("student@ezzkvi.ba", "Student123!", "Student");
}

app.Run();
