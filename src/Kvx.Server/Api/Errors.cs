namespace Kvx.Server.Api;

public sealed record ErrorBody(string Code, string Message, InnerError? InnerError = null);
public sealed record InnerError(string Code);
public sealed record ErrorEnvelope(ErrorBody Error);

public static class Errors
{
    public static IResult Result(int status, string code, string message, string? inner = null) =>
        Results.Json(Envelope(code, message, inner), statusCode: status);

    public static Task Write(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(Envelope(code, message, null));
    }

    private static ErrorEnvelope Envelope(string code, string message, string? inner) =>
        new(new ErrorBody(code, message, inner is null ? null : new InnerError(inner)));
}
