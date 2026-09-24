using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryBookManager.Api.Catalogue;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Http;
using LibraryBookManager.Api.Identity;
using LibraryBookManager.Api.Lending;
using Microsoft.EntityFrameworkCore;

// "create-user" and "set-password" run an operator command instead of the server.
var command = args.Length > 0 && UserCommands.Names.Contains(args[0]);
var builder = WebApplication.CreateBuilder(command ? [] : args);

// org-sec-004 REQ-SEC-011: TLS 1.2 or later only, and plaintext is redirected permanently.
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.ConfigureHttpsDefaults(https => https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13));
builder.Services.AddHttpsRedirection(options => options.RedirectStatusCode = StatusCodes.Status301MovedPermanently);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddDbContext<LibraryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Library")
                      ?? throw new InvalidOperationException("ConnectionStrings:Library is not configured.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<LibraryCalendar>();
builder.Services.AddScoped<IActorResolver, SessionActorResolver>();
builder.Services.AddSingleton<AuditRetention>();
builder.Services.AddHostedService<AuditRetentionHostedService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureCreated();

if (command)
{
    var stdin = Console.IsInputRedirected ? Console.In : new StringReader(ReadSecretFromTerminal() + "\n");
    return await UserCommands.RunAsync(args, app.Services, stdin, Console.Out, Console.Error);
}

app.UseHttpsRedirection();
app.UseActorResolution();

var v1 = app.MapGroup("/v1");
v1.MapCatalogue();
v1.MapAuth();
v1.MapLending();

app.Run();
return 0;

// Reads a password typed at a terminal without echoing it.
static string ReadSecretFromTerminal()
{
    Console.Error.Write("Password: ");
    var secret = new StringBuilder();
    while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Length--; }
        else if (!char.IsControl(key.KeyChar)) secret.Append(key.KeyChar);
    }
    Console.Error.WriteLine();
    return secret.ToString();
}

public partial class Program;
