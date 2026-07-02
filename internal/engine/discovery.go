package engine

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"io"
	"net/http"
	"net/url"
	"sort"
	"strings"
	"sync"
	"time"

	"bendit/internal/model"
	"bendit/internal/resources"

	"github.com/getkin/kin-openapi/openapi3"
)

const (
	classConfirmed        = "confirmed"
	classLikelyExists     = "likely_exists"
	classProtected        = "protected"
	classMethodNotAllowed = "method_not_allowed"
	classMaybeExists      = "maybe_exists"
	classNotFound         = "not_found"
	classSoft404          = "soft_404"
	classRateLimited      = "rate_limited"
	classServerError      = "server_error"
	classUnknown          = "unknown"
)

type Discoverer struct {
	mu                 sync.Mutex
	registeredEndpoint map[string]bool
	endpoints          []model.Endpoint
}

type endpointCandidate struct {
	Method       string
	URL          string
	Source       []string
	SourceDetail string
	Confidence   int
	Verified     bool
}

type discoveryHTTPResult struct {
	Method         string
	URL            string
	StatusCode     int
	ContentType    string
	Body           []byte
	ResponseLength int64
	ResponseHash   string
	RedirectedTo   string
	Err            error
}

type baselineFingerprint struct {
	Samples []discoveryHTTPResult
}

func NewDiscoverer(existing []model.Endpoint) *Discoverer {
	d := &Discoverer{
		registeredEndpoint: make(map[string]bool),
		endpoints:          append([]model.Endpoint(nil), existing...),
	}
	for _, endpoint := range existing {
		d.registeredEndpoint[endpoint.ID] = true
	}
	return d
}

func (d *Discoverer) Run(project model.Project, req model.DiscoveryRequest) model.EndpointsDocument {
	config := normalizeDiscoveryRequest(req)
	client := discoveryClient(config)
	baseline := d.buildBaseline(project.BaseURL, client, config)

	if config.UseSpecDiscovery {
		specCandidates := d.probeSpecRoutes(project.BaseURL, client, config, baseline)
		d.verifyAndRegister(project, client, config, baseline, specCandidates)
	}

	wordlistCandidates := d.wordlistCandidates(project.BaseURL, config)
	d.verifyAndRegister(project, client, config, baseline, wordlistCandidates)

	if len(d.endpoints) == 0 {
		fallback := model.DiscoveryRequest{APIList: []string{"GET /health", "GET /openapi.json"}}
		d.verifyAndRegister(project, client, config, baseline, d.wordlistCandidates(project.BaseURL, fallback))
	}

	return model.EndpointsDocument{
		GeneratedAt: time.Now().UTC(),
		Endpoints:   d.endpoints,
	}
}

func normalizeDiscoveryRequest(req model.DiscoveryRequest) model.DiscoveryRequest {
	if req.MaxWorkers <= 0 || req.MaxWorkers > 32 {
		req.MaxWorkers = 6
	}
	if req.TimeoutSeconds <= 0 || req.TimeoutSeconds > 60 {
		req.TimeoutSeconds = 10
	}
	if req.MaxBodyBytes <= 0 || req.MaxBodyBytes > 5*1024*1024 {
		req.MaxBodyBytes = 1024 * 1024
	}
	return req
}

func discoveryClient(req model.DiscoveryRequest) *http.Client {
	client := &http.Client{Timeout: time.Duration(req.TimeoutSeconds) * time.Second}
	if !req.FollowRedirects {
		client.CheckRedirect = func(_ *http.Request, _ []*http.Request) error {
			return http.ErrUseLastResponse
		}
	}
	return client
}

func (d *Discoverer) buildBaseline(baseURL string, client *http.Client, req model.DiscoveryRequest) baselineFingerprint {
	suffix := shortHash(baseURL + time.Now().UTC().Format(time.RFC3339Nano))
	paths := []string{
		"/__bendit_random_" + suffix,
		"/api/__bendit_random_" + suffix,
		"/random-not-found-bendit-" + suffix,
	}
	samples := make([]discoveryHTTPResult, 0, len(paths))
	for _, path := range paths {
		if fullURL, ok := resolveCandidateURL(baseURL, path); ok {
			samples = append(samples, executeDiscoveryRequest(client, http.MethodGet, fullURL, req.MaxBodyBytes))
		}
	}
	return baselineFingerprint{Samples: samples}
}

