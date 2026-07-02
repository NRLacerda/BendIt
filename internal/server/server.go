package server

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"net/url"
	"os"
	"path"
	"path/filepath"
	"regexp"
	"strings"
	"sync"
	"time"

	"bendit/internal/engine"
	"bendit/internal/model"
	"bendit/internal/storage"
)

type Server struct {
	store     *storage.Store
	frontend  http.Handler
	startedAt time.Time
	mu        sync.Mutex
	activeRun map[string]bool
}

func New(store *storage.Store, frontendDir string) *Server {
	if frontendDir == "" {
		frontendDir = "frontend"
	}
	if _, err := os.Stat(filepath.Join(frontendDir, "dist", "index.html")); err == nil {
		frontendDir = filepath.Join(frontendDir, "dist")
	}
	return &Server{
		store:     store,
		frontend:  frontendHandler(frontendDir),
		startedAt: time.Now().UTC(),
		activeRun: make(map[string]bool),
	}
}

func (s *Server) Handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/api/health", s.health)
	mux.HandleFunc("/api/projects", s.projects)
	mux.HandleFunc("/api/projects/", s.projectByID)
	mux.Handle("/", s.frontend)
	return withSecurityHeaders(mux)
}

func (s *Server) health(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"status":     "ok",
		"startedAt":  s.startedAt,
		"resultsDir": s.store.Root(),
	})
}

func (s *Server) projects(w http.ResponseWriter, r *http.Request) {
	switch r.Method {
	case http.MethodGet:
		projects, err := s.store.ListProjects()
		if err != nil {
			writeError(w, http.StatusInternalServerError, err)
			return
		}
		writeJSON(w, http.StatusOK, map[string]any{"projects": projects})
	case http.MethodPost:
		var project model.Project
		if err := readJSON(r, &project); err != nil {
			writeError(w, http.StatusBadRequest, err)
			return
		}
		normalized, err := normalizeProject(project, s.store.Root())
		if err != nil {
			writeError(w, http.StatusBadRequest, err)
			return
		}
		if err := s.store.SaveProject(normalized); err != nil {
			writeError(w, http.StatusInternalServerError, err)
			return
		}
		writeJSON(w, http.StatusCreated, normalized)
	default:
		methodNotAllowed(w)
	}
}

func (s *Server) projectByID(w http.ResponseWriter, r *http.Request) {
	projectID, rest, ok := splitProjectPath(r.URL.Path)
	if !ok {
		http.NotFound(w, r)
		return
	}

	switch {
	case rest == "":
		s.project(w, r, projectID)
	case rest == "endpoints":
		s.endpoints(w, r, projectID)
	case rest == "run":
		s.startRun(w, r, projectID)
	case rest == "runs/current":
		s.currentRun(w, r, projectID)
	case strings.HasPrefix(rest, "runs/"):
		s.run(w, r, projectID, strings.TrimPrefix(rest, "runs/"))
	case rest == "results":
		s.results(w, r, projectID)
	default:
		http.NotFound(w, r)
	}
}

func (s *Server) startRun(w http.ResponseWriter, r *http.Request, projectID string) {
	if r.Method != http.MethodPost {
		methodNotAllowed(w)
		return
	}
	project, err := s.store.LoadProject(projectID)
	if err != nil {
		writeError(w, http.StatusNotFound, err)
		return
	}

	req := model.RunRequest{
		Discovery: model.DiscoveryRequest{UseNativeAPIList: true, UseSpecDiscovery: true},
	}
	if r.Body != nil {
		if err := readJSON(r, &req); err != nil && !errors.Is(err, errEmptyBody) {
			writeError(w, http.StatusBadRequest, err)
			return
		}
	}

	if !s.reserveRun(projectID) {
		writeError(w, http.StatusConflict, errors.New("a run is already active for this project"))
		return
	}

	run := model.RunDocument{
		RunID:       newRunID(projectID),
		ProjectID:   projectID,
		Status:      "queued",
		CurrentStep: "api_specs",
		Progress:    0,
		StartedAt:   time.Now().UTC(),
	}
	if err := s.store.SaveRun(projectID, run); err != nil {
		s.releaseRun(projectID)
		writeError(w, http.StatusInternalServerError, err)
		return
	}

	go s.executeRun(project, req, run)
	writeJSON(w, http.StatusAccepted, run)
}

