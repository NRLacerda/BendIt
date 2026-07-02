package storage

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"sync"

	"bendit/internal/model"
)

var safeID = regexp.MustCompile(`^[a-z0-9][a-z0-9-]{0,80}$`)

type Store struct {
	root string
	mu   sync.Mutex
}

func New(root string) (*Store, error) {
	if root == "" {
		root = "bend-results"
	}
	if err := os.MkdirAll(root, 0o755); err != nil {
		return nil, err
	}
	return &Store{root: root}, nil
}

func (s *Store) Root() string {
	return s.root
}

func (s *Store) ProjectDir(projectID string) (string, error) {
	if !safeID.MatchString(projectID) {
		return "", fmt.Errorf("invalid projectId %q", projectID)
	}
	rootAbs, err := filepath.Abs(s.root)
	if err != nil {
		return "", err
	}
	dirAbs, err := filepath.Abs(filepath.Join(s.root, projectID))
	if err != nil {
		return "", err
	}
	if dirAbs != rootAbs && !strings.HasPrefix(dirAbs, rootAbs+string(os.PathSeparator)) {
		return "", errors.New("project path escapes results directory")
	}
	return dirAbs, nil
}

func (s *Store) SaveProject(project model.Project) error {
	dir, err := s.ProjectDir(project.ProjectID)
	if err != nil {
		return err
	}
	if err := os.MkdirAll(filepath.Join(dir, "reports"), 0o755); err != nil {
		return err
	}
	return writeJSON(filepath.Join(dir, "project.json"), project)
}

func (s *Store) LoadProject(projectID string) (model.Project, error) {
	var project model.Project
	path, err := s.projectFile(projectID, "project.json")
	if err != nil {
		return project, err
	}
	err = readJSON(path, &project)
	return project, err
}

func (s *Store) ListProjects() ([]model.Project, error) {
	entries, err := os.ReadDir(s.root)
	if err != nil {
		return nil, err
	}
	projects := make([]model.Project, 0, len(entries))
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		project, err := s.LoadProject(entry.Name())
		if err == nil {
			projects = append(projects, project)
		}
	}
	return projects, nil
}

func (s *Store) SaveEndpoints(projectID string, doc model.EndpointsDocument) error {
	path, err := s.projectFile(projectID, "endpoints.json")
	if err != nil {
		return err
	}
	return writeJSON(path, doc)
}

func (s *Store) LoadEndpoints(projectID string) (model.EndpointsDocument, error) {
	var doc model.EndpointsDocument
	path, err := s.projectFile(projectID, "endpoints.json")
	if err != nil {
		return doc, err
	}
	err = readJSON(path, &doc)
	if errors.Is(err, os.ErrNotExist) {
		return model.EndpointsDocument{}, nil
	}
	return doc, err
}

func (s *Store) SaveResults(projectID string, doc model.ResultsDocument) error {
	path, err := s.projectFile(projectID, "results.json")
	if err != nil {
		return err
	}
	return writeJSON(path, doc)
}

func (s *Store) LoadResults(projectID string) (model.ResultsDocument, error) {
	var doc model.ResultsDocument
	path, err := s.projectFile(projectID, "results.json")
	if err != nil {
		return doc, err
	}
	err = readJSON(path, &doc)
	if errors.Is(err, os.ErrNotExist) {
		return model.ResultsDocument{}, nil
	}
	return doc, err
}

func (s *Store) SaveRun(projectID string, doc model.RunDocument) error {
	s.mu.Lock()
	defer s.mu.Unlock()

	dir, err := s.ProjectDir(projectID)
	if err != nil {
		return err
	}
	if err := writeJSON(filepath.Join(dir, "current-run.json"), doc); err != nil {
		return err
	}
	if doc.RunID == "" {
		return nil
	}
	if !safeID.MatchString(doc.RunID) {
		return fmt.Errorf("invalid runId %q", doc.RunID)
	}
	return writeJSON(filepath.Join(dir, "runs", doc.RunID+".json"), doc)
}

func (s *Store) LoadCurrentRun(projectID string) (model.RunDocument, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	var doc model.RunDocument
	path, err := s.projectFile(projectID, "current-run.json")
	if err != nil {
		return doc, err
	}
	err = readJSON(path, &doc)
	return doc, err
}

func (s *Store) LoadRun(projectID, runID string) (model.RunDocument, error) {
	s.mu.Lock()
	defer s.mu.Unlock()

	var doc model.RunDocument
	if !safeID.MatchString(runID) {
		return doc, fmt.Errorf("invalid runId %q", runID)
	}
	dir, err := s.ProjectDir(projectID)
	if err != nil {
		return doc, err
	}
	err = readJSON(filepath.Join(dir, "runs", runID+".json"), &doc)
	return doc, err
}

func (s *Store) projectFile(projectID, name string) (string, error) {
	dir, err := s.ProjectDir(projectID)
	if err != nil {
		return "", err
	}
	return filepath.Join(dir, name), nil
}

func writeJSON(path string, value any) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	tmp, err := os.CreateTemp(filepath.Dir(path), ".tmp-*.json")
	if err != nil {
		return err
	}
	tmpName := tmp.Name()
	defer os.Remove(tmpName)

	enc := json.NewEncoder(tmp)
	enc.SetIndent("", "  ")
	if err := enc.Encode(value); err != nil {
		tmp.Close()
		return err
	}
	if err := tmp.Close(); err != nil {
		return err
	}
	if err := os.Rename(tmpName, path); err != nil {
		if removeErr := os.Remove(path); removeErr != nil && !errors.Is(removeErr, os.ErrNotExist) {
			return err
		}
		return os.Rename(tmpName, path)
	}
	return nil
}

func readJSON(path string, value any) error {
	file, err := os.Open(path)
	if err != nil {
		return err
	}
	defer file.Close()
	return json.NewDecoder(file).Decode(value)
}
