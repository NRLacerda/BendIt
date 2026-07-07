using System.Net.Sockets;
using System.Text;
using BendIt.Api.Discovery;
using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("native api list contains api routes from go implementation", NativeApiListContainsApiRoutes),
    ("discovery and battery produce results for local api", DiscoveryAndBatteryProduceResultsForLocalApi),
    ("likely exists health endpoint still produces result evidence", LikelyExistsHealthEndpointProducesResultEvidence),
    ("localhost https discovery falls back to http", LocalhostHttpsDiscoveryFallsBackToHttp),
    ("test battery caps response body capture", TestBatteryCapsResponseBodyCapture),
    ("test battery routes body tests by http verb", TestBatteryRoutesBodyTestsByHttpVerb),
    ("test battery maps results to OWASP categories", TestBatteryMapsResultsToOwaspCategories),
    ("security headers validator records misconfiguration evidence", SecurityHeadersValidatorRecordsMisconfigurationEvidence),
    ("auth boundary validator sends malformed jwt probes", AuthBoundaryValidatorSendsMalformedJwtProbes),
    ("id mutation detects query identifier parameters", IdMutationDetectsQueryIdentifierParameters),
    ("inventory validator flags live legacy routes", InventoryValidatorFlagsLiveLegacyRoutes),
    ("sensitive data validator reports data classes without values", SensitiveDataValidatorReportsDataClassesWithoutValues),
    ("rate limit validator detects throttled burst", RateLimitValidatorDetectsThrottledBurst),
    ("rate limit validator detects missing throttling", RateLimitValidatorDetectsMissingThrottling),
    ("rate limit validator detects degraded post-burst response", RateLimitValidatorDetectsDegradedPostBurstResponse),
    ("rate limit validator caps requested burst size", RateLimitValidatorCapsRequestedBurstSize),
    ("content type validator accepts secure json only endpoint", ContentTypeValidatorAcceptsSecureJsonOnlyEndpoint),
    ("content type validator flags text plain json acceptance", ContentTypeValidatorFlagsTextPlainJsonAcceptance),
    ("content type validator flags missing content type acceptance", ContentTypeValidatorFlagsMissingContentTypeAcceptance),
    ("content type validator skips get endpoints", ContentTypeValidatorSkipsGetEndpoints)
};

foreach (var test in tests)
{
    await test.Run();
    Console.WriteLine("PASS " + test.Name);
}

static Task NativeApiListContainsApiRoutes()
{
    var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "backend", "Resources", "api-list.txt"));
    AssertContains(lines, "GET /api/users");
    AssertContains(lines, "GET /api/orders/{id}");
    AssertContains(lines, "POST /api/token");
    return Task.CompletedTask;
}

static async Task DiscoveryAndBatteryProduceResultsForLocalApi()
{
    await using var server = await LocalApiServer.StartAsync();
    var discovery = new DiscoveryOrchestrator(new TestEnvironment(Path.Combine(RepoRoot(), "backend")));

    var project = new Project
    {
        ProjectId = "local-api",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    };

    var endpoints = await discovery.RunAsync(project, new DiscoveryRequest
    {
        UseNativeApiList = true,
        UseSpecDiscovery = false,
        MaxWorkers = 4,
        TimeoutSeconds = 2
    }, [], CancellationToken.None);

    if (endpoints.Endpoints.Count == 0)
    {
        throw new InvalidOperationException("expected native API-list discovery to register endpoints");
    }

    var testable = DiscoveryOrchestrator.FilterTestableEndpoints(endpoints.Endpoints);
    if (testable.Count == 0)
    {
        throw new InvalidOperationException("expected discovered local API endpoints to be testable");
    }

    var results = await new TestBatteryRunner().RunAsync(project, testable, new TestRunRequest
    {
        BendTypes = ["authConsistency", "requestSize"],
        ParallelWorkers = 2
    }, CancellationToken.None);

    if (results.Results.Count == 0)
    {
        throw new InvalidOperationException("expected test battery to produce results for discovered endpoints");
    }
}

