package engine

import (
	"testing"

	"bendit/internal/model"
)

func TestRunTestsCreatesExpectedResultsAndSkipsExcludedEndpoints(t *testing.T) {
	project := model.Project{
		ProjectID: "demo",
		Auth:      model.AuthConfig{Type: "jwt", HeaderName: "Authorization", Scheme: "Bearer", TokenMasked: "abc...***"},
	}
	endpoints := []model.Endpoint{
		{ID: "endpoint_users", Method: "GET", Scheme: "https", Host: "api.example.com", Path: "/api/users/{id}"},
		{ID: "endpoint_delete", Method: "POST", Scheme: "https", Host: "api.example.com", Path: "/api/delete"},
	}
	req := model.TestRunRequest{
		BendTypes:            []string{"authConsistency", "requestSize"},
		ParallelWorkers:      4,
		ExcludedPathPatterns: []string{"/delete"},
	}

	doc := RunTests(project, endpoints, req)

	if len(doc.Results) != 2 {
		t.Fatalf("expected two results for one non-excluded endpoint and two bend types, got %d", len(doc.Results))
	}
	for _, result := range doc.Results {
		if result.ProjectID != "demo" {
			t.Fatalf("expected project ID demo, got %q", result.ProjectID)
		}
		if result.EndpointID != "endpoint_users" {
			t.Fatalf("excluded endpoint should not be tested, got %q", result.EndpointID)
		}
		if !result.TokenUsed {
			t.Fatal("expected tokenUsed for jwt project")
		}
		if result.Request.HeadersMasked["Authorization"] != "Bearer ***" {
			t.Fatalf("expected masked authorization header, got %#v", result.Request.HeadersMasked)
		}
		if result.ResultBody == "" {
			t.Fatal("expected stored result body")
		}
		if result.Risk < 1 || result.Risk > 10 {
			t.Fatalf("expected risk in 1..10, got %d", result.Risk)
		}
		if result.BendType == "authConsistency" && !result.Interesting {
			t.Fatal("expected authConsistency success to be marked interesting")
		}
		if result.BendType == "requestSize" && result.Outcome != "expected-control" {
			t.Fatalf("expected requestSize 413 to be expected-control, got %q", result.Outcome)
		}
		if result.AnalysisSummary == "" {
			t.Fatal("expected analysis summary")
		}
	}
}
