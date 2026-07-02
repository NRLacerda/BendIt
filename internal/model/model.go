package model

import "time"

type Project struct {
	ProjectID   string            `json:"projectId"`
	Name        string            `json:"name"`
	Description string            `json:"description,omitempty"`
	BaseURL     string            `json:"baseUrl"`
	Headers     map[string]string `json:"headers,omitempty"`
	Auth        AuthConfig        `json:"auth"`
	OutputDir   string            `json:"outputDir"`
	CreatedAt   time.Time         `json:"createdAt"`
	UpdatedAt   time.Time         `json:"updatedAt"`
}

type AuthConfig struct {
	Type          string              `json:"type"`
	HeaderName    string              `json:"headerName,omitempty"`
	Scheme        string              `json:"scheme,omitempty"`
	TokenMasked   string              `json:"tokenMasked,omitempty"`
	CookieMasked  string              `json:"cookieMasked,omitempty"`
	HeadersMasked []MaskedHeaderValue `json:"headersMasked,omitempty"`
}

type MaskedHeaderValue struct {
	Name  string `json:"name"`
	Value string `json:"value"`
}

type Endpoint struct {
	ID               string    `json:"id"`
	Method           string    `json:"method"`
	Scheme           string    `json:"scheme"`
	Host             string    `json:"host"`
	Path             string    `json:"path"`
	QueryParams      []string  `json:"queryParams"`
	Source           []string  `json:"source"`
	SourceDetail     string    `json:"sourceDetail,omitempty"`
	AuthRequired     bool      `json:"authRequired"`
	Status           string    `json:"status"`
	StatusCode       int       `json:"statusCode,omitempty"`
	ContentType      string    `json:"contentType,omitempty"`
	ResponseLength   int64     `json:"responseLength,omitempty"`
	ResponseHash     string    `json:"responseHash,omitempty"`
	Confidence       int       `json:"confidence,omitempty"`
	Classification   string    `json:"classification,omitempty"`
	Protected        bool      `json:"protected,omitempty"`
	RedirectedTo     string    `json:"redirectedTo,omitempty"`
	RequestSchemaID  *string   `json:"requestSchemaId"`
	ResponseSchemaID *string   `json:"responseSchemaId"`
	FirstSeenAt      time.Time `json:"firstSeenAt"`
	LastSeenAt       time.Time `json:"lastSeenAt"`
	VerifiedAt       time.Time `json:"verifiedAt,omitempty"`
}

type EndpointsDocument struct {
	GeneratedAt time.Time  `json:"generatedAt"`
	Endpoints   []Endpoint `json:"endpoints"`
}

type DiscoveryRequest struct {
	UseNativeAPIList bool     `json:"useNativeApiList"`
	UseSpecDiscovery bool     `json:"useSpecDiscovery"`
	APIList          []string `json:"apiList"`
	MaxWorkers       int      `json:"maxWorkers,omitempty"`
	TimeoutSeconds   int      `json:"timeoutSeconds,omitempty"`
	MaxBodyBytes     int64    `json:"maxBodyBytes,omitempty"`
	FollowRedirects  bool     `json:"followRedirects,omitempty"`
}

type TestRunRequest struct {
	BendTypes              []string `json:"bendTypes"`
	MaxRequestsPerEndpoint int      `json:"maxRequestsPerEndpoint"`
	ParallelWorkers        int      `json:"parallelWorkers"`
	FieldSizesKB           []int    `json:"fieldSizesKb"`
	BodySizesKB            []int    `json:"bodySizesKb"`
	ExcludedPathPatterns   []string `json:"excludedPathPatterns"`
}

type RunRequest struct {
	Discovery DiscoveryRequest `json:"discovery"`
	Tests     TestRunRequest   `json:"tests"`
}

type RunDocument struct {
	RunID         string     `json:"runId"`
	ProjectID     string     `json:"projectId"`
	Status        string     `json:"status"`
	CurrentStep   string     `json:"currentStep"`
	Progress      int        `json:"progress"`
	StartedAt     time.Time  `json:"startedAt"`
	CompletedAt   *time.Time `json:"completedAt,omitempty"`
	EndpointCount int        `json:"endpointCount"`
	ResultCount   int        `json:"resultCount"`
	FindingCount  int        `json:"findingCount"`
	Error         string     `json:"error,omitempty"`
}

type TestResult struct {
	ID                    string         `json:"id"`
	ProjectID             string         `json:"projectId"`
	TestRunID             string         `json:"testRunId"`
	EndpointID            string         `json:"endpointId"`
	BendType              string         `json:"bendType"`
	Category              string         `json:"category"`
	Title                 string         `json:"title"`
	Method                string         `json:"method"`
	URL                   string         `json:"url"`
	OriginalURL           string         `json:"originalUrl"`
	Mutation              map[string]any `json:"mutation"`
	Request               ResultRequest  `json:"request"`
	Result                HTTPResult     `json:"result"`
	ResultBody            string         `json:"resultBody"`
	Evidence              string         `json:"evidence"`
	Outcome               string         `json:"outcome"`
	Interesting           bool           `json:"interesting"`
	AnalysisSummary       string         `json:"analysisSummary"`
	Risk                  int            `json:"risk"`
	Severity              string         `json:"severity"`
	Confidence            int            `json:"confidence"`
	Reproducible          bool           `json:"reproducible"`
	SensitiveDataDetected bool           `json:"sensitiveDataDetected"`
	TokenUsed             bool           `json:"tokenUsed"`
	AuthContext           AuthConfig     `json:"authContext"`
	CreatedAt             time.Time      `json:"createdAt"`
}

type ResultRequest struct {
	HeadersMasked map[string]string `json:"headersMasked"`
	Body          *string           `json:"body"`
	BodySizeBytes int               `json:"bodySizeBytes"`
}

type HTTPResult struct {
	StatusCode    int    `json:"statusCode"`
	StatusText    string `json:"statusText"`
	ContentType   string `json:"contentType"`
	BodySizeBytes int    `json:"bodySizeBytes"`
	DurationMS    int    `json:"durationMs"`
}

type ResultsDocument struct {
	GeneratedAt time.Time    `json:"generatedAt"`
	ProjectID   string       `json:"projectId"`
	Results     []TestResult `json:"results"`
}
