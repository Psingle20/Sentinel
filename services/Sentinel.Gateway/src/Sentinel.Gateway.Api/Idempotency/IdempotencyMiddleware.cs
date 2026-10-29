using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Sentinel.Gateway.Application.Abstractions.Persistence;

namespace Sentinel.Gateway.Api.Idempotency;

/// <summary>
/// Requires an <c>Idempotency-Key</c> on every mutation of the public API (Phase 08 API principles)
/// and makes retries safe: the same key and request get the first response again, marked with
/// <c>Idempotent-Replayed</c>; the same key with a different request is refused. Responses that
/// failed on the server are not kept, so a retry runs again.
/// </summary>
internal sealed partial class IdempotencyMiddleware(RequestDelegate next)
{
    public const string Header = IdempotencyExtensions.Header;
    public const string ReplayedHeader = IdempotencyExtensions.ReplayedHeader;

    private const int MaxStoredBody = 64 * 1024;

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store, TimeProvider time)
    {
        if (!AppliesTo(context.Request) || context.User.FindFirstValue(ClaimTypes.NameIdentifier) is not { } actor)
        {
            await next(context);
            return;
        }

        string key = context.Request.Headers[Header].ToString();
        if (!KeyFormat().IsMatch(key))
        {
            await Results.Problem(
                title: "Idempotency.KeyRequired",
                detail: $"Mutations need an {Header} header of 1 to 128 letters, digits, '-', '_', ':' or '.'.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "Idempotency.KeyRequired" }).ExecuteAsync(context);
            return;
        }

        string hash = await HashAsync(context.Request, context.RequestAborted);
        switch (await store.ClaimAsync(actor, key, hash, time.GetUtcNow(), context.RequestAborted))
        {
            case IdempotencyClaim.Replay replay:
                context.Response.StatusCode = replay.Response.StatusCode;
                context.Response.ContentType = replay.Response.ContentType;
                context.Response.Headers[ReplayedHeader] = "true";
                await context.Response.WriteAsync(replay.Response.Body, context.RequestAborted);
                return;
            case IdempotencyClaim.Mismatch:
                await Refuse(context, StatusCodes.Status422UnprocessableEntity, "Idempotency.KeyReused", "This idempotency key was used for a different request.");
                return;
            case IdempotencyClaim.InProgress:
                await Refuse(context, StatusCodes.Status409Conflict, "Idempotency.InProgress", "A request with this idempotency key is still running.");
                return;
        }

        Stream original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        catch
        {
            await store.ReleaseAsync(actor, key, CancellationToken.None);
            throw;
        }
        finally
        {
            context.Response.Body = original;
        }

        buffer.Position = 0;
        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError || buffer.Length > MaxStoredBody)
        {
            await store.ReleaseAsync(actor, key, CancellationToken.None);
        }
        else
        {
            string body = await new StreamReader(buffer, leaveOpen: true).ReadToEndAsync(context.RequestAborted);
            await store.CompleteAsync(actor, key, new StoredResponse(context.Response.StatusCode, context.Response.ContentType, body), time.GetUtcNow(), CancellationToken.None);
            buffer.Position = 0;
        }

        await buffer.CopyToAsync(original, context.RequestAborted);
    }

    // Every mutation of the versioned public API. The analytics beacon is fire-and-forget by design.
    private static bool AppliesTo(HttpRequest request) =>
        request.Path.StartsWithSegments("/api/v1", StringComparison.Ordinal)
        && !request.Path.StartsWithSegments("/api/v1/analytics", StringComparison.Ordinal)
        && (HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) || HttpMethods.IsPatch(request.Method) || HttpMethods.IsDelete(request.Method));

    private static async Task<string> HashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes($"{request.Method} {request.Path}{request.QueryString}\n"));
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            hash.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static Task Refuse(HttpContext context, int status, string code, string detail) =>
        Results.Problem(title: code, detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);

    [GeneratedRegex("^[A-Za-z0-9_:.-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyFormat();
}
