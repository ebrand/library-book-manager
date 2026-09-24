using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Identity;

namespace LibraryBookManager.Api.Tests.Infrastructure;

/// <summary>Builds the "given" state of a criterion directly in the store.</summary>
public static class Given
{
    private static int _sequence;

    /// <summary>A registered user with a known password (<see cref="ApiFactory.Password"/> unless given).</summary>
    public static User User(this ApiFactory api, Role role, string? email = null, string password = ApiFactory.Password)
    {
        var n = Interlocked.Increment(ref _sequence);
        var user = new User
        {
            PublicId = Guid.NewGuid(),
            Email = email ?? $"{role.ToString().ToLowerInvariant()}{n}@example.test",
            Role = role,
            PasswordHash = api.Service<PasswordHasher>().Hash(password),
        };
        api.Seed(db => db.Users.Add(user));
        return user;
    }

    public static Title Title(this ApiFactory api, string isbn, int copies = 0,
        string title = "The Lord of the Rings", string author = "J. R. R. Tolkien", int year = 1991)
    {
        var entity = new Title { Isbn = isbn, Name = title, Author = author, PublicationYear = year };
        for (var i = 1; i <= copies; i++)
            entity.Copies.Add(new Copy { Barcode = $"{isbn}-{i}" });
        api.Seed(db => db.Titles.Add(entity));
        return entity;
    }

    public static Loan OpenLoan(this ApiFactory api, string barcode, User member,
        DateOnly? checkedOut = null, DateOnly? due = null)
    {
        var start = checkedOut ?? new DateOnly(2026, 3, 1);
        Loan loan = null!;
        api.Seed(db =>
        {
            var copy = db.Copies.Single(c => c.Barcode == barcode);
            loan = new Loan
            {
                CopyId = copy.Id,
                MemberId = member.Id,
                CheckedOutOn = start,
                DueOn = due ?? start.AddDays(21),
            };
            db.Loans.Add(loan);
        });
        return loan;
    }
}
