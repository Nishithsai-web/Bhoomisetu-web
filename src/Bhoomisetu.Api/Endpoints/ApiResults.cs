using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.Api.Endpoints;

/// <summary>Consistent RFC 9457 problem responses. Every error carries a stable "code" the web app translates.</summary>
public static class ApiResults
{
    public static IResult Problem(int status, string code, string message, IDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };
        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                extensions[pair.Key] = pair.Value;
            }
        }

        return Results.Problem(statusCode: status, title: message, extensions: extensions);
    }

    public static IResult Invalid(string code, string message) => Problem(StatusCodes.Status400BadRequest, code, message);

    public static IResult Invalid(Error error) => Invalid(error.Code, error.Message);

    public static IResult NotFound(string what) => Problem(StatusCodes.Status404NotFound, "not_found", $"{what} was not found.");

    public static IResult Forbidden(string code, string message) => Problem(StatusCodes.Status403Forbidden, code, message);
}
