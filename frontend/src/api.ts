import type { Endpoint, Project, RunConfig, RunDocument, TestResult } from "./types";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    headers: { "Content-Type": "application/json", ...(init?.headers || {}) },
    ...init
  });
  const text = await response.text();
  const payload = text ? safeJSON(text) : null;
  if (!response.ok) {
    const message = payload && typeof payload === "object" && "error" in payload
      ? String((payload as { error: unknown }).error)
      : text || `HTTP ${response.status}`;
    throw new Error(message);
  }
  return payload as T;
}

function safeJSON(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export const api = {
  async listProjects(): Promise<Project[]> {
    const payload = await request<{ projects: Project[] }>("/api/projects");
    return payload.projects || [];
  },
  saveProject(project: Project): Promise<Project> {
    return request<Project>("/api/projects", { method: "POST", body: JSON.stringify(project) });
  },
  getProject(projectId: string): Promise<Project> {
    return request<Project>(`/api/projects/${encodeURIComponent(projectId)}`);
  },
  async getEndpoints(projectId: string): Promise<Endpoint[]> {
    const payload = await request<{ endpoints: Endpoint[] }>(`/api/projects/${encodeURIComponent(projectId)}/endpoints`);
    return payload.endpoints || [];
  },
  startRun(projectId: string, config: RunConfig): Promise<RunDocument> {
    return request<RunDocument>(`/api/projects/${encodeURIComponent(projectId)}/run`, {
      method: "POST",
      body: JSON.stringify(config)
    });
  },
  getCurrentRun(projectId: string): Promise<RunDocument> {
    return request<RunDocument>(`/api/projects/${encodeURIComponent(projectId)}/runs/current`);
  },
  async getResults(projectId: string): Promise<TestResult[]> {
    const payload = await request<{ results: TestResult[] }>(`/api/projects/${encodeURIComponent(projectId)}/results`);
    return payload.results || [];
  }
};