static async Task LikelyExistsHealthEndpointProducesResultEvidence()
{
    await using var server = await LocalApiServer.StartAsync();
    var discovery = new DiscoveryOrchestrator(new TestEnvironment(Path.Combine(RepoRoot(), "backend")));

    var project = new Project
    {
        ProjectId = "orgrow",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    };

    var endpoints = await discovery.RunAsync(project, new DiscoveryRequest
    {
        UseNativeApiList = false,
        UseSpecDiscovery = false,
        ApiList = ["GET /api/orgrow/grow/isalive"],
        MaxWorkers = 2,
        TimeoutSeconds = 2
    }, [], CancellationToken.None);

    var endpoint = endpoints.Endpoints.SingleOrDefault(item => item.Path == "/api/orgrow/grow/isalive")
        ?? throw new InvalidOperationException("expected isalive endpoint to be discovered");

    if (endpoint.Classification != "likely_exists")
    {
        throw new InvalidOperationException($"expected text/plain true endpoint to be likely_exists, got {endpoint.Classification}");
    }

    var testable = DiscoveryOrchestrator.FilterTestableEndpoints(endpoints.Endpoints);
    if (testable.All(item => item.Id != endpoint.Id))
    {
        throw new InvalidOperationException("expected likely_exists isalive endpoint to be testable");
    }

    var results = await new TestBatteryRunner().RunAsync(project, testable, new TestRunRequest
    {
        BendTypes = ["authConsistency", "idMutation", "httpMethodValidation"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    if (results.Results.Count == 0)
    {
        throw new InvalidOperationException("expected likely_exists isalive endpoint to produce raw evidence");
    }

    if (results.Results.All(result => result.ResultBody != "true"))
    {
        throw new InvalidOperationException("expected resultBody to contain the actual API response body");
    }

    if (results.Results.Any(result => result.Interesting))
    {
        throw new InvalidOperationException("expected public isalive endpoint evidence to be healthy, not a finding");
    }
}

static async Task LocalhostHttpsDiscoveryFallsBackToHttp()
{
    await using var server = await LocalApiServer.StartAsync();
    var discovery = new DiscoveryOrchestrator(new TestEnvironment(Path.Combine(RepoRoot(), "backend")));
    var httpsBaseUrl = server.BaseUrl
        .Replace("http://127.0.0.1:", "https://localhost:", StringComparison.Ordinal);

    var project = new Project
    {
        ProjectId = "localhost-https-fallback",
        BaseUrl = httpsBaseUrl,
        Auth = new AuthConfig { Type = "none" }
    };

    var endpoints = await discovery.RunAsync(project, new DiscoveryRequest
    {
        UseNativeApiList = false,
        UseSpecDiscovery = false,
        ApiList = ["GET /api/orgrow/grow/isalive"],
        MaxWorkers = 1,
        TimeoutSeconds = 2
    }, [], CancellationToken.None);

    var endpoint = endpoints.Endpoints.SingleOrDefault(item => item.Path == "/api/orgrow/grow/isalive")
        ?? throw new InvalidOperationException("expected isalive endpoint to be discovered through http fallback");

    if (endpoint.StatusCode != 200)
    {
        throw new InvalidOperationException($"expected localhost fallback endpoint status 200, got {endpoint.StatusCode}");
    }

    if (endpoint.Scheme != "http")
    {
        throw new InvalidOperationException($"expected fallback endpoint scheme http, got {endpoint.Scheme}");
    }

    if (endpoint.Port is null)
    {
        throw new InvalidOperationException("expected fallback endpoint to preserve local port");
    }

    if (endpoint.Classification != "likely_exists" || endpoint.Confidence < 60)
    {
        throw new InvalidOperationException($"expected likely_exists with confidence >= 60, got {endpoint.Classification} {endpoint.Confidence}");
    }
}

static async Task TestBatteryCapsResponseBodyCapture()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var endpoint = new Endpoint
    {
        Id = "endpoint_large",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/large",
        Classification = "likely_exists",
        Confidence = 65
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "large-body",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["authConsistency"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (Encoding.UTF8.GetByteCount(result.ResultBody) > 100 * 1024)
    {
        throw new InvalidOperationException($"expected capped resultBody <= 100 KB, got {Encoding.UTF8.GetByteCount(result.ResultBody)} bytes");
    }

    if (!result.Evidence.Contains("truncated", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected evidence to mention truncated response capture");
    }
}

static async Task TestBatteryRoutesBodyTestsByHttpVerb()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var getEndpoint = new Endpoint
    {
        Id = "endpoint_get",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/orgrow/environment/setup/{id}/{id}",
        Classification = "likely_exists",
        Confidence = 65
    };
    var postEndpoint = new Endpoint
    {
        Id = "endpoint_post",
        Method = "POST",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/orgrow/product/create",
        Classification = "likely_exists",
        Confidence = 65
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "verb-routing",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [getEndpoint, postEndpoint], new TestRunRequest
    {
        BendTypes = ["fieldSize", "requestSize", "massAssignment", "idMutation", "authConsistency"],
        FieldSizesKb = [8],
        BodySizesKb = [12],
        ParallelWorkers = 1
    }, CancellationToken.None);

    if (results.Results.Any(result => result.EndpointId == getEndpoint.Id && result.BendType is "fieldSize" or "requestSize" or "massAssignment"))
    {
        throw new InvalidOperationException("expected body-oriented tests to be skipped for GET endpoints");
    }

    if (!results.Results.Any(result => result.EndpointId == getEndpoint.Id && result.BendType == "idMutation"))
    {
        throw new InvalidOperationException("expected idMutation to run for GET endpoint with path identifiers");
    }

    var postFieldSize = results.Results.SingleOrDefault(result => result.EndpointId == postEndpoint.Id && result.BendType == "fieldSize")
        ?? throw new InvalidOperationException("expected fieldSize to run for POST endpoint");

    if (postFieldSize.Request.BodySizeBytes < 8 * 1024)
    {
        throw new InvalidOperationException($"expected fieldSize body to use configured KB size, got {postFieldSize.Request.BodySizeBytes} bytes");
    }
}

static async Task TestBatteryMapsResultsToOwaspCategories()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var endpoint = new Endpoint
    {
        Id = "endpoint_owasp",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/orgrow/environment/setup/{id}/{id}",
        Classification = "likely_exists",
        Confidence = 65
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "owasp-mapping",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["idMutation", "authConsistency"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    if (results.Results.Any(result => string.IsNullOrWhiteSpace(result.OwaspCategory)))
    {
        throw new InvalidOperationException("expected every result to include an OWASP category");
    }

    if (results.Results.Any(result => string.IsNullOrWhiteSpace(result.Recommendation)))
    {
        throw new InvalidOperationException("expected every result to include a recommendation");
    }

    if (!results.Results.Any(result => result.BendType == "idMutation" && result.OwaspCategory.StartsWith("API1:", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("expected idMutation to map to OWASP API1");
    }
}

static async Task SecurityHeadersValidatorRecordsMisconfigurationEvidence()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var endpoint = new Endpoint
    {
        Id = "endpoint_headers",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/users",
        Classification = "likely_exists",
        Confidence = 65
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "security-headers",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["securityHeaders"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (result.OwaspCategory != "API8: Security Misconfiguration")
    {
        throw new InvalidOperationException($"expected securityHeaders to map to API8, got {result.OwaspCategory}");
    }

    if (!result.Evidence.Contains("missing X-Content-Type-Options", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected securityHeaders evidence to mention missing X-Content-Type-Options");
    }

    if (result.Result.HeadersMasked.Count == 0)
    {
        throw new InvalidOperationException("expected securityHeaders to persist response headers");
    }
}

static async Task AuthBoundaryValidatorSendsMalformedJwtProbes()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var endpoint = new Endpoint
    {
        Id = "endpoint_auth_boundary",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/users",
        Classification = "protected",
        Confidence = 80,
        AuthRequired = true
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "auth-boundary",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "jwt" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["jwtAnalysis"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (!result.Request.HeadersMasked.TryGetValue("Authorization", out var masked) || masked != "Bearer ***")
    {
        throw new InvalidOperationException("expected jwtAnalysis to record masked Authorization header");
    }

    if (!result.Evidence.Contains("malformed bearer token", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected jwtAnalysis evidence to mention malformed bearer token");
    }

    if (result.Risk < 9 || !result.Interesting)
    {
        throw new InvalidOperationException("expected protected endpoint accepting malformed JWT to be high risk");
    }
}

static async Task IdMutationDetectsQueryIdentifierParameters()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var endpoint = new Endpoint
    {
        Id = "endpoint_query_id",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/users",
        QueryParams = ["userId"],
        Classification = "likely_exists",
        Confidence = 65
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "query-id-mutation",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["idMutation"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.SingleOrDefault()
        ?? throw new InvalidOperationException("expected idMutation to run for query identifier parameter");

    if (!result.Url.Contains("userId=2", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"expected mutated query identifier in URL, got {result.Url}");
    }

    if (!result.OwaspCategory.StartsWith("API1:", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("expected query idMutation to map to OWASP API1");
    }
}

static async Task InventoryValidatorFlagsLiveLegacyRoutes()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var endpoint = new Endpoint
    {
        Id = "endpoint_legacy_inventory",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/v1/legacy/users",
        Source = ["customApiList"],
        Classification = "likely_exists",
        Confidence = 70
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "inventory-exposure",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["inventoryExposure"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.SingleOrDefault()
        ?? throw new InvalidOperationException("expected inventoryExposure to run for legacy route");

    if (!result.OwaspCategory.StartsWith("API9:", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"expected inventoryExposure to map to API9, got {result.OwaspCategory}");
    }

    if (!result.Evidence.Contains("legacy route", StringComparison.OrdinalIgnoreCase) ||
        !result.Evidence.Contains("versioned API route", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected inventoryExposure evidence to include legacy and versioned route signals");
    }

    if (result.Risk < 7 || !result.Interesting)
    {
        throw new InvalidOperationException("expected live legacy route to be a finding");
    }
}

static async Task SensitiveDataValidatorReportsDataClassesWithoutValues()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var endpoint = new Endpoint
    {
        Id = "endpoint_sensitive_profile",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/profile",
        Classification = "likely_exists",
        Confidence = 70
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "sensitive-data",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["sensitiveDataExposure"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (!result.OwaspCategory.StartsWith("API3:", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"expected sensitiveDataExposure to map to API3, got {result.OwaspCategory}");
    }

    if (!result.Evidence.Contains("credential-shaped response field", StringComparison.OrdinalIgnoreCase) ||
        !result.Evidence.Contains("email address", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected sensitiveDataExposure evidence to report sensitive data classes");
    }

    if (result.Evidence.Contains("secret-token-value", StringComparison.OrdinalIgnoreCase) ||
        result.Evidence.Contains("alice@example.com", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected sensitiveDataExposure evidence to avoid copying sensitive values");
    }

    if (!result.SensitiveDataDetected || result.Risk < 8)
    {
        throw new InvalidOperationException("expected credential-shaped exposure to be high risk and marked sensitive");
    }
}

static async Task RateLimitValidatorDetectsThrottledBurst()
{
    await using var server = await LocalApiServer.StartAsync();
    var endpoint = EndpointFor(server, "endpoint_rate_limit_throttled", "/api/rate-limit/throttled");

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "rate-limit-throttled",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["rateLimit"],
        MaxRequestsPerEndpoint = 5,
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (result.Risk >= 5 || result.Interesting)
    {
        throw new InvalidOperationException("expected throttled burst with headers and healthy post-burst response to be low risk");
    }

    if (!result.Evidence.Contains("throttling headers observed", StringComparison.OrdinalIgnoreCase) ||
        !result.Evidence.Contains("429", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected rateLimit evidence to mention 429 and throttling headers");
    }
}

static async Task RateLimitValidatorDetectsMissingThrottling()
{
    await using var server = await LocalApiServer.StartAsync();
    var endpoint = EndpointFor(server, "endpoint_rate_limit_missing", "/api/rate-limit/open");

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "rate-limit-missing",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["rateLimit"],
        MaxRequestsPerEndpoint = 4,
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (result.Risk < 5 || !result.Interesting)
    {
        throw new InvalidOperationException("expected missing throttling to be suspicious");
    }

    if (!result.Evidence.Contains("no HTTP 429 observed", StringComparison.OrdinalIgnoreCase) ||
        !result.Evidence.Contains("no throttling headers observed", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected rateLimit evidence to mention missing 429 and headers");
    }
}

static async Task RateLimitValidatorDetectsDegradedPostBurstResponse()
{
    await using var server = await LocalApiServer.StartAsync();
    var endpoint = EndpointFor(server, "endpoint_rate_limit_degraded", "/api/rate-limit/degraded");

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "rate-limit-degraded",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["rateLimit"],
        MaxRequestsPerEndpoint = 3,
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (result.Risk < 6 || !result.Interesting)
    {
        throw new InvalidOperationException("expected degraded post-burst behavior to be medium risk");
    }

    if (!result.Evidence.Contains("post-burst response differed", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected rateLimit evidence to mention degraded post-burst response");
    }
}

static async Task RateLimitValidatorCapsRequestedBurstSize()
{
    await using var server = await LocalApiServer.StartAsync();
    var endpoint = EndpointFor(server, "endpoint_rate_limit_cap", "/api/rate-limit/cap");

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "rate-limit-cap",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["rateLimit"],
        MaxRequestsPerEndpoint = 50,
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (!result.Mutation.TryGetValue("burstRequests", out var burstRequests) || Convert.ToInt32(burstRequests) != 10)
    {
        throw new InvalidOperationException("expected rateLimit burstRequests mutation value to be capped at 10");
    }

}

static async Task ContentTypeValidatorAcceptsSecureJsonOnlyEndpoint()
{
    await using var server = await LocalApiServer.StartAsync();
    var endpoint = EndpointFor(server, "endpoint_content_type_strict", "/api/content-type/strict", "POST");

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "content-type-strict",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["contentTypeValidation"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (result.Risk >= 5 || result.Interesting)
    {
        throw new InvalidOperationException("expected strict content-type enforcement to be low risk");
    }

    if (!result.Evidence.Contains("text/plain JSON was rejected", StringComparison.OrdinalIgnoreCase) ||
        !result.Evidence.Contains("missing Content-Type JSON was rejected", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected contentTypeValidation evidence to mention both invalid content-type rejections");
    }

    if (!result.Mutation.TryGetValue("baselineStatus", out var baselineStatus) || Convert.ToInt32(baselineStatus) != 200 ||
        !result.Mutation.TryGetValue("textPlainStatus", out var textPlainStatus) || Convert.ToInt32(textPlainStatus) != 415 ||
        !result.Mutation.TryGetValue("missingContentTypeStatus", out var missingStatus) || Convert.ToInt32(missingStatus) != 415)
    {
        throw new InvalidOperationException("expected contentTypeValidation mutation to record baseline, text/plain, and missing content-type statuses");
    }
}

static async Task ContentTypeValidatorFlagsTextPlainJsonAcceptance()
{
    await using var server = await LocalApiServer.StartAsync();
    var endpoint = EndpointFor(server, "endpoint_content_type_text_plain", "/api/content-type/text-plain-accepted", "POST");

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "content-type-text-plain",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["contentTypeValidation"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (result.Risk < 5 || !result.Interesting)
    {
        throw new InvalidOperationException("expected text/plain JSON acceptance to be a content-type finding");
    }

    if (!result.Evidence.Contains("text/plain JSON was accepted", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected contentTypeValidation evidence to mention text/plain acceptance");
    }

    if (!result.Request.HeadersMasked.TryGetValue("Content-Type", out var contentType) || contentType != "text/plain")
    {
        throw new InvalidOperationException("expected representative contentTypeValidation request header to be text/plain");
    }

    if (string.IsNullOrWhiteSpace(result.OwaspCategory) || !result.OwaspCategory.StartsWith("API8:", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("expected contentTypeValidation to map to OWASP API8");
    }
}

static async Task ContentTypeValidatorFlagsMissingContentTypeAcceptance()
{
    await using var server = await LocalApiServer.StartAsync();
    var endpoint = EndpointFor(server, "endpoint_content_type_missing", "/api/content-type/missing-accepted", "POST");

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "content-type-missing",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["contentTypeValidation"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (result.Risk < 5 || !result.Interesting)
    {
        throw new InvalidOperationException("expected missing Content-Type JSON acceptance to be a content-type finding");
    }

    if (!result.Evidence.Contains("missing Content-Type JSON was accepted", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected contentTypeValidation evidence to mention missing Content-Type acceptance");
    }

    if (!result.Request.HeadersMasked.TryGetValue("Content-Type", out var contentType) || contentType != "<missing>")
    {
        throw new InvalidOperationException("expected representative contentTypeValidation request header to show missing Content-Type");
    }

    if (!result.Mutation.TryGetValue("missingContentTypeStatus", out var missingStatus) || Convert.ToInt32(missingStatus) != 200)
    {
        throw new InvalidOperationException("expected contentTypeValidation mutation to record missing content-type acceptance status");
    }
}

static async Task ContentTypeValidatorSkipsGetEndpoints()
{
    await using var server = await LocalApiServer.StartAsync();
    var endpoint = EndpointFor(server, "endpoint_content_type_get", "/api/content-type/strict");

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "content-type-get-skip",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["contentTypeValidation"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    if (results.Results.Count != 0)
    {
        throw new InvalidOperationException("expected contentTypeValidation to be skipped for GET endpoints");
    }
}

static void AssertContains(IEnumerable<string> lines, string expected)
{
    if (!lines.Any(line => string.Equals(line.Trim(), expected, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException($"expected api-list.txt to contain '{expected}'");
    }
}

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("unable to locate repository root");
}

static Endpoint EndpointFor(LocalApiServer server, string id, string path, string method = "GET")
{
    var uri = new Uri(server.BaseUrl);
    return new Endpoint
    {
        Id = id,
        Method = method,
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = path,
        Classification = "likely_exists",
        Confidence = 70
    };
}

sealed class TestEnvironment(string contentRootPath) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "BendIt.Api.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = contentRootPath;
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
}

sealed class LocalApiServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, int> _requestCounts = new(StringComparer.Ordinal);
    private readonly Task _loop;

    private LocalApiServer(TcpListener listener, string baseUrl)
    {
        _listener = listener;
        BaseUrl = baseUrl;
        _loop = Task.Run(ServeAsync);
    }

    public string BaseUrl { get; }

    public static Task<LocalApiServer> StartAsync()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var baseUrl = $"http://127.0.0.1:{port}";
        return Task.FromResult(new LocalApiServer(listener, baseUrl));
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (SocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _stop.Dispose();
        }
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = Task.Run(() => RespondAsync(client), _stop.Token);
        }
    }

    private async Task RespondAsync(TcpClient client)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync() ?? "";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? headerLine;
        while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
        {
            var separator = headerLine.IndexOf(':');
            if (separator > 0)
            {
                headers[headerLine[..separator].Trim()] = headerLine[(separator + 1)..].Trim();
            }
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var method = parts.Length >= 1 ? parts[0] : "";
        var path = parts.Length >= 2 ? parts[1].Split('?', 2)[0] : "/";
        var count = CountRequest(path);
        if (path == "/api/orgrow/grow/isalive")
        {
            await WriteAsync(stream, 200, "true", "text/plain");
            return;
        }

        if (path == "/api/profile")
        {
            await WriteAsync(stream, 200, """{"email":"alice@example.com","access_token":"secret-token-value"}""", "application/json");
            return;
        }

        if (path is "/api/users" or "/api/orders" or "/api/items" or "/api/health" or "/health" or "/api/v1/legacy/users")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path is "/api/orgrow/environment/setup/1/1" or "/api/orgrow/environment/setup/2/2" or "/api/orgrow/product/create")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path == "/api/content-type/strict")
        {
            await WriteContentTypeResponseAsync(stream, method, headers, acceptTextPlain: false, acceptMissing: false);
            return;
        }

        if (path == "/api/content-type/text-plain-accepted")
        {
            await WriteContentTypeResponseAsync(stream, method, headers, acceptTextPlain: true, acceptMissing: false);
            return;
        }

        if (path == "/api/content-type/missing-accepted")
        {
            await WriteContentTypeResponseAsync(stream, method, headers, acceptTextPlain: false, acceptMissing: true);
            return;
        }

        if (path == "/api/large")
        {
            await WriteAsync(stream, 200, new string('A', 130 * 1024), "text/plain");
            return;
        }

        if (path == "/api/rate-limit/throttled")
        {
            if (count is >= 3 and <= 6)
            {
                await WriteAsync(stream, 429, """{"error":"too many requests"}""", "application/json", new Dictionary<string, string>
                {
                    ["Retry-After"] = "1",
                    ["RateLimit-Limit"] = "2",
                    ["RateLimit-Remaining"] = "0"
                });
                return;
            }

            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path == "/api/rate-limit/open" || path == "/api/rate-limit/cap")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path == "/api/rate-limit/degraded")
        {
            var body = count >= 5 ? """{"ok":false,"mode":"degraded"}""" : """{"ok":true}""";
            await WriteAsync(stream, 200, body, "application/json");
            return;
        }

        await WriteAsync(stream, 404, """{"error":"not found"}""", "application/json");
    }

    private int CountRequest(string path)
    {
        lock (_requestCounts)
        {
            _requestCounts.TryGetValue(path, out var count);
            count++;
            _requestCounts[path] = count;
            return count;
        }
    }

    private static async Task WriteAsync(Stream stream, int status, string body, string contentType, Dictionary<string, string>? headers = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var reason = status switch
        {
            200 => "OK",
            429 => "Too Many Requests",
            _ => "Not Found"
        };
        var extraHeaders = headers is null || headers.Count == 0
            ? ""
            : string.Concat(headers.Select(header => $"{header.Key}: {header.Value}\r\n"));
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\n{extraHeaders}Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
    }

    private static async Task WriteContentTypeResponseAsync(Stream stream, string method, Dictionary<string, string> headers, bool acceptTextPlain, bool acceptMissing)
    {
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await WriteAsync(stream, 405, """{"error":"method not allowed"}""", "application/json");
            return;
        }

        headers.TryGetValue("Content-Type", out var contentType);
        var normalized = contentType?.Split(';', 2)[0].Trim();
        var accepted = string.Equals(normalized, "application/json", StringComparison.OrdinalIgnoreCase) ||
                       acceptTextPlain && string.Equals(normalized, "text/plain", StringComparison.OrdinalIgnoreCase) ||
                       acceptMissing && string.IsNullOrWhiteSpace(normalized);

        if (accepted)
        {
            await WriteAsync(stream, 200, """{"accepted":true}""", "application/json");
            return;
        }

        await WriteAsync(stream, 415, """{"error":"unsupported media type"}""", "application/json");
    }
}
