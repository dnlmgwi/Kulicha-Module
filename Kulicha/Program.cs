using Kulicha.Components;
using Kulicha.Services;

var builder = WebApplication.CreateBuilder(args);

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Register the SpacetimeDB background service
builder.Services.AddHostedService<SpacetimeDbService>();

// If you need to access the service (e.g., its input queue) from Blazor components,
// register it as a singleton too. Be mindful of thread safety when accessing it.
builder.Services.AddSingleton<SpacetimeDbService>(); // Allows injection

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

Console.WriteLine("Starting Web Application...");
app.Run(); // This will now block, while the SpacetimeDBService runs in the background
Console.WriteLine("Web Application Stopped."); // This line executes on shutdown
