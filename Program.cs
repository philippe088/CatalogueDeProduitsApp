using CatalogueDeProduitsApp.Services;
using CatalogueDeProduitsApp.Services.Interfaces;
using CatalogueDeProduitsApp.Services.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Configuration du logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}

// Add services to the container.
builder.Services.AddControllersWithViews();

// Enregistrement des services avec l'injection de dépendances
builder.Services.AddScoped<IProduitRepository, CsvProduitRepository>();
builder.Services.AddSingleton<CsvMonitoringService>();

// Maintenir la compatibilité avec l'ancien service
// In Program.cs
builder.Services.AddScoped<ProduitService>(provider =>
{
    var repository = provider.GetRequiredService<IProduitRepository>();
    var logger = provider.GetRequiredService<ILogger<ProduitService>>();
    return new ProduitService(repository, logger);
});

// Configuration des options
builder.Services.Configure<CsvOptions>(options =>
{
    options.FilePath = Path.Combine(builder.Environment.ContentRootPath, "Data", "produits.csv");
    options.CacheExpiryMinutes = 5;
    options.BackupEnabled = true;
    options.BackupRetentionDays = 7;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Endpoint de monitoring pour les métriques
app.MapGet("/api/health", async (CsvMonitoringService monitoring) =>
{
    var csvPath = Path.Combine(app.Environment.ContentRootPath, "Data", "produits.csv");
    var healthCheck = await monitoring.CheckFileSystemHealthAsync(csvPath);
    return healthCheck.IsHealthy ? Results.Ok(healthCheck) : Results.Problem("System unhealthy", statusCode: 503);
});

app.MapGet("/api/metrics", (CsvMonitoringService monitoring) =>
{
    var metrics = monitoring.GetMetrics();
    return Results.Ok(metrics);
});

app.Run();







