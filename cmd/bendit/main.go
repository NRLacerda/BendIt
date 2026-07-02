package main

import (
	"context"
	"flag"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"bendit/internal/server"
	"bendit/internal/storage"
)

func main() {
	addr := flag.String("addr", "127.0.0.1:8080", "HTTP listen address")
	resultsDir := flag.String("results-dir", "bend-results", "directory for generated JSON artifacts")
	frontendDir := flag.String("frontend-dir", "frontend", "directory containing static frontend files")
	flag.Parse()

	store, err := storage.New(*resultsDir)
	if err != nil {
		slog.Error("create store", "error", err)
		os.Exit(1)
	}

	app := server.New(store, *frontendDir)
	httpServer := &http.Server{
		Addr:              *addr,
		Handler:           app.Handler(),
		ReadHeaderTimeout: 5 * time.Second,
	}

	go func() {
		fmt.Printf("BendIt listening at http://%s\n", *addr)
		if err := httpServer.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			slog.Error("server failed", "error", err)
			os.Exit(1)
		}
	}()

	stop := make(chan os.Signal, 1)
	signal.Notify(stop, os.Interrupt, syscall.SIGTERM)
	<-stop

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := httpServer.Shutdown(ctx); err != nil {
		slog.Error("shutdown failed", "error", err)
		os.Exit(1)
	}
}
