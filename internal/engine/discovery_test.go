package engine

import (
	"net/http"
	"net/http/httptest"
	"testing"

	"bendit/internal/model"
)

func TestNormalizeEndpointStableHashAndTemplate(t *testing.T) {
	first, ok := NormalizeEndpoint("get", "https://API.EXAMPLE.com//api/users/123/?b=2&a=1")
	if !ok {
		t.Fatal("expected endpoint to normalize")
	}
	second, ok := NormalizeEndpoint("GET", "https://api.example.com/api/users/456?a=9&b=8")
	if !ok {
		t.Fatal("expected second endpoint to normalize")
	}

	if first.ID != second.ID {
		t.Fatalf("expected IDs to match for same template, got %s and %s", first.ID, second.ID)
	}
	if first.Method != "GET" {
		t.Fatalf("expected normalized method GET, got %q", first.Method)
	}
	if first.Host != "api.example.com" {
		t.Fatalf("expected lower-cased host, got %q", first.Host)
	}
	if first.Path != "/api/users/{id}" {
		t.Fatalf("expected path template, got %q", first.Path)
	}
	if got := first.QueryParams; len(got) != 2 || got[0] != "a" || got[1] != "b" {
		t.Fatalf("expected sorted query parameter names, got %#v", got)
	}
}

func TestDiscovererParsesOpenAPIAndClassifiesEndpoints(t *testing.T) {
	target := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/openapi.json":
			w.Header().Set("Content-Type", "application/json")
			_, _ = w.Write([]byte(`{
				"openapi":"3.0.0",
				"info":{"title":"Demo","version":"1.0.0"},
				"paths":{
					"/api/users/{id}":{"get":{"responses":{"200":{"description":"ok"}}}},
					"/api/orders":{"post":{"responses":{"201":{"description":"created"}}}}
				}
			}`))
		case "/api/users/123":
			w.Header().Set("Content-Type", "application/json")
			_, _ = w.Write([]byte(`{"id":123}`))
		default:
			http.NotFound(w, r)
		}
	}))
	defer target.Close()

	doc := NewDiscoverer(nil).Run(model.Project{
		ProjectID: "demo",
		BaseURL:   target.URL,
		Auth:      model.AuthConfig{Type: "none"},
	}, model.DiscoveryRequest{UseSpecDiscovery: true, UseNativeAPIList: false, TimeoutSeconds: 2})

	foundGet := false
	foundPost := false
	for _, endpoint := range doc.Endpoints {
		if endpoint.Path == "/api/users/{id}" && endpoint.Method == "GET" {
			foundGet = true
			if endpoint.Classification != classConfirmed || endpoint.Confidence < 100 {
				t.Fatalf("expected confirmed OpenAPI GET endpoint, got %#v", endpoint)
			}
		}
		if endpoint.Path == "/api/orders" && endpoint.Method == "POST" {
			foundPost = true
			if endpoint.Classification != classConfirmed || endpoint.Confidence < 100 {
				t.Fatalf("expected confirmed passive OpenAPI POST endpoint, got %#v", endpoint)
			}
		}
	}
	if !foundGet || !foundPost {
		t.Fatalf("expected OpenAPI endpoints, got %#v", doc.Endpoints)
	}
}

func TestDiscovererDetectsSoft404(t *testing.T) {
	target := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/html")
		_, _ = w.Write([]byte(`<html><title>app</title></html>`))
	}))
	defer target.Close()

	doc := NewDiscoverer(nil).Run(model.Project{
		ProjectID: "demo",
		BaseURL:   target.URL,
		Auth:      model.AuthConfig{Type: "none"},
	}, model.DiscoveryRequest{
		UseSpecDiscovery: false,
		UseNativeAPIList: false,
		APIList:          []string{"GET /api/users"},
		TimeoutSeconds:   2,
	})

	if len(doc.Endpoints) != 1 {
		t.Fatalf("expected one candidate endpoint, got %d", len(doc.Endpoints))
	}
	if doc.Endpoints[0].Classification != classSoft404 {
		t.Fatalf("expected soft_404 classification, got %#v", doc.Endpoints[0])
	}
	if len(FilterTestableEndpoints(doc.Endpoints)) != 0 {
		t.Fatal("expected soft-404 endpoint to be excluded from tests")
	}
}

func TestDiscovererProtectedEndpointFeedsTests(t *testing.T) {
	target := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/private" {
			http.Error(w, "no", http.StatusUnauthorized)
			return
		}
		http.NotFound(w, r)
	}))
	defer target.Close()

	doc := NewDiscoverer(nil).Run(model.Project{
		ProjectID: "demo",
		BaseURL:   target.URL,
		Auth:      model.AuthConfig{Type: "none"},
	}, model.DiscoveryRequest{
		UseSpecDiscovery: false,
		UseNativeAPIList: false,
		APIList:          []string{"GET /api/private"},
		TimeoutSeconds:   2,
	})

	if len(doc.Endpoints) != 1 {
		t.Fatalf("expected one endpoint, got %d", len(doc.Endpoints))
	}
	if doc.Endpoints[0].Classification != classProtected || !doc.Endpoints[0].Protected || !doc.Endpoints[0].AuthRequired {
		t.Fatalf("expected protected endpoint metadata, got %#v", doc.Endpoints[0])
	}
	if len(FilterTestableEndpoints(doc.Endpoints)) != 1 {
		t.Fatal("expected protected endpoint to feed tests")
	}
}