func (d *Discoverer) probeSpecRoutes(baseURL string, client *http.Client, req model.DiscoveryRequest, baseline baselineFingerprint) []endpointCandidate {
	candidates := []endpointCandidate{}
	for _, line := range resources.NativeSpecList() {
		method, route := parseAPIListLine(line)
		if method == "" || route == "" {
			continue
		}
		fullURL, ok := resolveCandidateURL(baseURL, route)
		if !ok {
			continue
		}
		result := executeDiscoveryRequest(client, http.MethodGet, fullURL, req.MaxBodyBytes)
		classification, confidence := classifyDiscoveryResult(result, "openApiSpecProbe", baseline, false)
		if classification != classNotFound && classification != classSoft404 && classification != classUnknown {
			d.register(endpointCandidate{
				Method:       http.MethodGet,
				URL:          fullURL,
				Source:       []string{"openApiSpecProbe"},
				SourceDetail: route,
				Confidence:   confidence,
				Verified:     true,
			}, projectlessAuthRequired(classification), result, classification, confidence)
		}
		if result.Err == nil && looksLikeOpenAPISpec(result.Body) {
			extracted := extractOpenAPIEndpoints(baseURL, fullURL, result.Body)
			candidates = append(candidates, extracted...)
		}
	}
	return candidates
}

func (d *Discoverer) wordlistCandidates(baseURL string, req model.DiscoveryRequest) []endpointCandidate {
	lines := []string{}
	if req.UseNativeAPIList {
		lines = append(lines, resources.NativeAPIList()...)
	}
	lines = append(lines, req.APIList...)
	if len(lines) == 0 && !req.UseSpecDiscovery {
		lines = append(lines, "GET /health", "GET /openapi.json")
	}

	candidates := []endpointCandidate{}
	for _, line := range lines {
		method, route := parseAPIListLine(line)
		if route == "" {
			continue
		}
		fullURL, ok := resolveCandidateURL(baseURL, route)
		if !ok {
			continue
		}
		source := "customApiList"
		if containsLine(resources.NativeAPIList(), line) {
			source = "nativeApiList"
		}
		candidates = append(candidates, endpointCandidate{
			Method:       method,
			URL:          fullURL,
			Source:       []string{source},
			SourceDetail: route,
			Confidence:   40,
		})
	}
	return candidates
}

func (d *Discoverer) verifyAndRegister(project model.Project, client *http.Client, req model.DiscoveryRequest, baseline baselineFingerprint, candidates []endpointCandidate) {
	if len(candidates) == 0 {
		return
	}

	jobs := make(chan endpointCandidate)
	results := make(chan verifiedCandidate)

	var wg sync.WaitGroup
	for i := 0; i < req.MaxWorkers; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for candidate := range jobs {
				results <- d.verifyCandidate(client, req, baseline, candidate)
			}
		}()
	}

	go func() {
		for _, candidate := range candidates {
			jobs <- candidate
		}
		close(jobs)
		wg.Wait()
		close(results)
	}()

	authRequired := project.Auth.Type != "none"
	for result := range results {
		d.register(result.Candidate, authRequired || projectlessAuthRequired(result.Classification), result.HTTPResult, result.Classification, result.Confidence)
	}
}

type verifiedCandidate struct {
	Candidate      endpointCandidate
	HTTPResult     discoveryHTTPResult
	Classification string
	Confidence     int
}

func (d *Discoverer) verifyCandidate(client *http.Client, req model.DiscoveryRequest, baseline baselineFingerprint, candidate endpointCandidate) verifiedCandidate {
	if candidate.Verified {
		return verifiedCandidate{Candidate: candidate, Classification: classConfirmed, Confidence: candidate.Confidence}
	}
	if !isSafeDiscoveryMethod(candidate.Method) {
		if firstSource(candidate.Source) == "openapi" {
			return verifiedCandidate{Candidate: candidate, Classification: classConfirmed, Confidence: max(candidate.Confidence, 100)}
		}
		return verifiedCandidate{Candidate: candidate, Classification: classUnknown, Confidence: max(candidate.Confidence, 40)}
	}

	result := executeDiscoveryRequest(client, candidate.Method, candidate.URL, req.MaxBodyBytes)
	classification, confidence := classifyDiscoveryResult(result, firstSource(candidate.Source), baseline, firstSource(candidate.Source) == "openapi")
	if candidate.Confidence > confidence {
		confidence = candidate.Confidence
	}
	return verifiedCandidate{
		Candidate:      candidate,
		HTTPResult:     result,
		Classification: classification,
		Confidence:     confidence,
	}
}

