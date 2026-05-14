namespace EigenfocusApi.Endpoints;

internal static class ApiResults
{
    public static IResult Error(int statusCode, string error, string detail) =>
        Results.Json(new { error, detail }, statusCode: statusCode);

    public static IResult BadRequest(string detail) =>
        Error(StatusCodes.Status400BadRequest, "Validation failed", detail);

    public static IResult NotFound(string detail) =>
        Error(StatusCodes.Status404NotFound, "Not found", detail);

    public static IResult Conflict(string detail) =>
        Error(StatusCodes.Status409Conflict, "Constraint violation", detail);
}
