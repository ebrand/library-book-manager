using System.Net.Http.Json;
using System.Text.Json;

namespace LibraryBookManager.Api.Tests.Infrastructure;

// The published v1 representations, as the tests expect to read them.
// Deliberately independent of the implementation's own DTO types.

public sealed record ErrorBody(string Error, string Message, string? Field);

public sealed record CopyView(string Barcode, bool OnLoan);

public sealed record TitleDetail(
    string Isbn, string Title, string Author, int PublicationYear,
    int CopyCount, int AvailableCopies, bool Available, List<CopyView> Copies);

public sealed record TitleSummary(
    string Isbn, string Title, string Author, int PublicationYear,
    int CopyCount, int AvailableCopies, bool Available);

public sealed record UserView(Guid UserId, string Email, string Role);

public sealed record SessionView(string Token, UserView User);

public sealed record MemberView(Guid UserId, string Email);

public sealed record LoanView(
    string Barcode, string Isbn, string Title, MemberView Member,
    DateOnly CheckedOutOn, DateOnly DueOn, DateOnly? ReturnedOn, bool ReturnedLate, bool Overdue);

public sealed record SearchPage(List<TitleSummary> Items, int Total, int Page, int PageSize);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async Task<T> Read<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(Options);
        return body ?? throw new InvalidOperationException("empty body");
    }
}
