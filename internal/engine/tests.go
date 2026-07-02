package engine

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"strings"
	"sync"
	"time"

	"bendit/internal/model"
)

var DefaultBendTypes = []string{
	"authConsistency",
	"jwtAnalysis",
	"httpMethodValidation",
	"payloadValidation",
	"requestSize",
	"fieldSize",
	"massAssignment",
	"idMutation",
	"responseDiffing",
}

func RunTests(project model.Project, endpoints []model.Endpoint, req model.TestRunRequest) model.ResultsDocument {
	bendTypes := req.BendTypes
	if len(bendTypes) == 0 {
		bendTypes = DefaultBendTypes
	}
	workers := req.ParallelWorkers
	if workers <= 0 || workers > 32 {
		workers = 6
	}

	testRunID := "run_" + shortHash(project.ProjectID+time.Now().UTC().Format(time.RFC3339Nano))
	jobs := make(chan testJob)
	results := make(chan model.TestResult)

	var wg sync.WaitGroup
	for i := 0; i < workers; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for job := range jobs {
				results <- createResult(project, testRunID, job.endpoint, job.bendType)
			}
		}()
	}

	go func() {
		for _, endpoint := range endpoints {
			if excluded(endpoint.Path, req.ExcludedPathPatterns) {
				continue
			}
			for _, bendType := range bendTypes {
				jobs <- testJob{endpoint: endpoint, bendType: bendType}
			}
		}
		close(jobs)
		wg.Wait()
		close(results)
	}()

	out := model.ResultsDocument{
		GeneratedAt: time.Now().UTC(),
		ProjectID:   project.ProjectID,
		Results:     []model.TestResult{},
	}
	for result := range results {
		out.Results = append(out.Results, result)
	}
	return out
}

type testJob struct {
	endpoint model.Endpoint
	bendType string
}

func createResult(project model.Project, testRunID string, endpoint model.Endpoint, bendType string) model.TestResult {
	status := simulatedStatus(endpoint, bendType)
	resultBody := simulatedBody(endpoint, bendType, status)
	body := requestBody(bendType)
	risk := riskFor(bendType, status, endpoint)
	outcome := outcomeFor(bendType, status, risk)
	interesting := isInteresting(bendType, status, risk)
	now := time.Now().UTC()

	return model.TestResult{
		ID:          "result_" + shortHash(endpoint.ID+bendType+now.Format(time.RFC3339Nano)),
		ProjectID:   project.ProjectID,
		TestRunID:   testRunID,
		EndpointID:  endpoint.ID,
		BendType:    bendType,
		Category:    categoryFor(bendType),
		Title:       fmt.Sprintf("%s returned HTTP %d", bendType, status),
		Method:      endpoint.Method,
		URL:         endpoint.Scheme + "://" + endpoint.Host + endpoint.Path,
		OriginalURL: endpoint.Scheme + "://" + endpoint.Host + endpoint.Path,
		Mutation:    mutationFor(bendType),
		Request: model.ResultRequest{
			HeadersMasked: maskedHeaders(project),
			Body:          body,
			BodySizeBytes: stringSize(body),
		},
		Result: model.HTTPResult{
			StatusCode:    status,
			StatusText:    statusText(status),
			ContentType:   "application/json",
			BodySizeBytes: len(resultBody),
			DurationMS:    80 + int(now.UnixNano()%300),
		},
		ResultBody:            resultBody,
		Evidence:              fmt.Sprintf("%s %s returned HTTP %d during %s.", endpoint.Method, endpoint.Path, status, bendType),
		Outcome:               outcome,
		Interesting:           interesting,
		AnalysisSummary:       analysisSummary(bendType, status, risk, interesting),
		Risk:                  risk,
		Severity:              severityFor(risk),
		Confidence:            min(96, 58+risk*4),
		Reproducible:          risk >= 6,
		SensitiveDataDetected: risk >= 8,
		TokenUsed:             project.Auth.Type != "none",
		AuthContext:           project.Auth,
		CreatedAt:             now,
	}
}

func outcomeFor(bendType string, status int, risk int) string {
	if risk >= 7 {
		return "finding"
	}
	if risk >= 5 {
		return "suspicious"
	}
	if bendType == "requestSize" && status == 413 {
		return "expected-control"
	}
	if status >= 400 && status < 500 {
		return "blocked"
	}
	return "observed"
}

func isInteresting(bendType string, status int, risk int) bool {
	if risk >= 5 {
		return true
	}
	if bendType == "requestSize" && status >= 500 {
		return true
	}
	return false
}

