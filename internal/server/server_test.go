package server

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"bendit/internal/model"
	"bendit/internal/storage"
)

func TestProjectCreateValidationFailures(t *testing.T) {
	handler := testHandler(t)

	tests := []struct {
		name string
		body string
	}{
		{name: "invalid json", body: `{`},
		{name: "missing base url", body: `{"projectId":"demo"}`},
		{name: "unsupported scheme", body: `{"projectId":"demo","baseUrl":"ftp://example.com"}`},
		{name: "unknown field", body: `{"projectId":"demo","baseUrl":"https://example.com","extra":true}`},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			req := httptest.NewRequest(http.MethodPost, "/api/projects", bytes.NewBufferString(tt.body))
			rec := httptest.NewRecorder()
			handler.ServeHTTP(rec, req)
			if rec.Code != http.StatusBadRequest {
				t.Fatalf("expected 400, got %d with body %s", rec.Code, rec.Body.String())
			}
		})
	}
}

func TestServerProjectRunFlow(t *testing.T) {
	handler := testHandler(t)
	target := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/custom/123", "/custom":
			w.Header().Set("Content-Type", "application/json")
			_, _ = w.Write([]byte(`{"ok":true}`))
		default:
			http.NotFound(w, r)
		}
	}))
	defer target.Close()

	projectBody := `{"projectId":"Flow Project","name":"Flow Project","baseUrl":"` + target.URL + `/path","auth":{"type":"jwt","headerName":"Authorization","scheme":"Bearer","tokenMasked":"abc...***"}}`
	rec := doJSON(handler, http.MethodPost, "/api/projects", projectBody)
	if rec.Code != http.StatusCreated {
		t.Fatalf("create project: expected 201, got %d: %s", rec.Code, rec.Body.String())
	}

	var project model.Project
	if err := json.NewDecoder(rec.Body).Decode(&project); err != nil {
		t.Fatal(err)
	}
	if project.ProjectID != "flow-project" {
		t.Fatalf("expected slugged project ID, got %q", project.ProjectID)
	}
	if project.BaseURL != target.URL {
		t.Fatalf("expected normalized origin base URL, got %q", project.BaseURL)
	}

	runBody := `{"discovery":{"useNativeApiList":false,"useSpecDiscovery":false,"apiList":["GET /custom/123","GET /custom"],"timeoutSeconds":2},"tests":{"bendTypes":["authConsistency","requestSize"],"parallelWorkers":2}}`
	current := startAndWaitForRun(t, handler, "/api/projects/flow-project/run", runBody)
	if current.EndpointCount != 2 || current.ResultCount != 4 {
		t.Fatalf("unexpected run metrics: %#v", current)
	}
}

func TestRemovedPhaseOnlyEndpointsReturnNotFound(t *testing.T) {
	handler := testHandler(t)

	rec := doJSON(handler, http.MethodPost, "/api/projects", `{"projectId":"no-endpoints","baseUrl":"https://api.example.com"}`)
	if rec.Code != http.StatusCreated {
		t.Fatalf("create project: expected 201, got %d: %s", rec.Code, rec.Body.String())
	}

	rec = doJSON(handler, http.MethodPost, "/api/projects/no-endpoints/tests/run", `{}`)
	if rec.Code != http.StatusNotFound {
		t.Fatalf("expected removed tests endpoint to return 404, got %d: %s", rec.Code, rec.Body.String())
	}
	rec = doJSON(handler, http.MethodPost, "/api/projects/no-endpoints/discovery/start", `{}`)
	if rec.Code != http.StatusNotFound {
		t.Fatalf("expected removed discovery endpoint to return 404, got %d: %s", rec.Code, rec.Body.String())
	}
}

