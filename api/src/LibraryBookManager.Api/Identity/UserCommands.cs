using LibraryBookManager.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LibraryBookManager.Api.Identity;

/// <summary>
/// Operator commands for accounts (QUESTIONS.md Q-AUTH-10). The specification has users but
/// no way to create them, so accounts are made by whoever runs the service:
///
///   LibraryBookManager.Api create-user --email a@b.c --role member|librarian|administrator
///   LibraryBookManager.Api set-password --email a@b.c
///
/// The password is read from standard input, never from the command line, where it would
/// be visible in process listings and shell history.
/// </summary>
public static class UserCommands
{
    public const int MinimumPasswordLength = 8;
    public static readonly string[] Names = ["create-user", "set-password"];

    public static async Task<int> RunAsync(string[] args, IServiceProvider services, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || !Names.Contains(args[0]))
        {
            await stderr.WriteLineAsync($"Unknown command. Commands: {string.Join(", ", Names)}.");
            return 2;
        }

        var options = new Dictionary<string, string>();
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                await stderr.WriteLineAsync($"Unexpected argument '{args[i]}'.");
                return 2;
            }
            options[args[i][2..]] = args[++i];
        }

        var email = AuthEndpoints.NormalizeEmail(options.GetValueOrDefault("email") ?? "");
        if (!LooksLikeEmail(email))
        {
            await stderr.WriteLineAsync("--email must be an email address.");
            return 1;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var existing = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        if (args[0] == "create-user")
        {
            if (!AuthEndpoints.TryParseRole(options.GetValueOrDefault("role"), out var role))
            {
                await stderr.WriteLineAsync("--role must be member, librarian or administrator.");
                return 1;
            }
            if (existing is not null)
            {
                await stderr.WriteLineAsync($"{email} already has an account.");
                return 1;
            }
            if (await ReadPassword(stdin, stderr) is not { } password) return 1;

            var user = new User { PublicId = Guid.NewGuid(), Email = email, Role = role, PasswordHash = hasher.Hash(password) };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.AuditRecords.Add(new AuditRecord { Event = AuditEvent.UserCreated, OccurredAt = now, AffectedUserId = user.Id, NewRole = role });
            await db.SaveChangesAsync();
            await stdout.WriteLineAsync($"Created {options["role"]} {email} ({user.PublicId}).");
            return 0;
        }

        if (existing is null)
        {
            await stderr.WriteLineAsync($"{email} has no account.");
            return 1;
        }
        if (await ReadPassword(stdin, stderr) is not { } newPassword) return 1;
        existing.PasswordHash = hasher.Hash(newPassword);
        db.AuditRecords.Add(new AuditRecord { Event = AuditEvent.PasswordSet, OccurredAt = now, AffectedUserId = existing.Id });
        await db.SaveChangesAsync();
        await stdout.WriteLineAsync($"Password set for {email}.");
        return 0;
    }

    private static async Task<string?> ReadPassword(TextReader stdin, TextWriter stderr)
    {
        var password = await stdin.ReadLineAsync();
        if (password is null || password.Length < MinimumPasswordLength)
        {
            await stderr.WriteLineAsync($"The password (read from standard input) must be at least {MinimumPasswordLength} characters.");
            return null;
        }
        return password;
    }

    private static bool LooksLikeEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && at < email.Length - 1 && !email.Any(char.IsWhiteSpace);
    }
}