func (s *Server) currentRun(w http.ResponseWriter, r *http.Request, projectID string) {
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	run, err := s.store.LoadCurrentRun(projectID)
	if errors.Is(err, os.ErrNotExist) {
		writeError(w, http.StatusNotFound, errors.New("no run has been started for this project"))
		return
	}
	if err != nil {
		writeError(w, http.StatusInternalServerError, err)
		return
	}
	writeJSON(w, http.StatusOK, run)
}

func (s *Server) run(w http.ResponseWriter, r *http.Request, projectID, runID string) {
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	run, err := s.store.LoadRun(projectID, runID)
	if errors.Is(err, os.ErrNotExist) {
		writeError(w, http.StatusNotFound, errors.New("run not found"))
		return
	}
	if err != nil {
		writeError(w, http.StatusInternalServerError, err)
		return
	}
	writeJSON(w, http.StatusOK, run)
}

func (s *Server) executeRun(project model.Project, req model.RunRequest, run model.RunDocument) {
	projectID := project.ProjectID
	defer s.releaseRun(projectID)

	update := func(step, status string, progress int) {
		run.CurrentStep = step
		run.Status = status
		run.Progress = progress
		_ = s.store.SaveRun(projectID, run)
	}
	fail := func(err error) {
		now := time.Now().UTC()
		run.Status = "failed"
		run.Error = err.Error()
		run.CompletedAt = &now
		if run.Progress < 1 {
			run.Progress = 1
		}
		_ = s.store.SaveRun(projectID, run)
	}

	update("api_specs", "running", 10)
	existing, err := s.store.LoadEndpoints(projectID)
	if err != nil {
		fail(err)
		return
	}

	update("discovery", "running", 25)
	discoverer := engine.NewDiscoverer(existing.Endpoints)
	endpoints := discoverer.Run(project, req.Discovery)
	run.EndpointCount = len(endpoints.Endpoints)
	if err := s.store.SaveEndpoints(projectID, endpoints); err != nil {
		fail(err)
		return
	}

	update("tests", "running", 55)
	testableEndpoints := engine.FilterTestableEndpoints(endpoints.Endpoints)
	results := engine.RunTests(project, testableEndpoints, req.Tests)
	run.ResultCount = len(results.Results)
	run.FindingCount = countFindings(results.Results)
	if err := s.store.SaveResults(projectID, results); err != nil {
		fail(err)
		return
	}

	update("results", "running", 82)
	update("analysis", "running", 95)
	now := time.Now().UTC()
	run.Status = "completed"
	run.Progress = 100
	run.CompletedAt = &now
	_ = s.store.SaveRun(projectID, run)
}

func (s *Server) reserveRun(projectID string) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.activeRun[projectID] {
		return false
	}
	s.activeRun[projectID] = true
	return true
}

func (s *Server) releaseRun(projectID string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	delete(s.activeRun, projectID)
}

func (s *Server) project(w http.ResponseWriter, r *http.Request, projectID string) {
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	project, err := s.store.LoadProject(projectID)
	if err != nil {
		writeError(w, http.StatusNotFound, err)
		return
	}
	writeJSON(w, http.StatusOK, project)
}

func (s *Server) endpoints(w http.ResponseWriter, r *http.Request, projectID string) {
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	doc, err := s.store.LoadEndpoints(projectID)
	if err != nil {
		writeError(w, http.StatusInternalServerError, err)
		return
	}
	writeJSON(w, http.StatusOK, doc)
}

func (s *Server) results(w http.ResponseWriter, r *http.Request, projectID string) {
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	doc, err := s.store.LoadResults(projectID)
	if err != nil {
		writeError(w, http.StatusInternalServerError, err)
		return
	}
	writeJSON(w, http.StatusOK, doc)
}

