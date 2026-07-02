package storage

import (
	"os"
	"path/filepath"
	"testing"
	"time"

	"bendit/internal/model"
)

func TestStoreRejectsUnsafeProjectIDs(t *testing.T) {
	store, err := New(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}

	unsafeIDs := []string{"../escape", "UpperCase", "bad/slash", "", "-starts-dash"}
	for _, id := range unsafeIDs {
		if _, err := store.ProjectDir(id); err == nil {
			t.Fatalf("expected project ID %q to be rejected", id)
		}
	}
}

func TestStorePersistsProjectEndpointsAndResults(t *testing.T) {
	root := t.TempDir()
	store, err := New(root)
	if err != nil {
		t.Fatal(err)
	}

	project := model.Project{
		ProjectID: "my-project",
		Name:      "My Project",
		BaseURL:   "https://api.example.com",
		OutputDir: "bend-results/my-project",
		CreatedAt: time.Now().UTC(),
		UpdatedAt: time.Now().UTC(),
	}
	if err := store.SaveProject(project); err != nil {
		t.Fatal(err)
	}
	loaded, err := store.LoadProject("my-project")
	if err != nil {
		t.Fatal(err)
	}
	if loaded.ProjectID != project.ProjectID {
		t.Fatalf("expected project ID %q, got %q", project.ProjectID, loaded.ProjectID)
	}

	endpoints := model.EndpointsDocument{
		GeneratedAt: time.Now().UTC(),
		Endpoints: []model.Endpoint{
			{ID: "endpoint_123", Method: "GET", Scheme: "https", Host: "api.example.com", Path: "/health"},
		},
	}
	if err := store.SaveEndpoints("my-project", endpoints); err != nil {
		t.Fatal(err)
	}
	if _, err := os.Stat(filepath.Join(root, "my-project", "endpoints.json")); err != nil {
		t.Fatal(err)
	}

	results := model.ResultsDocument{
		GeneratedAt: time.Now().UTC(),
		ProjectID:   "my-project",
		Results:     []model.TestResult{{ID: "result_123", Risk: 3}},
	}
	if err := store.SaveResults("my-project", results); err != nil {
		t.Fatal(err)
	}
	loadedResults, err := store.LoadResults("my-project")
	if err != nil {
		t.Fatal(err)
	}
	if len(loadedResults.Results) != 1 || loadedResults.Results[0].ID != "result_123" {
		t.Fatalf("unexpected results document: %#v", loadedResults)
	}

	run := model.RunDocument{
		RunID:       "run-123abc",
		ProjectID:   "my-project",
		Status:      "completed",
		CurrentStep: "analysis",
		Progress:    100,
		StartedAt:   time.Now().UTC(),
		ResultCount: 1,
	}
	if err := store.SaveRun("my-project", run); err != nil {
		t.Fatal(err)
	}
	loadedRun, err := store.LoadCurrentRun("my-project")
	if err != nil {
		t.Fatal(err)
	}
	if loadedRun.RunID != run.RunID || loadedRun.Status != "completed" {
		t.Fatalf("unexpected current run: %#v", loadedRun)
	}
	archivedRun, err := store.LoadRun("my-project", "run-123abc")
	if err != nil {
		t.Fatal(err)
	}
	if archivedRun.RunID != run.RunID {
		t.Fatalf("unexpected archived run: %#v", archivedRun)
	}
}
