using System.Security.Cryptography;
using System.Text.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LibraryBookManager.Api.Catalogue;

public sealed record CopyResponse(string Barcode, bool OnLoan);

public sealed record TitleResponse(
    string Isbn, string Title, string Author, int PublicationYear,
    int CopyCount, int AvailableCopies, bool Available, IReadOnlyList<CopyResponse> Copies);

public sealed record TitleSummaryResponse(
    string Isbn, string Title, string Author, int PublicationYear,
    int CopyCount, int AvailableCopies, bool Available);

public sealed record SearchResponse(IReadOnlyList<TitleSummaryResponse> Items, int Total, int Page, int PageSize);

/// <summary>Capability BOOK — Book records.</summary>
public static class CatalogueEndpoints
{
    public const int PageSize = 50;

    public static void MapCatalogue(this IEndpointRouteBuilder v1)
    {
        v1.MapGet("/titles", Search).RequireActor();
        v1.MapPost("/titles", AddTitle).RequireActor(Role.Librarian);
        v1.MapGet("/titles/{isbn}", GetTitle).RequireActor(Role.Librarian);
        v1.MapPatch("/titles/{isbn}", UpdateTitle).RequireActor(Role.Librarian);
        v1.MapPost("/titles/{isbn}/copies", AddCopy).RequireActor(Role.Librarian);
        v1.MapDelete("/copies/{barcode}", RemoveCopy).RequireActor(Role.Librarian);
    }

    // REQ-BOOK-005, REQ-BOOK-006
    private static async Task<IResult> Search(HttpRequest request, LibraryDbContext db)
    {
        var query = request.Query;
        var named = new[] { "title", "author", "isbn" }.Where(query.ContainsKey).ToArray();
        if (named.Length != 1)
            return ApiError.BadRequest("INVALID_SEARCH", "Search by exactly one of title, author or isbn.");

        var page = 1;
        if (query.TryGetValue("page", out var pageText) && (!int.TryParse(pageText, out page) || page < 1))
            return ApiError.BadRequest("INVALID_SEARCH", "page must be a whole number of 1 or more.");

        var field = named[0];
        var term = query[field].ToString().Trim();
        IQueryable<Title> titles = db.Titles.AsNoTracking();
        titles = field switch
        {
            "isbn" => titles.Where(t => t.Isbn == Isbn.Normalize(term)),
            "title" => titles.Where(t => EF.Functions.Like(t.Name, Contains(term), "\\")),
            _ => titles.Where(t => EF.Functions.Like(t.Author, Contains(term), "\\")),
        };

        var total = await titles.CountAsync();
        var items = await titles
            .OrderBy(t => t.Name).ThenBy(t => t.Isbn)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(t => new
            {
                t.Isbn, t.Name, t.Author, t.PublicationYear,
                CopyCount = t.Copies.Count(c => c.RemovedAt == null),
                AvailableCopies = t.Copies.Count(c => c.RemovedAt == null && !c.Loans.Any(l => l.ReturnedOn == null)),
            })
            .ToListAsync();

        return Results.Ok(new SearchResponse(
            items.Select(t => new TitleSummaryResponse(t.Isbn, t.Name, t.Author, t.PublicationYear,
                t.CopyCount, t.AvailableCopies, t.AvailableCopies > 0)).ToList(),
            total, page, PageSize));
    }

    private static string Contains(string term) =>
        "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    // REQ-BOOK-001
    private static async Task<IResult> AddTitle(HttpRequest request, LibraryDbContext db)
    {
        if (await JsonBody.ReadObjectAsync(request) is not { } body) return JsonBody.Malformed();

        foreach (var field in new[] { "isbn", "title", "author", "publicationYear" })
            if (body.IsEmpty(field)) return ApiError.FieldRequired(field);

        var isbnText = body.Text("isbn");
        if (isbnText is null || !Isbn.HasValidShape(Isbn.Normalize(isbnText)))
            return ApiError.InvalidField("isbn", "isbn must be an ISBN-10 or ISBN-13.");
        if (ValidateText(body, "title") is { } titleError) return titleError;
        if (ValidateText(body, "author") is { } authorError) return authorError;
        if (!body.TryInt("publicationYear", out var year))
            return ApiError.InvalidField("publicationYear", "publicationYear must be a whole number.");

        var isbn = Isbn.Normalize(isbnText);
        if (await db.Titles.AnyAsync(t => t.Isbn == isbn)) return IsbnExists();

        var title = new Title { Isbn = isbn, Name = body.Text("title")!, Author = body.Text("author")!, PublicationYear = year };
        db.Titles.Add(title);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (IsUniqueViolation(e))
        {
            return IsbnExists(); // lost a race with a concurrent add of the same ISBN
        }

        return Results.Created($"/v1/titles/{isbn}", await Detail(db, isbn));
    }

    private static IResult IsbnExists() =>
        ApiError.Conflict("ISBN_ALREADY_EXISTS", "A title with this ISBN is already in the catalogue.");

    private static IResult? ValidateText(JsonElement body, string field) =>
        body.Text(field) is null ? ApiError.InvalidField(field, $"{field} must be text.") : null;

