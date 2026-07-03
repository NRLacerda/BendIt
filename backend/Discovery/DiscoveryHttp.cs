namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private static async Task<DiscoveryHttpResult> ExecuteDiscoveryRequestAsync(HttpClient client, string method, string url, long maxBodyBytes, CancellationToken cancellationToken)
    {
        var result = new DiscoveryHttpResult(method, url);
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await ReadLimitedAsync(response.Content, maxBodyBytes, cancellationToken);
            result.StatusCode = (int)response.StatusCode;
            result.ContentType = response.Content.Headers.ContentType?.ToString() ?? "";
            result.Body = body;
            result.ResponseLength = body.Length;
            result.ResponseHash = BodyHash(body);
            result.RedirectedTo = response.Headers.Location?.ToString() ?? "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result.Error = ex;
        }

        return result;
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, long maxBodyBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            var allowed = (int)Math.Min(read, maxBodyBytes - total);
            if (allowed > 0)
            {
                memory.Write(buffer, 0, allowed);
                total += allowed;
            }

            if (total >= maxBodyBytes)
            {
                break;
            }
        }

        return memory.ToArray();
    }
}
