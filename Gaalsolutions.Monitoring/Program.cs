using Gaalsolutions.Monitoring.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Gaalsolutions.Monitoring.Models;
using Gaalsolutions.Monitoring.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<HostService>();
builder.Services.AddSingleton<AlertService>();
builder.Services.AddHttpClient<ZabbixService>();

// Fuente de datos del Dashboard: simulada o Zabbix.
// Se elige con "Monitoring:Source" en los secretos de usuario ("Zabbix" o "Simulated").
// Si no está configurada, se usan datos simulados.
builder.Services.AddSingleton<SimulatedMonitoringSource>();
builder.Services.AddScoped<ZabbixMonitoringSource>();
builder.Services.AddScoped<IMonitoringSource>(sp =>
    string.Equals(builder.Configuration["Monitoring:Source"], "Zabbix", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<ZabbixMonitoringSource>()
        : sp.GetRequiredService<SimulatedMonitoringSource>());

var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection.");

if (string.Equals(databaseProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<PostgreSqlApplicationDbContext>(options =>
        options.UseNpgsql(connectionString)
            .ConfigureWarnings(warnings =>
                warnings.Log(RelationalEventId.PendingModelChangesWarning)));
    builder.Services.AddScoped<ApplicationDbContext>(sp =>
        sp.GetRequiredService<PostgreSqlApplicationDbContext>());
}
else
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
    {
        if (string.Equals(databaseProvider, "MySql", StringComparison.OrdinalIgnoreCase))
        {
            options.UseMySQL(connectionString);
        }
        else if (string.Equals(databaseProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlServer(connectionString);
        }
        else
        {
            throw new InvalidOperationException(
                $"Database:Provider '{databaseProvider}' no es compatible. Usa 'SqlServer', 'MySql' o 'PostgreSql'.");
        }
    });
}

// Identity SIN la interfaz predeterminada: no existe registro público,
// "olvidé mi contraseña" ni reenvío de confirmación. Se usa login propio.
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;

    // Mantiene el tamaño de 128 caracteres de las columnas clave de Identity
    options.Stores.MaxLengthForKeys = 128;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
});

builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<NotificationService>();

builder.Services.AddTransient<EmailService>();

// Envío automático de correos por alertas (se activa con Notifications:Enabled).
builder.Services.AddHostedService<AlertNotificationWorker>();

var app = builder.Build();

async Task SeedAdminAsync(IServiceProvider scopedServices, string? email, string? password)
{
    if (string.IsNullOrWhiteSpace(email) != string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            "Configura juntos BootstrapAdmin:Email y BootstrapAdmin:Password.");
    }

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        var existingUserManager = scopedServices.GetRequiredService<UserManager<IdentityUser>>();
        if (app.Environment.IsProduction() && !await existingUserManager.Users.AnyAsync())
        {
            throw new InvalidOperationException(
                "La base de datos no contiene usuarios. Configura BootstrapAdmin:Email y BootstrapAdmin:Password para el primer arranque.");
        }

        return;
    }

    var userManager = scopedServices.GetRequiredService<UserManager<IdentityUser>>();
    var admin = await userManager.FindByEmailAsync(email);

    if (admin is null)
    {
        admin = new IdentityUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(admin, password);

        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"No se pudo crear el usuario inicial: {errors}");
        }
    }
}

if (string.Equals(databaseProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    await SeedAdminAsync(
        scope.ServiceProvider,
        builder.Configuration["BootstrapAdmin:Email"],
        builder.Configuration["BootstrapAdmin:Password"]);
}

if (app.Environment.IsDevelopment()
    && string.Equals(databaseProvider, "MySql", StringComparison.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedAdminAsync(
        scope.ServiceProvider,
        builder.Configuration["LocalAdmin:Email"],
        builder.Configuration["LocalAdmin:Password"]);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();