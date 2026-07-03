using System.Security.Cryptography;
using System.Text;
using BendIt.Api.Models;

namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private static async Task<BaselineFingerprint> BuildBaselineAsync(string baseUrl, HttpClient client, DiscoveryRequest request, CancellationToken cancellationToken)
    {
        var suffix = ShortHash(baseUrl + DateTimeOffset.UtcNow.ToString("O"));
        var paths = new[] { "/__bendit_random_" + suffix, "/api/__bendit_random_" + suffix, "/random-not-found-bendit-" + suffix };
        var samples = new List<DiscoveryHttpResult>();
        foreach (var path in paths)
        {
            if (ResolveCandidateUrl(baseUrl, path, out var fullUrl))
            {
                samples.Add(await ExecuteDiscoveryRequestAsync(client, HttpMethod.Get.Method, fullUrl, request.MaxBodyBytes, cancellationToken));
            }
        }

        return new BaselineFingerprint(samples);
    }

    private static bool LooksLikeSoft404(DiscoveryHttpResult result, BaselineFingerprint baseline)
    {
        if (result.Error is not null || baseline.Samples.Count == 0)
        {
            return false;
        }

        foreach (var sample in baseline.Samples.Where(sample => sample.Error is null && result.StatusCode == sample.StatusCode))
        {
            if (!string.IsNullOrEmpty(result.ResponseHash) && result.ResponseHash == sample.ResponseHash)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(result.ContentType) && !string.IsNullOrEmpty(sample.ContentType) &&
                ContentTypeFamily(result.ContentType) == ContentTypeFamily(sample.ContentType) &&
                Math.Abs(result.ResponseLength - sample.ResponseLength) <= 128)
            {
                return true;
            }
        }

        return false;
    }

    private static string BodyHash(byte[] body)
    {
        return body.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant()[..16];
    }

    private static string ShortHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];
    }

    private static string ContentTypeFamily(string value)
    {
        return value.Split(';')[0].Trim().ToLowerInvariant();
    }
}