func executeDiscoveryRequest(client *http.Client, method, rawURL string, maxBodyBytes int64) discoveryHTTPResult {
	result := discoveryHTTPResult{Method: method, URL: rawURL}
	request, err := http.NewRequest(method, rawURL, nil)
	if err != nil {
		result.Err = err
		return result
	}
	request.Header.Set("Accept", "application/json, application/yaml, text/yaml, text/html;q=0.8, */*;q=0.5")
	request.Header.Set("User-Agent", "BendIt discovery")

	response, err := client.Do(request)
	if err != nil {
		result.Err = err
		return result
	}
	defer response.Body.Close()

	body, _ := io.ReadAll(io.LimitReader(response.Body, maxBodyBytes))
	result.StatusCode = response.StatusCode
	result.ContentType = response.Header.Get("Content-Type")
	result.Body = body
	result.ResponseLength = int64(len(body))
	result.ResponseHash = bodyHash(body)
	result.RedirectedTo = response.Header.Get("Location")
	return result
}

func classifyDiscoveryResult(result discoveryHTTPResult, source string, baseline baselineFingerprint, fromOpenAPI bool) (string, int) {
	if result.Err != nil {
		return classUnknown, 10
	}
	if looksLikeSoft404(result, baseline) {
		return classSoft404, 20
	}

	switch {
	case result.StatusCode >= 200 && result.StatusCode <= 204:
		if fromOpenAPI {
			return classConfirmed, 100
		}
		if strings.Contains(strings.ToLower(result.ContentType), "json") {
			return classConfirmed, 80
		}
		if source == "openApiSpecProbe" {
			return classLikelyExists, 75
		}
		return classLikelyExists, 65
	case result.StatusCode >= 300 && result.StatusCode < 400:
		return classLikelyExists, 55
	case result.StatusCode == 400:
		return classMaybeExists, 40
	case result.StatusCode == 401 || result.StatusCode == 403:
		return classProtected, 70
	case result.StatusCode == 404:
		return classNotFound, 0
	case result.StatusCode == 405:
		return classMethodNotAllowed, 65
	case result.StatusCode == 429:
		return classRateLimited, 30
	case result.StatusCode >= 500:
		return classServerError, 30
	default:
		return classUnknown, 15
	}
}

func looksLikeSoft404(result discoveryHTTPResult, baseline baselineFingerprint) bool {
	if result.Err != nil || len(baseline.Samples) == 0 {
		return false
	}
	for _, sample := range baseline.Samples {
		if sample.Err != nil {
			continue
		}
		if result.StatusCode != sample.StatusCode {
			continue
		}
		if result.ResponseHash != "" && result.ResponseHash == sample.ResponseHash {
			return true
		}
		if result.ContentType != "" && sample.ContentType != "" && contentTypeFamily(result.ContentType) == contentTypeFamily(sample.ContentType) {
			diff := result.ResponseLength - sample.ResponseLength
			if diff < 0 {
				diff = -diff
			}
			if diff <= 128 {
				return true
			}
		}
	}
	return false
}

func contentTypeFamily(value string) string {
	value = strings.ToLower(strings.TrimSpace(strings.Split(value, ";")[0]))
	return value
}

func looksLikeOpenAPISpec(body []byte) bool {
	trimmed := bytes.TrimSpace(body)
	if len(trimmed) == 0 {
		return false
	}
	lower := bytes.ToLower(trimmed)
	return bytes.Contains(lower, []byte("openapi")) || bytes.Contains(lower, []byte("swagger")) || bytes.Contains(lower, []byte("paths:")) || bytes.Contains(lower, []byte(`"paths"`))
}

