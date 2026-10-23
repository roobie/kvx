namespace Kvx.Server.Api;

/// <summary>
/// Key Vault list paging. The continuation is the last key returned (keyset), so a page boundary stays
/// put when secrets are added or removed between pages. The SDK follows nextLink verbatim, so it carries api-version.
/// </summary>
public static class Paging
{
    /// <summary>The service's documented maximum and default for maxresults.</summary>
    private const int MaxResults = 25;

    public static PageBody<TOut> Page<TIn, TOut>(
        HttpContext context, IEnumerable<TIn> items, Func<TIn, string> key, Func<TIn, TOut> map)
    {
        var query = context.Request.Query;
        var size = int.TryParse(query["maxresults"], out var n) ? Math.Clamp(n, 1, MaxResults) : MaxResults;
        var after = query["$skiptoken"].ToString();

        var ordered = items.OrderBy(key, StringComparer.OrdinalIgnoreCase)
            .SkipWhile(x => after.Length > 0 && StringComparer.OrdinalIgnoreCase.Compare(key(x), after) <= 0)
            .Take(size + 1)
            .ToList();

        string? next = null;
        if (ordered.Count > size)
        {
            ordered.RemoveAt(size);
            var link = QueryString.Create(new KeyValuePair<string, string?>[]
            {
                new("api-version", query["api-version"].ToString()),
                new("$skiptoken", key(ordered[^1])),
                new("maxresults", size.ToString()),
            });
            next = $"https://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{link}";
        }
        return new PageBody<TOut>(ordered.Select(map).ToList(), next);
    }
}