    private static async Task<IResult> GetTitle(string isbn, LibraryDbContext db) =>
        await Detail(db, Isbn.Normalize(isbn)) is { } title ? Results.Ok(title) : TitleNotFound();

    // REQ-BOOK-003
    private static async Task<IResult> UpdateTitle(string isbn, HttpRequest request, LibraryDbContext db)
    {
        if (await JsonBody.ReadObjectAsync(request) is not { } body) return JsonBody.Malformed();

        var title = await db.Titles.SingleOrDefaultAsync(t => t.Isbn == Isbn.Normalize(isbn));
        if (title is null) return TitleNotFound();

        if (body.Has("isbn") && (body.Text("isbn") is not { } requested || Isbn.Normalize(requested) != title.Isbn))
            return ApiError.BadRequest("ISBN_IMMUTABLE", "A title's ISBN cannot be changed.", "isbn");

        foreach (var field in new[] { "title", "author", "publicationYear" })
            if (body.Has(field) && body.IsEmpty(field)) return ApiError.FieldRequired(field);
        if (body.Has("title") && ValidateText(body, "title") is { } titleError) return titleError;
        if (body.Has("author") && ValidateText(body, "author") is { } authorError) return authorError;
        var year = title.PublicationYear;
        if (body.Has("publicationYear") && !body.TryInt("publicationYear", out year))
            return ApiError.InvalidField("publicationYear", "publicationYear must be a whole number.");

        // Only these three fields are ever written; copies and loans are not touched.
        if (body.Has("title")) title.Name = body.Text("title")!;
        if (body.Has("author")) title.Author = body.Text("author")!;
        title.PublicationYear = year;
        await db.SaveChangesAsync();

        return Results.Ok(await Detail(db, title.Isbn));
    }

    // REQ-BOOK-002
    private static async Task<IResult> AddCopy(string isbn, HttpRequest request, LibraryDbContext db)
    {
        if (await JsonBody.ReadObjectAsync(request) is not { } body) return JsonBody.Malformed();

        var title = await db.Titles.SingleOrDefaultAsync(t => t.Isbn == Isbn.Normalize(isbn));
        if (title is null) return TitleNotFound();

        string barcode;
        if (body.IsEmpty("barcode"))
        {
            barcode = await NewBarcode(db);
        }
        else
        {
            if (body.Text("barcode") is not { } supplied)
                return ApiError.InvalidField("barcode", "barcode must be text.");
            barcode = supplied;
            if (await db.Copies.AnyAsync(c => c.Barcode == barcode)) return BarcodeInUse();
        }

        db.Copies.Add(new Copy { TitleId = title.Id, Barcode = barcode });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (IsUniqueViolation(e))
        {
            return BarcodeInUse();
        }

        return Results.Json(new CopyResponse(barcode, false), statusCode: StatusCodes.Status201Created);
    }

    private static IResult BarcodeInUse() =>
        ApiError.Conflict("BARCODE_IN_USE", "Another copy already holds this barcode.");

    /// <summary>"LBM" and 10 random digits, drawn again on the rare collision.</summary>
    private static async Task<string> NewBarcode(LibraryDbContext db)
    {
        while (true)
        {
            var candidate = "LBM" + RandomNumberGenerator.GetInt32(0, int.MaxValue).ToString("D10");
            if (!await db.Copies.AnyAsync(c => c.Barcode == candidate)) return candidate;
        }
    }

    // REQ-BOOK-004
    private static async Task<IResult> RemoveCopy(string barcode, LibraryDbContext db, TimeProvider clock)
    {
        // Serialised with checkout, so a copy is never both removed and lent (Q-BOOK-14).
        await using var transaction = await Lending.LendingEndpoints.BeginSerialized(db);
        var copy = await db.Copies.SingleOrDefaultAsync(c => c.Barcode == barcode && c.RemovedAt == null);
        if (copy is null) return ApiError.NotFound("COPY_NOT_FOUND", "No copy in the catalogue has this barcode.");
        if (await db.Loans.AnyAsync(l => l.CopyId == copy.Id && l.ReturnedOn == null))
            return ApiError.Conflict("COPY_ON_LOAN", "The copy is on loan and cannot be removed.");

        copy.RemovedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.NoContent();
    }

    private static IResult TitleNotFound() =>
        ApiError.NotFound("TITLE_NOT_FOUND", "No title in the catalogue has this ISBN.");

    private static async Task<TitleResponse?> Detail(LibraryDbContext db, string isbn)
    {
        var title = await db.Titles.AsNoTracking()
            .Where(t => t.Isbn == isbn)
            .Select(t => new
            {
                t.Isbn, t.Name, t.Author, t.PublicationYear,
                Copies = t.Copies.Where(c => c.RemovedAt == null).OrderBy(c => c.Barcode)
                    .Select(c => new CopyResponse(c.Barcode, c.Loans.Any(l => l.ReturnedOn == null)))
                    .ToList(),
            })
            .SingleOrDefaultAsync();
        if (title is null) return null;
        var available = title.Copies.Count(c => !c.OnLoan);
        return new TitleResponse(title.Isbn, title.Name, title.Author, title.PublicationYear,
            title.Copies.Count, available, available > 0, title.Copies);
    }

    private static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is SqliteException { SqliteErrorCode: 19 }; // SQLITE_CONSTRAINT
}