func extractOpenAPIEndpoints(baseURL, specURL string, body []byte) []endpointCandidate {
	loader := openapi3.NewLoader()
	doc, err := loader.LoadFromData(body)
	if err != nil || doc == nil || doc.Paths == nil {
		return nil
	}

	serverBase := baseURL
	if len(doc.Servers) > 0 && doc.Servers[0] != nil && doc.Servers[0].URL != "" {
		if resolved, ok := resolveSpecServer(baseURL, specURL, doc.Servers[0].URL); ok {
			serverBase = resolved
		}
	}

	candidates := []endpointCandidate{}
	paths := doc.Paths.Map()
	for route, item := range paths {
		if item == nil {
			continue
		}
		for method := range item.Operations() {
			fullURL, ok := resolveCandidateURL(serverBase, instantiateOpenAPIPath(route))
			if !ok {
				continue
			}
			candidates = append(candidates, endpointCandidate{
				Method:       method,
				URL:          fullURL,
				Source:       []string{"openapi"},
				SourceDetail: specURL,
				Confidence:   100,
			})
		}
	}
	return candidates
}

func resolveSpecServer(baseURL, specURL, serverURL string) (string, bool) {
	if parsed, err := url.Parse(serverURL); err == nil && parsed.Scheme != "" && parsed.Host != "" {
		return parsed.String(), true
	}
	anchor := baseURL
	if strings.HasPrefix(serverURL, ".") {
		anchor = specURL
	}
	return resolveCandidateURL(anchor, serverURL)
}

func (d *Discoverer) register(candidate endpointCandidate, authRequired bool, result discoveryHTTPResult, classification string, confidence int) (model.Endpoint, bool) {
	normalized, ok := NormalizeEndpoint(candidate.Method, candidate.URL)
	if !ok {
		return model.Endpoint{}, false
	}

	now := time.Now().UTC()
	endpoint := model.Endpoint{
		ID:               normalized.ID,
		Method:           normalized.Method,
		Scheme:           normalized.Scheme,
		Host:             normalized.Host,
		Path:             normalized.Path,
		QueryParams:      normalized.QueryParams,
		Source:           candidate.Source,
		SourceDetail:     candidate.SourceDetail,
		AuthRequired:     authRequired,
		Status:           "processed",
		StatusCode:       result.StatusCode,
		ContentType:      result.ContentType,
		ResponseLength:   result.ResponseLength,
		ResponseHash:     result.ResponseHash,
		Confidence:       confidence,
		Classification:   classification,
		Protected:        classification == classProtected,
		RedirectedTo:     result.RedirectedTo,
		RequestSchemaID:  nil,
		ResponseSchemaID: nil,
		FirstSeenAt:      now,
		LastSeenAt:       now,
		VerifiedAt:       now,
	}

	d.mu.Lock()
	defer d.mu.Unlock()

	if d.registeredEndpoint[endpoint.ID] {
		return model.Endpoint{}, false
	}
	d.registeredEndpoint[endpoint.ID] = true
	d.endpoints = append(d.endpoints, endpoint)
	return endpoint, true
}

func FilterTestableEndpoints(endpoints []model.Endpoint) []model.Endpoint {
	filtered := make([]model.Endpoint, 0, len(endpoints))
	for _, endpoint := range endpoints {
		switch endpoint.Classification {
		case "", classConfirmed, classProtected, classMethodNotAllowed:
			if endpoint.Classification == "" || endpoint.Confidence >= 60 || firstSource(endpoint.Source) == "openapi" {
				filtered = append(filtered, endpoint)
			}
		}
	}
	return filtered
}

func parseAPIListLine(line string) (method, route string) {
	line = strings.TrimSpace(line)
	if line == "" || strings.HasPrefix(line, "#") {
		return "", ""
	}
	fields := strings.Fields(line)
	if len(fields) >= 2 && isHTTPMethod(fields[0]) {
		return strings.ToUpper(fields[0]), fields[1]
	}
	return http.MethodGet, fields[0]
}

func isHTTPMethod(value string) bool {
	switch strings.ToUpper(strings.TrimSpace(value)) {
	case http.MethodGet, http.MethodPost, http.MethodPut, http.MethodPatch, http.MethodDelete, http.MethodHead, http.MethodOptions:
		return true
	default:
		return false
	}
}