var projectIDChars = regexp.MustCompile(`[^a-z0-9-]+`)

func normalizeProject(project model.Project, resultsRoot string) (model.Project, error) {
	parsed, err := url.Parse(project.BaseURL)
	if err != nil || parsed.Scheme == "" || parsed.Host == "" {
		return project, errors.New("baseUrl must be an absolute URL")
	}
	if parsed.Scheme != "http" && parsed.Scheme != "https" {
		return project, errors.New("baseUrl must use http or https")
	}

	project.ProjectID = slug(project.ProjectID)
	if project.ProjectID == "" {
		if project.Name != "" {
			project.ProjectID = slug(project.Name)
		} else {
			project.ProjectID = slug(parsed.Host)
		}
	}
	if project.Name == "" {
		project.Name = project.ProjectID
	}
	project.BaseURL = parsed.Scheme + "://" + parsed.Host
	project.OutputDir = filepath.ToSlash(filepath.Join(resultsRoot, project.ProjectID))
	now := time.Now().UTC()
	if project.CreatedAt.IsZero() {
		project.CreatedAt = now
	}
	project.UpdatedAt = now
	if project.Auth.Type == "" {
		project.Auth.Type = "none"
	}
	return project, nil
}

func slug(value string) string {
	value = strings.ToLower(strings.TrimSpace(value))
	value = projectIDChars.ReplaceAllString(value, "-")
	value = strings.Trim(value, "-")
	if len(value) > 80 {
		value = strings.Trim(value[:80], "-")
	}
	return value
}

func splitProjectPath(path string) (projectID, rest string, ok bool) {
	trimmed := strings.TrimPrefix(path, "/api/projects/")
	if trimmed == path || trimmed == "" {
		return "", "", false
	}
	parts := strings.SplitN(trimmed, "/", 2)
	projectID = parts[0]
	if len(parts) == 2 {
		rest = parts[1]
	}
	return projectID, rest, projectID != ""
}

func newRunID(projectID string) string {
	sum := sha256.Sum256([]byte(projectID + time.Now().UTC().Format(time.RFC3339Nano)))
	return "run-" + hex.EncodeToString(sum[:])[:12]
}

func countFindings(results []model.TestResult) int {
	count := 0
	for _, result := range results {
		if result.Interesting {
			count++
		}
	}
	return count
}

func frontendHandler(frontendDir string) http.Handler {
	fileSystem := http.Dir(frontendDir)
	fileServer := http.FileServer(fileSystem)
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/bendit-logo1.png" {
			http.ServeFile(w, r, "bendit-logo1.png")
			return
		}
		if r.URL.Path == "/" {
			http.ServeFile(w, r, filepath.Join(frontendDir, "index.html"))
			return
		}
		cleanPath := strings.TrimPrefix(path.Clean("/"+r.URL.Path), "/")
		if _, err := os.Stat(filepath.Join(frontendDir, cleanPath)); err != nil {
			http.ServeFile(w, r, filepath.Join(frontendDir, "index.html"))
			return
		}
		fileServer.ServeHTTP(w, r)
	})
}

var errEmptyBody = errors.New("empty body")

func readJSON(r *http.Request, value any) error {
	r.Body = http.MaxBytesReader(nil, r.Body, 1<<20)
	defer r.Body.Close()

	dec := json.NewDecoder(r.Body)
	dec.DisallowUnknownFields()
	if err := dec.Decode(value); err != nil {
		if strings.Contains(err.Error(), "EOF") {
			return errEmptyBody
		}
		return err
	}
	return nil
}

func writeJSON(w http.ResponseWriter, status int, value any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(value)
}

func writeError(w http.ResponseWriter, status int, err error) {
	writeJSON(w, status, map[string]any{"error": err.Error()})
}

func methodNotAllowed(w http.ResponseWriter) {
	writeError(w, http.StatusMethodNotAllowed, fmt.Errorf("method not allowed"))
}

func withSecurityHeaders(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-Content-Type-Options", "nosniff")
		w.Header().Set("Referrer-Policy", "no-referrer")
		next.ServeHTTP(w, r)
	})
}