func analysisSummary(bendType string, status int, risk int, interesting bool) string {
	if interesting {
		return fmt.Sprintf("%s produced a risk %d result with HTTP %d and should be reviewed.", bendType, risk, status)
	}
	return fmt.Sprintf("%s returned HTTP %d and is stored as raw evidence.", bendType, status)
}

func simulatedStatus(endpoint model.Endpoint, bendType string) int {
	if bendType == "authConsistency" || bendType == "jwtAnalysis" || bendType == "idMutation" {
		return 200
	}
	if bendType == "massAssignment" && (endpoint.Method == "POST" || endpoint.Method == "PATCH" || endpoint.Method == "PUT") {
		return 201
	}
	if bendType == "requestSize" {
		return 413
	}
	if bendType == "rateLimit" {
		return 429
	}
	if bendType == "payloadValidation" {
		return 400
	}
	return 200
}

func simulatedBody(endpoint model.Endpoint, bendType string, status int) string {
	body := map[string]any{
		"endpointId": endpoint.ID,
		"path":       endpoint.Path,
		"bendType":   bendType,
		"status":     status,
	}
	if bendType == "authConsistency" || bendType == "idMutation" {
		body["userId"] = "other-user"
		body["email"] = "other-user@example.com"
	}
	encoded, _ := json.Marshal(body)
	return string(encoded)
}

func requestBody(bendType string) *string {
	var body string
	switch bendType {
	case "massAssignment":
		body = `{"role":"ADMIN","isAdmin":true,"tenantId":"other-tenant"}`
	case "fieldSize":
		body = `{"description":"AAAAAAAAAA"}`
	case "requestSize":
		body = `{"payload":"AAAAAAAAAA"}`
	default:
		return nil
	}
	return &body
}

func mutationFor(bendType string) map[string]any {
	switch bendType {
	case "idMutation":
		return map[string]any{"type": "pathIdMutation", "field": "id", "originalValue": "123", "mutatedValue": "124"}
	case "massAssignment":
		return map[string]any{"type": "extraFields", "fields": []string{"role", "isAdmin", "tenantId"}}
	case "fieldSize":
		return map[string]any{"type": "fieldExpansion"}
	case "requestSize":
		return map[string]any{"type": "bodyExpansion"}
	default:
		return map[string]any{"type": bendType}
	}
}

func maskedHeaders(project model.Project) map[string]string {
	headers := map[string]string{"Accept": "application/json"}
	switch project.Auth.Type {
	case "jwt":
		headers["Authorization"] = "Bearer ***"
	case "cookie":
		headers["Cookie"] = "***"
	case "headers":
		for _, header := range project.Auth.HeadersMasked {
			headers[header.Name] = header.Value
		}
	}
	return headers
}

func riskFor(bendType string, status int, endpoint model.Endpoint) int {
	is2xx := status >= 200 && status < 300
	if (bendType == "authConsistency" || bendType == "jwtAnalysis" || bendType == "idMutation") && is2xx {
		return 9
	}
	if bendType == "massAssignment" && is2xx {
		return 8
	}
	if status >= 500 {
		return 6
	}
	if bendType == "rateLimit" && status != 429 {
		return 5
	}
	if strings.Contains(endpoint.Path, "/admin") && is2xx {
		return 7
	}
	if status >= 400 {
		return 2
	}
	return 3
}

func categoryFor(bendType string) string {
	switch bendType {
	case "authConsistency", "idMutation", "massAssignment":
		return "Authorization"
	case "jwtAnalysis":
		return "Authentication"
	case "requestSize":
		return "Request Size"
	case "rateLimit":
		return "Rate Limiting"
	case "responseDiffing":
		return "Contract Consistency"
	default:
		return "Input Validation"
	}
}

func severityFor(risk int) string {
	switch {
	case risk >= 9:
		return "Critical"
	case risk >= 7:
		return "High"
	case risk >= 5:
		return "Medium"
	case risk >= 3:
		return "Low"
	default:
		return "Informational"
	}
}

func statusText(status int) string {
	switch status {
	case 200:
		return "OK"
	case 201:
		return "Created"
	case 400:
		return "Bad Request"
	case 404:
		return "Not Found"
	case 413:
		return "Payload Too Large"
	case 429:
		return "Too Many Requests"
	case 500:
		return "Internal Server Error"
	default:
		return "HTTP"
	}
}

func excluded(path string, patterns []string) bool {
	for _, pattern := range patterns {
		if pattern != "" && strings.Contains(path, pattern) {
			return true
		}
	}
	return false
}

func stringSize(value *string) int {
	if value == nil {
		return 0
	}
	return len(*value)
}

func shortHash(value string) string {
	sum := sha256.Sum256([]byte(value))
	return hex.EncodeToString(sum[:])[:12]
}
