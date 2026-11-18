using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Infrastucture.Http;

/// <summary>
/// Global MVC filter: every JSON error response (status &gt;= 400) leaves as an
/// RFC 7807 ProblemDetails (<c>application/problem+json</c>) with status,
/// title, detail (the message) and traceId, plus an <c>error</c> extension
/// carrying the same message so older clients reading <c>body.error</c> keep
/// working. Converted:
///  - a bare string (<c>BadRequest("...")</c>, <c>Conflict("...")</c>, ...);
///  - an anonymous object with an <c>error</c> (or else <c>message</c>) string
///    property; its other properties (counts, <c>code</c>, <c>job</c>, ...)
///    become extensions;
///  - an ObjectResult with no value.
/// ProblemDetails / ValidationProblemDetails and any other value are left as
/// they are, as is every non-object result (files, HTML, redirects, empty
/// status codes - [ApiController] already maps those to ProblemDetails).
/// Always-run, so results set by exception filters and short-circuiting
/// filters are covered too.
/// </summary>
public sealed class ErrorProblemDetailsFilter : IAsyncAlwaysRunResultFilter
{
    public const string ProblemJsonContentType = "application/problem+json";
    public const string ErrorExtension = "error";

    // Standard ProblemDetails members: never shadowed by an extension.
    private static readonly HashSet<string> ReservedNames =
        new(StringComparer.OrdinalIgnoreCase) { "type", "title", "status", "detail", "instance" };

    private readonly ProblemDetailsFactory _factory;
    private readonly JsonOptions _json;

    public ErrorProblemDetailsFilter(ProblemDetailsFactory factory, IOptions<JsonOptions> json)
    {
        _factory = factory;
        _json = json.Value;
    }

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { StatusCode: >= 400 } result
            && TryDescribe(result.Value, out var detail, out var extras))
        {
            var status = result.StatusCode!.Value;
            var problem = _factory.CreateProblemDetails(context.HttpContext, statusCode: status, detail: detail);
            problem.Title ??= ReasonPhrases.GetReasonPhrase(status);
            problem.Extensions[ErrorExtension] = detail ?? problem.Title;

            var naming = _json.JsonSerializerOptions.PropertyNamingPolicy;
            foreach (var (name, value) in extras)
            {
                var key = naming?.ConvertName(name) ?? name;
                if (!ReservedNames.Contains(key)) problem.Extensions.TryAdd(key, value);
            }

            context.Result = new ObjectResult(problem)
            {
                StatusCode = status,
                DeclaredType = typeof(ProblemDetails),
                ContentTypes = { ProblemJsonContentType }
            };
        }

        return next();
    }

    // The message (if any) and extra properties of a convertible error value;
    // false for values that stay as they are.
    private static bool TryDescribe(object? value, out string? detail, out List<(string Name, object? Value)> extras)
    {
        detail = null;
        extras = new List<(string, object?)>();

        switch (value)
        {
            case null:
                return true;
            case ProblemDetails:
                return false;
            case string text:
                detail = text;
                return true;
        }

        var type = value.GetType();
        if (!IsAnonymousType(type)) return false;

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var messageProperty =
            properties.FirstOrDefault(p => string.Equals(p.Name, "error", StringComparison.OrdinalIgnoreCase) && p.PropertyType == typeof(string))
            ?? properties.FirstOrDefault(p => string.Equals(p.Name, "message", StringComparison.OrdinalIgnoreCase) && p.PropertyType == typeof(string));
        if (messageProperty is null) return false;

        detail = (string?)messageProperty.GetValue(value);
        foreach (var property in properties)
        {
            if (property == messageProperty) continue;
            extras.Add((property.Name, property.GetValue(value)));
        }
        return true;
    }

    private static bool IsAnonymousType(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        && type.Name.Contains("AnonymousType", StringComparison.Ordinal);
}
