namespace LibraryBookManager.Api.Http;

/// <summary>The single error representation every endpoint returns.</summary>
public sealed record ApiError(string Error, string Message, string? Field = null)
{
    public IResult ToResult(int status) => Results.Json(this, statusCode: status);

    public static IResult BadRequest(string code, string message, string? field = null) =>
        new ApiError(code, message, field).ToResult(StatusCodes.Status400BadRequest);

    public static IResult NotFound(string code, string message) =>
        new ApiError(code, message).ToResult(StatusCodes.Status404NotFound);

    public static IResult Conflict(string code, string message) =>
        new ApiError(code, message).ToResult(StatusCodes.Status409Conflict);

    public static IResult Forbidden() =>
        new ApiError("FORBIDDEN", "Your role does not permit this operation.").ToResult(StatusCodes.Status403Forbidden);

    public static IResult Unauthenticated(string code = "UNAUTHENTICATED", string message = "Sign in to continue.") =>
        new ApiError(code, message).ToResult(StatusCodes.Status401Unauthorized);

    public static IResult FieldRequired(string field) =>
        BadRequest("FIELD_REQUIRED", $"{field} is required.", field);

    public static IResult InvalidField(string field, string message) =>
        BadRequest("INVALID_FIELD", message, field);
}