func TestProjectRunPipelinePersistsProgressAndArtifacts(t *testing.T) {
	handler := testHandler(t)
	target := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/openapi.json":
			w.Header().Set("Content-Type", "application/json")
			_, _ = w.Write([]byte(`{
				"openapi":"3.0.0",
				"info":{"title":"Pipeline","version":"1.0.0"},
				"paths":{
					"/users/{id}":{"get":{"responses":{"200":{"description":"ok"}}}},
					"/orders":{"post":{"responses":{"201":{"description":"created"}}}}
				}
			}`))
		case "/users/123":
			w.Header().Set("Content-Type", "application/json")
			_, _ = w.Write([]byte(`{"id":123}`))
		default:
			http.NotFound(w, r)
		}
	}))
	defer target.Close()

	rec := doJSON(handler, http.MethodPost, "/api/projects", `{"projectId":"pipeline","baseUrl":"`+target.URL+`","auth":{"type":"none"}}`)
	if rec.Code != http.StatusCreated {
		t.Fatalf("create project: expected 201, got %d: %s", rec.Code, rec.Body.String())
	}

	runBody := `{"discovery":{"useNativeApiList":false,"useSpecDiscovery":true,"timeoutSeconds":2},"tests":{"bendTypes":["authConsistency","massAssignment"],"parallelWorkers":2}}`
	rec = doJSON(handler, http.MethodPost, "/api/projects/pipeline/run", runBody)
	if rec.Code != http.StatusAccepted {
		t.Fatalf("start run: expected 202, got %d: %s", rec.Code, rec.Body.String())
	}
	var started model.RunDocument
	if err := json.NewDecoder(rec.Body).Decode(&started); err != nil {
		t.Fatal(err)
	}
	if started.RunID == "" || started.Status != "queued" {
		t.Fatalf("unexpected started run: %#v", started)
	}

	var current model.RunDocument
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		rec = doJSON(handler, http.MethodGet, "/api/projects/pipeline/runs/current", "")
		if rec.Code != http.StatusOK {
			t.Fatalf("current run: expected 200, got %d: %s", rec.Code, rec.Body.String())
		}
		if err := json.NewDecoder(rec.Body).Decode(&current); err != nil {
			t.Fatal(err)
		}
		if current.Status == "completed" || current.Status == "failed" {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}
	if current.Status != "completed" {
		t.Fatalf("expected completed run, got %#v", current)
	}
	if current.EndpointCount < 2 || current.ResultCount < 4 || current.FindingCount == 0 || current.Progress != 100 {
		t.Fatalf("unexpected completed run metrics: %#v", current)
	}

	rec = doJSON(handler, http.MethodGet, "/api/projects/pipeline/endpoints", "")
	if rec.Code != http.StatusOK {
		t.Fatalf("endpoints: expected 200, got %d: %s", rec.Code, rec.Body.String())
	}
	var endpoints model.EndpointsDocument
	if err := json.NewDecoder(rec.Body).Decode(&endpoints); err != nil {
		t.Fatal(err)
	}
	if len(endpoints.Endpoints) != current.EndpointCount {
		t.Fatalf("expected %d endpoints, got %d", current.EndpointCount, len(endpoints.Endpoints))
	}

	rec = doJSON(handler, http.MethodGet, "/api/projects/pipeline/results", "")
	if rec.Code != http.StatusOK {
		t.Fatalf("results: expected 200, got %d: %s", rec.Code, rec.Body.String())
	}
	var results model.ResultsDocument
	if err := json.NewDecoder(rec.Body).Decode(&results); err != nil {
		t.Fatal(err)
	}
	if len(results.Results) != current.ResultCount {
		t.Fatalf("expected %d results, got %d", current.ResultCount, len(results.Results))
	}
}

func startAndWaitForRun(t *testing.T, handler http.Handler, path, body string) model.RunDocument {
	t.Helper()
	rec := doJSON(handler, http.MethodPost, path, body)
	if rec.Code != http.StatusAccepted {
		t.Fatalf("start run: expected 202, got %d: %s", rec.Code, rec.Body.String())
	}
	var started model.RunDocument
	if err := json.NewDecoder(rec.Body).Decode(&started); err != nil {
		t.Fatal(err)
	}
	if started.RunID == "" || started.Status != "queued" {
		t.Fatalf("unexpected started run: %#v", started)
	}

	projectPath := strings.TrimSuffix(strings.TrimSuffix(path, "/run"), "/")
	var current model.RunDocument
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		rec = doJSON(handler, http.MethodGet, projectPath+"/runs/current", "")
		if rec.Code != http.StatusOK {
			t.Fatalf("current run: expected 200, got %d: %s", rec.Code, rec.Body.String())
		}
		if err := json.NewDecoder(rec.Body).Decode(&current); err != nil {
			t.Fatal(err)
		}
		if current.Status == "completed" || current.Status == "failed" {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}
	if current.Status != "completed" {
		t.Fatalf("expected completed run, got %#v", current)
	}
	return current
}

func testHandler(t *testing.T) http.Handler {
	t.Helper()
	store, err := storage.New(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	return New(store, "../../frontend").Handler()
}

func doJSON(handler http.Handler, method, path, body string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(method, path, bytes.NewBufferString(body))
	req.Header.Set("Content-Type", "application/json")
	rec := httptest.NewRecorder()
	handler.ServeHTTP(rec, req)
	return rec
}