func isSafeDiscoveryMethod(method string) bool {
	switch strings.ToUpper(strings.TrimSpace(method)) {
	case http.MethodGet, http.MethodHead, http.MethodOptions:
		return true
	default:
		return false
	}
}

type NormalizedEndpoint struct {
	ID          string
	Method      string
	Scheme      string
	Host        string
	Path        string
	QueryParams []string
	HashInput   string
}

func NormalizeEndpoint(method, rawURL string) (NormalizedEndpoint, bool) {
	parsed, err := url.Parse(rawURL)
	if err != nil || parsed.Scheme == "" || parsed.Host == "" {
		return NormalizedEndpoint{}, false
	}

	method = strings.ToUpper(strings.TrimSpace(method))
	path := normalizePathTemplate(parsed.Path)
	queryParams := make([]string, 0, len(parsed.Query()))
	for key := range parsed.Query() {
		queryParams = append(queryParams, key)
	}
	sort.Strings(queryParams)

	hashInput := method + " " + strings.ToLower(parsed.Scheme) + "://" + strings.ToLower(parsed.Host) + path
	sum := sha256.Sum256([]byte(hashInput))

	return NormalizedEndpoint{
		ID:          "endpoint_" + hex.EncodeToString(sum[:])[:12],
		Method:      method,
		Scheme:      strings.ToLower(parsed.Scheme),
		Host:        strings.ToLower(parsed.Host),
		Path:        path,
		QueryParams: queryParams,
		HashInput:   hashInput,
	}, true
}

func normalizePathTemplate(path string) string {
	path = strings.TrimSpace(path)
	if path == "" {
		return "/"
	}
	if !strings.HasPrefix(path, "/") {
		path = "/" + path
	}
	for strings.Contains(path, "//") {
		path = strings.ReplaceAll(path, "//", "/")
	}
	if len(path) > 1 {
		path = strings.TrimRight(path, "/")
	}

	parts := strings.Split(path, "/")
	for i, part := range parts {
		if isNumeric(part) {
			parts[i] = "{id}"
			continue
		}
		if looksLikeHexID(part) {
			parts[i] = "{value}"
		}
	}
	return strings.Join(parts, "/")
}

func instantiateOpenAPIPath(path string) string {
	parts := strings.Split(path, "/")
	for i, part := range parts {
		if strings.HasPrefix(part, "{") && strings.HasSuffix(part, "}") {
			parts[i] = "123"
		}
	}
	return strings.Join(parts, "/")
}

func isNumeric(value string) bool {
	if value == "" {
		return false
	}
	for _, r := range value {
		if r < '0' || r > '9' {
			return false
		}
	}
	return true
}

func looksLikeHexID(value string) bool {
	if len(value) < 8 {
		return false
	}
	for _, r := range value {
		if !((r >= '0' && r <= '9') || (r >= 'a' && r <= 'f') || (r >= 'A' && r <= 'F') || r == '-') {
			return false
		}
	}
	return true
}

func resolveCandidateURL(baseURL, path string) (string, bool) {
	if parsed, err := url.Parse(path); err == nil && parsed.Scheme != "" && parsed.Host != "" {
		return parsed.String(), true
	}
	base, err := url.Parse(baseURL)
	if err != nil || base.Scheme == "" || base.Host == "" {
		return "", false
	}
	ref, err := url.Parse(path)
	if err != nil {
		return "", false
	}
	return base.ResolveReference(ref).String(), true
}

func bodyHash(body []byte) string {
	if len(body) == 0 {
		return ""
	}
	sum := sha256.Sum256(body)
	return hex.EncodeToString(sum[:])[:16]
}

func firstSource(sources []string) string {
	if len(sources) == 0 {
		return ""
	}
	return sources[0]
}

func containsLine(lines []string, value string) bool {
	for _, line := range lines {
		if strings.TrimSpace(line) == strings.TrimSpace(value) {
			return true
		}
	}
	return false
}

func projectlessAuthRequired(classification string) bool {
	return classification == classProtected
}

func max(a, b int) int {
	if a > b {
		return a
	}
	return b
}
