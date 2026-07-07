import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { Activity, AlertTriangle, Download, Eye, FilePlus2, Globe2, Moon, Play, RefreshCw, Search, Server, Sun } from "lucide-react";
import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from "recharts";
import { api } from "./api";
import logoUrl from "./assets/bendit-logo1.png";
import { Button, ConfirmDialog, Field, SelectField, TextArea, TextInput, Toggle } from "./components";
import type { AuthType, Endpoint, Notice, Project, RunDocument, TestResult } from "./types";
import {
  authFromInput,
  downloadJSON,
  maskSecret,
  newProjectTemplate,
  parseLines,
  parseNumberList,
  prettyBody,
  riskClass,
  slugify,
  normalizeTargetUrl
} from "./utils";
import "./styles.css";

type View = "projects" | "projectForm" | "projectRuns" | "run" | "execution" | "results";
type Theme = "dark" | "light";

const bendTypes = [
  "authConsistency",
  "jwtAnalysis",
  "httpMethodValidation",
  "payloadValidation",
  "requestSize",
  "fieldSize",
  "massAssignment",
  "idMutation",
  "inventoryExposure",
  "parameterPollution",
  "contentTypeValidation",
  "corsAnalysis",
  "headerAnalysis",
  "cookieAnalysis",
  "securityHeaders",
  "sensitiveDataExposure",
  "rateLimit",
  "responseDiffing",
  "timingAnalysis"
];

const defaultBendTypes = [
  "authConsistency",
  "jwtAnalysis",
  "httpMethodValidation",
  "payloadValidation",
  "requestSize",
  "fieldSize",
  "massAssignment",
  "idMutation",
  "inventoryExposure",
  "securityHeaders",
  "sensitiveDataExposure",
  "responseDiffing"
];

function App() {
  const [theme, setTheme] = useState<Theme>(() => (localStorage.getItem("bendit.theme") as Theme) || "dark");
  const [view, setView] = useState<View>("projects");
  const [projects, setProjects] = useState<Project[]>([]);
  const [project, setProject] = useState<Project>(() => newProjectTemplate());
  const [runs, setRuns] = useState<RunDocument[]>([]);
  const [endpoints, setEndpoints] = useState<Endpoint[]>([]);
  const [results, setResults] = useState<TestResult[]>([]);
  const [notice, setNotice] = useState<Notice>(null);
  const [runState, setRunState] = useState("Idle");
  const [progress, setProgress] = useState(0);
  const [currentRun, setCurrentRun] = useState<RunDocument | null>(null);
  const [projectSearch, setProjectSearch] = useState("");
  const [endpointSearch, setEndpointSearch] = useState("");
  const [resultSearch, setResultSearch] = useState("");
  const [resultType, setResultType] = useState("all");
  const [riskFilter, setRiskFilter] = useState("all");
  const [resultMode, setResultMode] = useState<"findings" | "raw">("findings");
  const [selectedResult, setSelectedResult] = useState<TestResult | null>(null);
  const [selectedRunId, setSelectedRunId] = useState("");
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [projectEditorMode, setProjectEditorMode] = useState<"create" | "edit">("create");
  const [projectEditorId, setProjectEditorId] = useState<string | null>(null);

  const [authType, setAuthType] = useState<AuthType>("jwt");
  const [jwt, setJwt] = useState("");
  const [cookie, setCookie] = useState("");
  const [headers, setHeaders] = useState("");
  const [useSpecDiscovery, setUseSpecDiscovery] = useState(true);
  const [useNativeApiList, setUseNativeApiList] = useState(true);
  const [apiListText, setApiListText] = useState("");
  const [apiListFileName, setApiListFileName] = useState("");
  const [selectedTypes, setSelectedTypes] = useState<string[]>(defaultBendTypes);
  const [maxRequests, setMaxRequests] = useState("200");
  const [parallelWorkers, setParallelWorkers] = useState("6");
  const [fieldSizes, setFieldSizes] = useState("1,10,50,100");
  const [bodySizes, setBodySizes] = useState("1,10,50,100,150");
  const [excludedPaths, setExcludedPaths] = useState("/payment\n/charge\n/transfer\n/withdraw\n/delete");

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem("bendit.theme", theme);
  }, [theme]);

  useEffect(() => {
    if (!notice || notice.type !== "info") {
      return;
    }
    const timer = window.setTimeout(() => {
      setNotice((current) => (current?.type === "info" ? null : current));
    }, 3500);
    return () => window.clearTimeout(timer);
  }, [notice]);

  useEffect(() => {
    loadProjects();
  }, []);

  useEffect(() => {
    const timer = window.setInterval(() => {
      if (runState === "Idle" && project.projectId && projects.some((item) => item.projectId === project.projectId)) {
        refreshArtifacts(project.projectId, false);
      }
    }, 30000);
    return () => window.clearInterval(timer);
  }, [project.projectId, projects, runState]);

  const filteredProjects = projects.filter((item) => {
    const haystack = `${item.projectId} ${item.name} ${item.baseUrl} ${item.description || ""}`.toLowerCase();
    return !projectSearch || haystack.includes(projectSearch.toLowerCase());
  });

  const filteredEndpoints = endpoints.filter((endpoint) => {
    const haystack = `${endpoint.id} ${endpoint.method} ${endpointUrl(endpoint)} ${endpoint.source.join(" ")}`.toLowerCase();
    return !endpointSearch || haystack.includes(endpointSearch.toLowerCase());
  });
  const endpointSummary = useMemo(() => summarizeEndpoints(endpoints), [endpoints]);

  const resultTypes = useMemo(() => Array.from(new Set(results.map((result) => result.bendType))).sort(), [results]);
  const filteredResults = results.filter((result) => {
    const haystack = `${result.bendType} ${result.url} ${result.title} ${result.result.statusCode}`.toLowerCase();
    const riskMatch = riskFilter === "all" ||
      (riskFilter === "threat" && result.risk >= 7) ||
      (riskFilter === "warning" && result.risk >= 5 && result.risk < 7) ||
      (riskFilter === "healthy" && result.risk < 5);
    const modeMatch = resultMode === "raw" || result.interesting;
    return modeMatch && (resultType === "all" || result.bendType === resultType) && riskMatch && (!resultSearch || haystack.includes(resultSearch.toLowerCase()));
  });
  const runActive = currentRun?.status === "queued" || currentRun?.status === "running";

  async function loadProjects() {
    try {
      setProjects(await api.listProjects());
    } catch (error) {
      fail(error);
    }
  }

  async function selectProject(projectId: string) {
    try {
      const loaded = await api.getProject(projectId);
      setProject(loaded);
      setAuthType(loaded.auth.type);
      setJwt("");
      setCookie("");
      setHeaders("");
      setRuns(await api.listRuns(loaded.projectId));
      setSelectedRunId("");
      info(`Selected project ${loaded.projectId}`);
      setView("projectRuns");
    } catch (error) {
      fail(error);
    }
  }

  async function runProject(projectId: string) {
    try {
      const loaded = await api.getProject(projectId);
      setProject(loaded);
      setAuthType(loaded.auth.type);
      setJwt("");
      setCookie("");
      setHeaders("");
      await refreshArtifacts(loaded.projectId, true);
      setRuns(await api.listRuns(loaded.projectId));
      setSelectedRunId("");
      info(`Ready to run ${loaded.projectId}`);
      setView("run");
    } catch (error) {
      fail(error);
    }
  }

  async function openRunResults(run: RunDocument) {
    try {
      const loadedResults = await api.getRunResults(run.projectId, run.runId);
      setCurrentRun(run);
      setSelectedRunId(run.runId);
      setResults(loadedResults);
      setSelectedResult(loadedResults.find((result) => result.interesting) || loadedResults[0] || null);
      setResultMode("findings");
      setView("results");
    } catch (error) {
      fail(error);
    }
  }

  async function downloadRunResults(run: RunDocument) {
    try {
      const loadedResults = await api.getRunResults(run.projectId, run.runId);
      downloadJSON(`${run.projectId}-${run.runId}-results.json`, {
        generatedAt: new Date().toISOString(),
        projectId: run.projectId,
        runId: run.runId,
        results: loadedResults
      });
    } catch (error) {
      fail(error);
    }
  }

  async function refreshProjectRuns() {
    if (!project.projectId) {
      return;
    }

    try {
      setRuns(await api.listRuns(project.projectId));
    } catch (error) {
      fail(error);
    }
  }

  async function refreshArtifacts(projectId: string, showMessage: boolean) {
    const [loadedEndpoints, loadedResults] = await Promise.all([
      api.getEndpoints(projectId),
      api.getResults(projectId)
    ]);
    setEndpoints(loadedEndpoints);
    setResults(loadedResults);
    if (showMessage) info(`Loaded ${loadedEndpoints.length} endpoints and ${loadedResults.length} results`);
  }

  function buildProjectDraft(projectSource: Project): Project {
    return {
      ...projectSource,
      projectId: slugify(projectSource.projectId),
      name: projectSource.name || "My Project",
      description: projectSource.description || "",
      isWebPage: Boolean(projectSource.isWebPage),
      baseUrl: normalizeTargetUrl(projectSource.baseUrl, Boolean(projectSource.isWebPage)),
      headers: { Accept: "application/json" },
      auth: authFromInput(authType, jwt, cookie, headers),
      outputDir: `bend-results/${slugify(projectSource.projectId)}`
    };
  }

  async function saveProject() {
    try {
      const draft = buildProjectDraft(project);
      const saved = projectEditorMode === "edit" && projectEditorId
        ? await api.updateProject(projectEditorId, draft)
        : await api.saveProject(draft);
      setProject(saved);
      setProjects(await api.listProjects());
      success(`Saved project ${saved.projectId} to ${saved.outputDir}`);
      setView("projects");
    } catch (error) {
      fail(error);
    }
  }

  async function startPipeline() {
    if (runActive) {
      setConfirmOpen(false);
      setView("execution");
      info("A run is already active for this project");
      return;
    }

    let runStarted = false;
    try {
      setConfirmOpen(false);
      setRunState("Saving project");
      setProgress(5);
      setCurrentRun(null);
      setView("execution");
      const saved = await saveProjectWithoutRedirect();

      const started = await api.startRun(saved.projectId, {
        discovery: {
          useSpecDiscovery,
          useNativeApiList,
          apiList: parseLines(apiListText)
        },
        tests: {
          bendTypes: selectedTypes,
          maxRequestsPerEndpoint: Number(maxRequests) || 200,
          parallelWorkers: Number(parallelWorkers) || 6,
          fieldSizesKb: parseNumberList(fieldSizes),
          bodySizesKb: parseNumberList(bodySizes),
          excludedPathPatterns: parseLines(excludedPaths)
        }
      });
      applyRunState(started);
      setCurrentRun(started);
      runStarted = true;
      info(`Started run ${started.runId}`);

      const completed = await pollRun(saved.projectId);
      const [loadedEndpoints, loadedResults] = await Promise.all([
        api.getEndpoints(saved.projectId),
        api.getResults(saved.projectId)
      ]);
      setEndpoints(loadedEndpoints);
      setResults(loadedResults);
      setSelectedResult(loadedResults.find((result) => result.interesting) || loadedResults[0] || null);
      setResultMode("findings");
      setRunState("Idle");
      setProgress(100);
      setCurrentRun(completed);
      setSelectedRunId(completed.runId);
      setRuns(await api.listRuns(saved.projectId));
      success(`Pipeline completed: ${completed.endpointCount} endpoints, ${completed.findingCount} findings, ${completed.resultCount} raw results`);
      setView("results");
    } catch (error) {
      setRunState(runStarted ? "Failed" : "Idle");
      if (!runStarted) {
        setProgress(0);
      }
      if (project.projectId) {
        try {
          await refreshArtifacts(project.projectId, false);
        } catch {
          // Keep the primary run error visible.
        }
      }
      fail(error);
    }
  }

  async function pollRun(projectId: string): Promise<RunDocument> {
    for (;;) {
      const current = await api.getCurrentRun(projectId);
      applyRunState(current);
      setCurrentRun(current);
      if (current.status === "completed") return current;
      if (current.status === "failed") throw new Error(current.error || "Run failed");
      await delay(2000);
    }
  }

  function applyRunState(run: RunDocument) {
    setRunState(runLabel(run));
    setProgress(run.progress);
  }

  async function saveProjectWithoutRedirect() {
    const draft = buildProjectDraft(project);
    const projectExists = projects.some((item) => item.projectId === project.projectId);
    const saved = projectExists
      ? await api.updateProject(project.projectId, draft)
      : await api.saveProject(draft);
    setProject(saved);
    setProjects(await api.listProjects());
    return saved;
  }

  async function loadApiListFile(file: File | null) {
    if (!file) return;
    try {
      const text = await file.text();
      const lines = file.name.toLowerCase().endsWith(".json") ? parseApiListJSON(text) : parseLines(text);
      setApiListText(lines.join("\n"));
      setApiListFileName(file.name);
      success(`Loaded ${lines.length} API-list entries from ${file.name}`);
    } catch (error) {
      fail(error);
    }
  }

  function parseApiListJSON(text: string): string[] {
    const parsed = JSON.parse(text);
    if (Array.isArray(parsed)) return parsed.map(String);
    if (Array.isArray(parsed.apiList)) return parsed.apiList.map(String);
    throw new Error("JSON API list must be an array or { apiList: [...] }");
  }

  function newProject() {
    setProject(newProjectTemplate());
      setEndpoints([]);
      setResults([]);
      setSelectedResult(null);
      setSelectedRunId("");
      setRuns([]);
    setAuthType("jwt");
    setJwt("");
    setCookie("");
    setHeaders("");
    setApiListText("");
    setApiListFileName("");
    setProjectEditorMode("create");
    setProjectEditorId(null);
    setView("projectForm");
  }

  function editProject(target: Project) {
    setProject(target);
    setAuthType(target.auth?.type || "none");
    setJwt("");
    setCookie("");
    setHeaders("");
    setApiListText("");
    setApiListFileName("");
    setProjectEditorMode("edit");
    setProjectEditorId(target.projectId);
    setView("projectForm");
  }

  function success(message: string) {
    setNotice({ type: "success", message });
  }

  function info(message: string) {
    setNotice({ type: "info", message });
  }

  function fail(error: unknown) {
    const message = error instanceof Error ? error.message : String(error);
    setNotice({ type: "error", message });
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <img src={logoUrl} alt="BendIt" className="brand-logo" />
          <div>
            <div className="brand-name">BendIt</div>
            <div className="brand-subtitle">API robustness auditor</div>
          </div>
        </div>
        <nav className="nav-list" aria-label="Primary">
          {[
            ["projects", "Projects"]
          ].map(([key, label]) => (
            <button className={`nav-item ${(view === key || (key === "projects" && (view === "projectForm" || view === "projectRuns"))) ? "is-active" : ""}`} key={key} onClick={() => setView(key as View)}>
              {label}
            </button>
          ))}
        </nav>
        <Button className="theme-toggle sidebar-toggle" title="Toggle theme" onClick={() => setTheme(theme === "dark" ? "light" : "dark")}>
          {theme === "dark" ? <Moon size={16} /> : <Sun size={16} />}
          <span>{theme === "dark" ? "Dark" : "Light"}</span>
        </Button>
        <div className="run-state">
          <div className="run-state-label">Current run</div>
          <div className="run-state-value">{runState}</div>
          <div className="progress-track"><div className="progress-fill" style={{ width: `${progress}%` }} /></div>
        </div>
      </aside>
      <main className="main">
        <header className="topbar">
          <div>
            <Breadcrumbs
              view={view}
              project={project}
              runId={selectedRunId || currentRun?.runId || ""}
              onProjects={() => setView("projects")}
              onProject={() => setView(project.projectId ? "projectRuns" : "projects")}
            />
            <h1>{viewTitle(view)}</h1>
            <p>{viewMeta(view, project)}</p>
          </div>
          <div className="topbar-actions">
            <Button onClick={() => downloadJSON("project.json", project)}><Download size={16} /> JSON</Button>
          </div>
        </header>
        {notice && <div className={`toast ${notice.type}`}>{notice.message}</div>}
        {view === "projects" && (
          <section>
            <div className="toolbar">
              <div className="toolbar-group">
                <Button className="primary-button" onClick={newProject}><FilePlus2 size={16} /> New project</Button>
                <Button onClick={loadProjects}><RefreshCw size={16} /> Refresh</Button>
              </div>
              <div className="toolbar-group">
                <Search size={16} />
                <TextInput value={projectSearch} onChange={(event) => setProjectSearch(event.target.value)} placeholder="Filter projects" />
              </div>
            </div>
            <ProjectTable projects={filteredProjects} selectedId={project.projectId} onSelect={selectProject} onEdit={editProject} onRun={runProject} />
          </section>
        )}
        {view === "projectRuns" && (
          <ProjectRunsView
            project={project}
            runs={runs}
            onRun={() => runProject(project.projectId)}
            onEdit={() => editProject(project)}
            onRefresh={refreshProjectRuns}
            onRead={openRunResults}
            onDownload={downloadRunResults}
          />
        )}
        {view === "projectForm" && (
          <section className="project-form-page">
            <ProjectEditor
              mode={projectEditorMode}
              project={project}
              setProject={setProject}
              authType={authType}
              setAuthType={setAuthType}
              jwt={jwt}
              setJwt={setJwt}
              cookie={cookie}
              setCookie={setCookie}
              headers={headers}
              setHeaders={setHeaders}
            />
            <div className="page-actions">
              <Button onClick={() => setView("projects")}>Cancel</Button>
              <Button className="primary-button" onClick={saveProject}>
                {projectEditorMode === "edit" ? "Save changes" : "Create project"}
              </Button>
            </div>
          </section>
        )}
        {view === "run" && (
          <RunView
            project={project}
            setProject={setProject}
            authType={authType}
            setAuthType={setAuthType}
            jwt={jwt}
            setJwt={setJwt}
            cookie={cookie}
            setCookie={setCookie}
            headers={headers}
            setHeaders={setHeaders}
            endpoints={filteredEndpoints}
            endpointSearch={endpointSearch}
            setEndpointSearch={setEndpointSearch}
            useSpecDiscovery={useSpecDiscovery}
            setUseSpecDiscovery={setUseSpecDiscovery}
            useNativeApiList={useNativeApiList}
            setUseNativeApiList={setUseNativeApiList}
            apiListText={apiListText}
            apiListFileName={apiListFileName}
            onLoadFile={loadApiListFile}
            selectedTypes={selectedTypes}
            setSelectedTypes={setSelectedTypes}
            maxRequests={maxRequests}
            setMaxRequests={setMaxRequests}
            parallelWorkers={parallelWorkers}
            setParallelWorkers={setParallelWorkers}
            fieldSizes={fieldSizes}
            setFieldSizes={setFieldSizes}
            bodySizes={bodySizes}
            setBodySizes={setBodySizes}
            excludedPaths={excludedPaths}
            setExcludedPaths={setExcludedPaths}
            progress={progress}
            runState={runState}
            isRunning={runActive}
            onStart={() => setConfirmOpen(true)}
          />
        )}
        {view === "execution" && (
          <ExecutionView
            project={project}
            run={currentRun}
            progress={progress}
            runState={runState}
            onBackToRun={() => setView("run")}
            onResults={() => currentRun ? openRunResults(currentRun) : setView("results")}
          />
        )}
        {view === "results" && (
          <ResultsView
            runId={selectedRunId}
            results={filteredResults}
            allResults={results}
            endpointSummary={endpointSummary}
            resultTypes={resultTypes}
            resultType={resultType}
            setResultType={setResultType}
            riskFilter={riskFilter}
            setRiskFilter={setRiskFilter}
            resultMode={resultMode}
            setResultMode={setResultMode}
            resultSearch={resultSearch}
            setResultSearch={setResultSearch}
            selectedResult={selectedResult}
            setSelectedResult={setSelectedResult}
          />
        )}
      </main>
      <ConfirmDialog open={confirmOpen} onOpenChange={setConfirmOpen} onConfirm={startPipeline} />
    </div>
  );
}

function viewTitle(view: View) {
  return ({ projects: "Projects", projectForm: "Project", projectRuns: "Project runs", run: "Run", execution: "Execution", results: "Run results" })[view];
}

function viewMeta(view: View, project: Project) {
  const selected = project?.projectId ? `Selected: ${project.projectId}` : "No project selected";
  return ({
    projects: "Select a project or create a target.",
    projectForm: "Create or edit the target configuration. Saving returns to the project list.",
    projectRuns: `${selected}. Review historical runs or start a new one.`,
    run: `${selected}. Configure discovery and the test battery for this project.`,
    execution: `${selected}. Follow the active run as the backend writes progress.`,
    results: `${selected}. Inspect stored evidence for the selected run.`
  })[view];
}

function Breadcrumbs(props: {
  view: View;
  project: Project;
  runId: string;
  onProjects: () => void;
  onProject: () => void;
}) {
  const projectLabel = props.project.name || props.project.projectId;
  return (
    <nav className="breadcrumbs" aria-label="Breadcrumb">
      <button type="button" onClick={props.onProjects}>Projects</button>
      {props.view !== "projects" && projectLabel && (
        <>
          <span>/</span>
          <button type="button" onClick={props.onProject}>{projectLabel}</button>
        </>
      )}
      {props.view === "projectForm" && <><span>/</span><strong>{props.project.projectId ? "Edit" : "New"}</strong></>}
      {props.view === "run" && <><span>/</span><strong>Run setup</strong></>}
      {props.view === "execution" && <><span>/</span><strong>Execution</strong></>}
      {props.view === "results" && <><span>/</span><strong>{props.runId || "Results"}</strong></>}
    </nav>
  );
}

function delay(ms: number) {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}

function runLabel(run: RunDocument) {
  if (run.status === "queued") return "Queued";
  if (run.status === "completed") return "Completed";
  if (run.status === "failed") return "Failed";
  return ({
    api_specs: "Probing API specs",
    discovery: "Discovering endpoints",
    tests: "Running test battery",
    results: "Writing results",
    analysis: "Analyzing findings"
  })[run.currentStep];
}

function EndpointTable(props: { endpoints: Endpoint[] }) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(15);
  const totalPages = Math.max(1, Math.ceil(props.endpoints.length / pageSize));
  const safePage = Math.min(page, totalPages);
  const start = props.endpoints.length === 0 ? 0 : (safePage - 1) * pageSize + 1;
  const end = Math.min(safePage * pageSize, props.endpoints.length);
  const visibleEndpoints = props.endpoints.slice((safePage - 1) * pageSize, safePage * pageSize);

  useEffect(() => {
    setPage(1);
  }, [props.endpoints.length, pageSize]);

  if (props.endpoints.length === 0) {
    return <div className="empty-state endpoint-empty">No endpoints are currently loaded for this project.</div>;
  }

  return (
    <div className="endpoint-table-wrap">
      <div className="table-shell">
        <table>
          <thead><tr><th>ID</th><th>Method</th><th>Endpoint</th><th>Source</th><th>Class</th><th>Confidence</th><th>HTTP</th><th>Auth</th></tr></thead>
          <tbody>
            {visibleEndpoints.map((endpoint) => (
              <tr key={endpoint.id}>
                <td><code>{endpoint.id}</code></td>
                <td><span className="pill">{endpoint.method}</span></td>
                <td className="path-cell">{endpointUrl(endpoint)}</td>
                <td title={endpoint.sourceDetail || ""}>{endpoint.source.join(", ")}</td>
                <td><span className={`pill ${endpointClass(endpoint.classification)}`}>{endpoint.classification || endpoint.status}</span></td>
                <td>{endpoint.confidence ? `${endpoint.confidence}%` : ""}</td>
                <td>{endpoint.statusCode || ""}</td>
                <td>{endpoint.authRequired ? <span className="pill good">yes</span> : <span className="pill">no</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="pagination-bar">
        <span>{start}-{end} of {props.endpoints.length}</span>
        <div className="pagination-controls">
          <select value={pageSize} onChange={(event) => setPageSize(Number(event.target.value))} aria-label="Endpoints per page">
            <option value={10}>10 / page</option>
            <option value={15}>15 / page</option>
            <option value={25}>25 / page</option>
            <option value={50}>50 / page</option>
          </select>
          <Button onClick={() => setPage((value) => Math.max(1, value - 1))} disabled={safePage === 1}>Previous</Button>
          <span>Page {safePage} of {totalPages}</span>
          <Button onClick={() => setPage((value) => Math.min(totalPages, value + 1))} disabled={safePage === totalPages}>Next</Button>
        </div>
      </div>
    </div>
  );
}

function endpointClass(classification?: string): "good" | "warn" | "danger" | "" {
  if (classification === "confirmed" || classification === "protected" || classification === "method_not_allowed") return "good";
  if (classification === "not_found" || classification === "soft_404") return "danger";
  if (classification) return "warn";
  return "";
}

function endpointUrl(endpoint: Endpoint) {
  const port = endpoint.port ? `:${endpoint.port}` : "";
  return `${endpoint.scheme}://${endpoint.host}${port}${endpoint.path}`;
}

function ExecutionView(props: {
  project: Project;
  run: RunDocument | null;
  progress: number;
  runState: string;
  onBackToRun: () => void;
  onResults: () => void;
}) {
  const status = props.run?.status || "queued";
  const active = status === "queued" || status === "running";
  const step = props.run?.currentStep || "api_specs";
  const startedAt = props.run?.startedAt ? new Date(props.run.startedAt).toLocaleString() : "Starting";
  const completedAt = props.run?.completedAt ? new Date(props.run.completedAt).toLocaleString() : "";

  return (
    <section className="execution-page">
      <Stepper progress={props.progress} />

      <section className={`surface execution-hero ${status}`}>
        <div>
          <div className="section-title-row">
            <h2>{active ? "Run in progress" : status === "completed" ? "Run completed" : "Run failed"}</h2>
            <span className={`pill ${status === "completed" ? "good" : status === "failed" ? "danger" : "warn"}`}>{status}</span>
          </div>
          <p>{props.project.name || props.project.projectId}</p>
        </div>
        <div className="execution-progress">
          <strong>{props.progress}%</strong>
          <div className="progress-track wide"><div className="progress-fill" style={{ width: `${props.progress}%` }} /></div>
          <span>{props.runState}</span>
        </div>
      </section>

      <div className="execution-grid">
        <section className="surface">
          <div className="section-head">
            <h2>Pipeline state</h2>
            <span className="muted-note">{props.run?.runId || "Allocating run id"}</span>
          </div>
          <div className="run-step-list">
            {runStepItems(step, props.progress).map((item) => (
              <div key={item.key} className={`run-step-item ${item.state}`}>
                <span>{item.index}</span>
                <div>
                  <strong>{item.label}</strong>
                  <em>{item.caption}</em>
                </div>
              </div>
            ))}
          </div>
        </section>

        <section className="surface">
          <div className="section-head">
            <h2>Live counters</h2>
            <span className="muted-note">Updated from current-run.json</span>
          </div>
          <div className="execution-metrics">
            <Metric label="Endpoints" value={props.run?.endpointCount || 0} />
            <Metric label="Checks run" value={props.run?.resultCount || 0} />
            <Metric label="Findings" value={props.run?.findingCount || 0} tone={(props.run?.findingCount || 0) > 0 ? "danger" : "default"} />
          </div>
          <div className="detail-grid no-margin">
            <Detail label="Started" value={startedAt} />
            <Detail label="Completed" value={completedAt || (active ? "Running" : "Not completed")} />
            <Detail label="Current step" value={props.runState} />
            <Detail label="Target" value={props.project.isWebPage ? "Web Page" : "WebAPI"} />
          </div>
          {props.run?.error && <div className="warning-strip danger-strip">{props.run.error}</div>}
        </section>
      </div>

      <section className="surface execution-actions">
        <div>
          <h2>{active ? "Execution is locked" : "Execution finished"}</h2>
          <p>{active ? "A project can only have one active run. The start action is disabled until this run finishes." : "Review results or adjust the configuration before starting another run."}</p>
        </div>
        <div className="toolbar-group">
          <Button onClick={props.onBackToRun} disabled={active}>Back to config</Button>
          <Button className="primary-button" onClick={props.onResults} disabled={active || status === "failed"}>Open results</Button>
        </div>
      </section>
    </section>
  );
}

function runStepItems(currentStep: RunDocument["currentStep"], progress: number) {
  const steps: Array<{ key: RunDocument["currentStep"]; label: string; caption: string; threshold: number }> = [
    { key: "api_specs", label: "API specs", caption: "Loading existing artifacts and probing specs", threshold: 20 },
    { key: "discovery", label: "Discovery", caption: "Finding and verifying endpoints", threshold: 40 },
    { key: "tests", label: "Test battery", caption: "Executing configured checks", threshold: 80 },
    { key: "results", label: "Results", caption: "Persisting evidence", threshold: 90 },
    { key: "analysis", label: "Analysis", caption: "Summarizing findings", threshold: 100 }
  ];
  const currentIndex = steps.findIndex((item) => item.key === currentStep);
  return steps.map((item, index) => ({
    ...item,
    index: index + 1,
    state: progress >= item.threshold ? "done" : index === currentIndex ? "active" : "pending"
  }));
}

function RunView(props: {
  project: Project;
  setProject: (project: Project) => void;
  authType: AuthType;
  setAuthType: (type: AuthType) => void;
  jwt: string;
  setJwt: (value: string) => void;
  cookie: string;
  setCookie: (value: string) => void;
  headers: string;
  setHeaders: (value: string) => void;
  endpoints: Endpoint[];
  endpointSearch: string;
  setEndpointSearch: (value: string) => void;
  useNativeApiList: boolean;
  setUseNativeApiList: (value: boolean) => void;
  useSpecDiscovery: boolean;
  setUseSpecDiscovery: (value: boolean) => void;
  apiListText: string;
  apiListFileName: string;
  onLoadFile: (file: File | null) => void;
  selectedTypes: string[];
  setSelectedTypes: (types: string[]) => void;
  maxRequests: string;
  setMaxRequests: (value: string) => void;
  parallelWorkers: string;
  setParallelWorkers: (value: string) => void;
  fieldSizes: string;
  setFieldSizes: (value: string) => void;
  bodySizes: string;
  setBodySizes: (value: string) => void;
  excludedPaths: string;
  setExcludedPaths: (value: string) => void;
  progress: number;
  runState: string;
  isRunning: boolean;
  onStart: () => void;
}) {
  const summary = summarizeEndpoints(props.endpoints);
  const [showBatterySettings, setShowBatterySettings] = useState(false);
  const customListCount = parseLines(props.apiListText).length;
  const defaultBattery = isDefaultBatterySettings(props);
  return (
    <section className="run-dashboard">
      <Stepper progress={props.progress} />

      <section className="surface run-command">
        <div>
          <div className="section-title-row">
            <h2>Pipeline</h2>
            <span className={`target-badge ${props.project.isWebPage ? "webpage" : "webapi"}`}>
              {props.project.isWebPage ? <Globe2 size={14} /> : <Server size={14} />}
              {props.project.isWebPage ? "WebPage" : "WebAPI"}
            </span>
          </div>
          <p>Review project details, discovery sources, and battery settings before starting the sequential run.</p>
        </div>
        <div className="run-command-actions">
          <div className="run-state-value">{props.runState}</div>
          <div className="progress-track wide"><div className="progress-fill" style={{ width: `${props.progress}%` }} /></div>
        </div>
      </section>

      <div className="tests-layout aligned">
        <section className="surface discovery-source">
          <div className="section-head">
            <h2>Discovery</h2>
          </div>
          <div className="config-list">
            <Toggle checked={props.useSpecDiscovery} onCheckedChange={props.setUseSpecDiscovery} label="Probe OpenAPI / Swagger" />
            <Toggle checked={props.useNativeApiList} onCheckedChange={props.setUseNativeApiList} label="Use native API list" />
          </div>
          <div className="attach-row">
            <label className="attach-button">
              <input type="file" accept=".txt,.json,text/plain,application/json" onChange={(event) => props.onLoadFile(event.target.files?.[0] || null)} />
              <FilePlus2 size={16} />
              Attach API list
            </label>
            <span className="muted-note inline">{props.apiListFileName ? `${props.apiListFileName} (${customListCount} entries)` : "No custom list attached"}</span>
          </div>
        </section>

        <section className="surface battery-panel">
          <div className="section-head">
            <h2>Battery</h2>
            <div className="toolbar-group">
              {defaultBattery && <span className="pill good">Default Settings</span>}
              <span className="pill">{props.selectedTypes.length} tests</span>
            </div>
          </div>
          <div className="battery-summary">
            <span>Max {props.maxRequests} requests</span>
            <span>{props.parallelWorkers} workers</span>
            <span>{parseLines(props.excludedPaths).length} exclusions</span>
          </div>
        <div className="battery-actions">
          <Button onClick={() => setShowBatterySettings((value) => !value)}>{showBatterySettings ? "Hide details" : "Configure tests"}</Button>
          <span className="muted-note inline">Changes apply on the next run.</span>
        </div>
      </section>
      </div>

      {showBatterySettings && (
        <section className="surface battery-settings">
          <div className="test-grid compact-tests">
            {bendTypes.map((type) => (
              <label key={type}>
                <input
                  type="checkbox"
                  checked={props.selectedTypes.includes(type)}
                  onChange={() => props.setSelectedTypes(props.selectedTypes.includes(type) ? props.selectedTypes.filter((item) => item !== type) : [...props.selectedTypes, type])}
                />
                {type}
              </label>
            ))}
          </div>
          <div className="form-grid settings-grid">
            <Field label="Max requests per endpoint"><TextInput value={props.maxRequests} onChange={(event) => props.setMaxRequests(event.target.value)} /></Field>
            <Field label="Parallel workers"><TextInput value={props.parallelWorkers} onChange={(event) => props.setParallelWorkers(event.target.value)} /></Field>
            <Field label="Field sizes KB"><TextInput value={props.fieldSizes} onChange={(event) => props.setFieldSizes(event.target.value)} /></Field>
            <Field label="Body sizes KB"><TextInput value={props.bodySizes} onChange={(event) => props.setBodySizes(event.target.value)} /></Field>
            <Field label="Excluded path patterns" className="full-span"><TextArea rows={4} value={props.excludedPaths} onChange={(event) => props.setExcludedPaths(event.target.value)} /></Field>
          </div>
        </section>
      )}

      <section className="endpoint-history">
        <div className="section-head endpoint-history-head">
          <div>
            <h2>Stored discovery</h2>
            <p>{props.endpoints.length === 0 ? "New runs start with no loaded endpoints." : "Endpoints from the latest saved discovery are paged below."}</p>
          </div>
        </div>
        <div className="toolbar">
          <div className="metrics-grid compact discovery-metrics">
            <Metric label="Discovered" value={props.endpoints.length} />
            <Metric label="Confirmed" value={summary.confirmed} />
            <Metric label="Protected" value={summary.protected} />
            <Metric label="JavaScript" value={summary.javascript} />
          </div>
          <div className="toolbar-group">
            <Button onClick={() => downloadJSON("endpoints.json", { generatedAt: new Date().toISOString(), endpoints: props.endpoints })}><Download size={16} /> Export endpoints</Button>
            <TextInput value={props.endpointSearch} onChange={(event) => props.setEndpointSearch(event.target.value)} placeholder="Filter endpoints" />
          </div>
        </div>
        <EndpointTable endpoints={props.endpoints} />
      </section>

      <section className="surface run-submit">
        <div>
          <h2>Ready to run</h2>
          <p>Start saves the selected project first, then runs discovery, enqueues endpoints, and executes the selected test battery.</p>
        </div>
        <div className="run-command-actions">
          <div className="run-state-value">{props.runState}</div>
          <Button className="primary-button start-button" onClick={props.onStart} disabled={props.isRunning}>
            <Play size={18} /> {props.isRunning ? "Running" : "Save & Start"}
          </Button>
        </div>
      </section>
    </section>
  );
}

function Stepper(props: { progress: number }) {
  const steps = [
    { label: "API Specs", threshold: 20 },
    { label: "Discovery", threshold: 40 },
    { label: "Tests", threshold: 65 },
    { label: "Results", threshold: 85 },
    { label: "Analysis", threshold: 100 }
  ];
  return (
    <section className="stepper surface">
      {steps.map((step, index) => {
        const previous = index === 0 ? 0 : steps[index - 1].threshold;
        const active = props.progress > previous && props.progress < step.threshold;
        const done = props.progress >= step.threshold;
        return (
          <div className={`step ${done ? "done" : ""} ${active ? "active" : ""}`} key={step.label}>
            <div className="step-index">{index + 1}</div>
            <div>{step.label}</div>
          </div>
        );
      })}
    </section>
  );
}

function ProjectRunsView(props: {
  project: Project;
  runs: RunDocument[];
  onRun: () => void;
  onEdit: () => void;
  onRefresh: () => void | Promise<void>;
  onRead: (run: RunDocument) => void;
  onDownload: (run: RunDocument) => void;
}) {
  const latest = props.runs[0];
  return (
    <section className="project-runs-page">
      <section className="surface project-summary">
        <div>
          <div className="section-title-row">
            <h2>{props.project.name || props.project.projectId}</h2>
            <span className={`target-badge ${props.project.isWebPage ? "webpage" : "webapi"}`}>
              {props.project.isWebPage ? <Globe2 size={14} /> : <Server size={14} />}
              {props.project.isWebPage ? "WebPage" : "WebAPI"}
            </span>
          </div>
          <p>{props.project.baseUrl}</p>
        </div>
        <div className="project-summary-actions">
          <Button onClick={props.onEdit}>Edit</Button>
          <Button onClick={props.onRefresh}><RefreshCw size={16} /> Refresh</Button>
          <Button className="primary-button" onClick={props.onRun}><Play size={16} /> Run</Button>
        </div>
      </section>

      <div className="results-overview">
        <Metric label="Runs" value={props.runs.length} />
        <Metric label="Latest endpoints" value={latest?.endpointCount || 0} />
        <Metric label="Latest checks" value={latest?.resultCount || 0} />
        <Metric label="Latest findings" value={latest?.findingCount || 0} tone={(latest?.findingCount || 0) > 0 ? "danger" : "default"} />
      </div>

      <section>
        <div className="section-head">
          <h2>Last runs</h2>
          <span className="muted-note">{props.runs.length === 0 ? "No runs yet" : "Newest first"}</span>
        </div>
        <RunHistoryTable runs={props.runs} onRead={props.onRead} onDownload={props.onDownload} />
      </section>
    </section>
  );
}

function RunHistoryTable(props: {
  runs: RunDocument[];
  onRead: (run: RunDocument) => void;
  onDownload: (run: RunDocument) => void;
}) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const totalPages = Math.max(1, Math.ceil(props.runs.length / pageSize));
  const safePage = Math.min(page, totalPages);
  const start = props.runs.length === 0 ? 0 : (safePage - 1) * pageSize + 1;
  const end = Math.min(safePage * pageSize, props.runs.length);
  const visibleRuns = props.runs.slice((safePage - 1) * pageSize, safePage * pageSize);

  useEffect(() => {
    setPage(1);
  }, [props.runs.length, pageSize]);

  if (props.runs.length === 0) {
    return <div className="empty-state">This project has no completed or historical runs yet.</div>;
  }

  return (
    <div className="endpoint-table-wrap">
      <div className="table-shell">
        <table>
          <thead><tr><th>Run</th><th>Status</th><th>Started</th><th>Completed</th><th>Progress</th><th>Endpoints</th><th>Checks</th><th>Findings</th><th>Actions</th></tr></thead>
          <tbody>
            {visibleRuns.map((run) => (
              <tr key={run.runId}>
                <td><code>{run.runId}</code></td>
                <td><span className={`pill ${run.status === "completed" ? "good" : run.status === "failed" ? "danger" : "warn"}`}>{run.status}</span></td>
                <td>{formatDateTime(run.startedAt)}</td>
                <td>{run.completedAt ? formatDateTime(run.completedAt) : ""}</td>
                <td>{run.progress}%</td>
                <td>{run.endpointCount}</td>
                <td>{run.resultCount}</td>
                <td>{run.findingCount}</td>
                <td className="row-action-cell">
                  <div className="project-row-actions">
                    <Button className="icon-only-button" title={`Read ${run.runId}`} onClick={() => props.onRead(run)}><Eye size={16} /></Button>
                    <Button className="icon-only-button" title={`Download ${run.runId}`} onClick={() => props.onDownload(run)}><Download size={16} /></Button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="pagination-bar">
        <span>{start}-{end} of {props.runs.length}</span>
        <div className="pagination-controls">
          <select value={pageSize} onChange={(event) => setPageSize(Number(event.target.value))} aria-label="Runs per page">
            <option value={10}>10 / page</option>
            <option value={15}>15 / page</option>
            <option value={25}>25 / page</option>
          </select>
          <Button onClick={() => setPage((value) => Math.max(1, value - 1))} disabled={safePage === 1}>Previous</Button>
          <span>Page {safePage} of {totalPages}</span>
          <Button onClick={() => setPage((value) => Math.min(totalPages, value + 1))} disabled={safePage === totalPages}>Next</Button>
        </div>
      </div>
    </div>
  );
}

function ProjectTable(props: {
  projects: Project[];
  selectedId: string;
  onSelect: (id: string) => void;
  onEdit: (project: Project) => void;
  onRun: (id: string) => void;
}) {
  return (
    <div className="table-shell">
      <table>
        <thead><tr><th>Project</th><th>Base URL</th><th>Target</th><th>Auth</th><th>Output</th><th>Updated</th><th>Actions</th></tr></thead>
        <tbody>
          {props.projects.map((project) => (
            <tr key={project.projectId} className={props.selectedId === project.projectId ? "is-selected" : ""} onClick={() => props.onSelect(project.projectId)}>
              <td><strong>{project.name || project.projectId}</strong><br /><code>{project.projectId}</code></td>
              <td className="path-cell">{project.baseUrl}</td>
              <td><span className="pill">{project.isWebPage ? "WebPage" : "WebAPI"}</span></td>
              <td><span className="pill">{project.auth?.type || "none"}</span></td>
              <td className="path-cell">{project.outputDir}</td>
              <td>{project.updatedAt || ""}</td>
              <td className="row-action-cell">
                <div className="project-row-actions">
                  <Button onClick={(event) => { event.stopPropagation(); props.onEdit(project); }}>Edit</Button>
                  <Button className="primary-button icon-button" title={`Run ${project.name || project.projectId}`} onClick={(event) => { event.stopPropagation(); props.onRun(project.projectId); }}>
                    <Play size={16} />
                    <span>Run</span>
                  </Button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ProjectEditor(props: {
  mode: "create" | "edit";
  project: Project;
  setProject: (project: Project) => void;
  authType: AuthType;
  setAuthType: (type: AuthType) => void;
  jwt: string;
  setJwt: (value: string) => void;
  cookie: string;
  setCookie: (value: string) => void;
  headers: string;
  setHeaders: (value: string) => void;
}) {
  const update = (patch: Partial<Project>) => props.setProject({ ...props.project, ...patch });
  return (
    <div className="split-layout">
      <section className="surface form-grid">
        <Field label="Project ID"><TextInput value={props.project.projectId} onChange={(event) => update({ projectId: event.target.value })} readOnly={props.mode === "edit"} /></Field>
        <Field label="Name"><TextInput value={props.project.name} onChange={(event) => update({ name: event.target.value })} /></Field>
        <div className="target-type-control full-span" role="radiogroup" aria-label="Target type">
          <button type="button" className={!props.project.isWebPage ? "is-active" : ""} onClick={() => update({ isWebPage: false })}>
            <Server size={18} />
            <span><strong>WebAPI</strong><small>Origin-scoped API target</small></span>
          </button>
          <button type="button" className={props.project.isWebPage ? "is-active" : ""} onClick={() => update({ isWebPage: true })}>
            <Globe2 size={18} />
            <span><strong>Web Page</strong><small>Runs JavaScript discovery first</small></span>
          </button>
        </div>
        <Field label="Base URL" className="full-span"><TextInput value={props.project.baseUrl} onChange={(event) => update({ baseUrl: event.target.value })} /></Field>
        <Field label="Description" className="full-span"><TextArea rows={4} value={props.project.description || ""} onChange={(event) => update({ description: event.target.value })} /></Field>
      </section>
      <section className="surface">
        <div className="section-head">
          <h2>Authentication</h2>
          <SelectField<AuthType>
            label="Authentication type"
            value={props.authType}
            onChange={props.setAuthType}
            options={[
              { value: "jwt", label: "JWT / bearer" },
              { value: "cookie", label: "Cookie" },
              { value: "headers", label: "Custom headers" },
              { value: "none", label: "None" }
            ]}
          />
        </div>
        {props.authType === "jwt" && <Field label="Bearer token"><TextArea rows={7} value={props.jwt} onChange={(event) => props.setJwt(event.target.value)} /></Field>}
        {props.authType === "cookie" && <Field label="Cookie header"><TextArea rows={7} value={props.cookie} onChange={(event) => props.setCookie(event.target.value)} /></Field>}
        {props.authType === "headers" && <Field label="Headers"><TextArea rows={7} value={props.headers} onChange={(event) => props.setHeaders(event.target.value)} placeholder="X-API-Key: ..." /></Field>}
        {props.authType === "none" && <div className="empty-state">No authentication context selected.</div>}
        <div className="warning-strip">Requests can change or harm application data. Run only in a controlled environment.</div>
        {(props.jwt || props.cookie || props.headers) && <div className="muted-note">Stored metadata is masked, for example: {maskSecret(props.jwt || props.cookie || props.headers)}</div>}
      </section>
    </div>
  );
}

function ResultsView(props: {
  runId: string;
  results: TestResult[];
  allResults: TestResult[];
  endpointSummary: EndpointSummary;
  resultTypes: string[];
  resultType: string;
  setResultType: (value: string) => void;
  riskFilter: string;
  setRiskFilter: (value: string) => void;
  resultMode: "findings" | "raw";
  setResultMode: (value: "findings" | "raw") => void;
  resultSearch: string;
  setResultSearch: (value: string) => void;
  selectedResult: TestResult | null;
  setSelectedResult: (result: TestResult) => void;
}) {
  const raw = props.allResults.length;
  const coveredEndpointCount = useMemo(() => new Set(props.allResults.map((result) => result.endpointId)).size, [props.allResults]);
  const healthData = useMemo(() => endpointHealthData(props.endpointSummary.total, props.allResults), [props.endpointSummary.total, props.allResults]);
  const healthy = healthData.find((item) => item.name === "Healthy")?.value || 0;
  const warning = healthData.find((item) => item.name === "Warning")?.value || 0;
  const threat = healthData.find((item) => item.name === "Threat")?.value || 0;
  const endpointRows = useMemo(() => endpointResultRows(props.results), [props.results]);
  const selectedEndpointId = props.selectedResult && endpointRows.some((row) => row.endpointId === props.selectedResult?.endpointId)
    ? props.selectedResult.endpointId
    : endpointRows[0]?.endpointId;
  const selectedEndpointResults = useMemo(() => props.allResults
    .filter((result) => result.endpointId === selectedEndpointId)
    .sort((left, right) => right.risk - left.risk || left.bendType.localeCompare(right.bendType)), [props.allResults, selectedEndpointId]);
  const selected = selectedEndpointResults.find((result) => result.id === props.selectedResult?.id) || selectedEndpointResults[0] || null;

  const [activeTab, setActiveTab] = useState<"verdict" | "request" | "response" | "mutation">("verdict");

  // Helper to format mutation details in human-readable terms
  function formatMutation(mutation: Record<string, any>): string {
    if (!mutation || Object.keys(mutation).length === 0) return "No mutation was applied (baseline check).";
    const type = mutation.type || "";
    if (type === "idMutation" || type === "pathIdMutation") {
      return `Modified the path parameter '${mutation.field || "id"}' from '${mutation.originalValue ?? "123"}' to '${mutation.mutatedValue ?? "124"}' to test for authorization flaws (BOLA/IDOR).`;
    }
    if (type === "massAssignment" || type === "extraFields") {
      const fields = Array.isArray(mutation.fields) ? mutation.fields.join(", ") : "extra parameters";
      return `Injected unexpected administrative/sensitive fields [${fields}] into the request body to check if the server accepts unauthorized parameter binding (Mass Assignment).`;
    }
    if (type === "fieldExpansion" || type === "fieldSize") {
      return `Expanded field parameters to test input size constraints and validation limits.`;
    }
    if (type === "bodyExpansion" || type === "requestSize") {
      return `Expanded the overall request body size to verify maximum body size constraints and check for Denial of Service or resource exhaustion vulnerabilities.`;
    }
    return `Applied mutation '${type}' with properties: ${JSON.stringify(mutation)}`;
  }

  return (
    <section>
      <div className="section-head">
        <h2>{props.runId ? `Run ${props.runId}` : "Run results"}</h2>
        <span className="muted-note">{props.allResults.length} stored checks</span>
      </div>
      <div className="results-overview">
        <Metric label="Discovered endpoints" value={props.endpointSummary.total} />
        <Metric label="Endpoints tested" value={coveredEndpointCount} />
        <Metric label="Raw checks" value={raw} />
        <Metric label="Healthy" value={healthy} tone="good" />
        <Metric label="Warning" value={warning} tone={warning > 0 ? "warning" : "default"} />
        <Metric label="Threat" value={threat} tone={threat > 0 ? "danger" : "default"} />
      </div>

      <section className="surface discovery-breakdown">
        <div className="section-head">
          <h2>Discovery coverage</h2>
          <span className="muted-note">{props.endpointSummary.testable} testable endpoints</span>
        </div>
        <div className="coverage-layout">
          <EndpointHealthChart data={healthData} />
          <div className="breakdown-grid">
            <BreakdownItem label="Confirmed" value={props.endpointSummary.confirmed} />
            <BreakdownItem label="Likely" value={props.endpointSummary.likely} />
            <BreakdownItem label="Protected" value={props.endpointSummary.protected} />
            <BreakdownItem label="Method blocked" value={props.endpointSummary.methodNotAllowed} />
            <BreakdownItem label="JavaScript" value={props.endpointSummary.javascript} />
            <BreakdownItem label="OpenAPI" value={props.endpointSummary.openapi} />
            <BreakdownItem label="API list" value={props.endpointSummary.apiList} />
          </div>
        </div>
      </section>

      <div className="toolbar">
        <div className="toolbar-group">
          <select value={props.resultMode} onChange={(event) => props.setResultMode(event.target.value as "findings" | "raw")}>
            <option value="findings">Findings only</option>
            <option value="raw">Raw evidence ({raw})</option>
          </select>
          <select value={props.resultType} onChange={(event) => props.setResultType(event.target.value)}>
            <option value="all">All types</option>
            {props.resultTypes.map((type) => <option key={type} value={type}>{type}</option>)}
          </select>
          <select value={props.riskFilter} onChange={(event) => props.setRiskFilter(event.target.value)}>
            <option value="all">All health</option>
            <option value="threat">Threat</option>
            <option value="warning">Warning</option>
            <option value="healthy">Healthy</option>
          </select>
        </div>
        <div className="toolbar-group">
          <Button onClick={() => downloadJSON("results.json", { generatedAt: new Date().toISOString(), results: props.allResults })}><Download size={16} /> Export</Button>
          <TextInput value={props.resultSearch} onChange={(event) => props.setResultSearch(event.target.value)} placeholder="Filter results" />
        </div>
      </div>

      <div className="results-layout enhanced">
        <div className="table-shell">
          <table className="results-table">
            <thead><tr><th>Score</th><th>Health</th><th>Endpoint</th><th>Checks</th><th>HTTP</th><th>Last seen</th></tr></thead>
            <tbody>
              {endpointRows.map((row) => (
                <tr key={row.endpointId} className={`${selectedEndpointId === row.endpointId ? "is-selected" : ""} ${row.maxRisk >= 7 ? "finding-row" : ""}`} onClick={() => props.setSelectedResult(row.representative)}>
                  <td><span className={`risk-score ${riskClass(row.maxRisk)}`}>{row.maxRisk}</span></td>
                  <td>
                    <span className={`pill ${riskClass(row.maxRisk)}`}>
                      {healthLabel(row.maxRisk)}
                    </span>
                  </td>
                  <td className="path-cell">{row.method} {safePath(row.url)}</td>
                  <td>{row.total} tests</td>
                  <td>{row.statusCodes.join(", ")}</td>
                  <td>{new Date(row.lastSeenAt).toLocaleTimeString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <section className="result-detail">
          {!selected ? <div className="empty-state">Select a result to inspect request/response payloads.</div> : (
            <>
              <div className="section-head">
                <h2>{selected.method} {safePath(selected.url)}</h2>
                <span className={`pill ${riskClass(selected.risk)}`}>{healthLabel(selected.risk)} {selected.risk}</span>
              </div>

              <div className="test-list">
                {selectedEndpointResults.map((result) => (
                  <button key={result.id} className={selected.id === result.id ? "is-active" : ""} onClick={() => props.setSelectedResult(result)}>
                    <span>{result.bendType}</span>
                    <strong className={riskClass(result.risk)}>{healthLabel(result.risk)} {result.risk}</strong>
                  </button>
                ))}
              </div>

              <div className="tab-row">
                {(["verdict", "request", "response", "mutation"] as const).map((tab) => (
                  <button key={tab} className={activeTab === tab ? "is-active" : ""} onClick={() => setActiveTab(tab)}>
                    {tab.toUpperCase()}
                  </button>
                ))}
              </div>

              <div className="tab-content">
                {activeTab === "verdict" && (
                  <div className="verdict-panel">
                    {selected.interesting ? (
                      <div className="verdict-banner danger">
                        <AlertTriangle size={16} /> Finding detected: endpoint returned vulnerable or unexpected response to payload audit.
                      </div>
                    ) : (
                      <div className="verdict-banner good">
                        <Activity size={16} /> Correct handling: endpoint rejected or safely handled mutated payload.
                      </div>
                    )}

                    <div className="detail-grid no-margin">
                      <Detail label="Test Type" value={selected.bendType} />
                      <Detail label="OWASP Category" value={selected.owaspCategory || "Unmapped"} />
                      <Detail label="Endpoint Tests" value={`${selectedEndpointResults.length}`} />
                      <Detail label="Verdict Class" value={selected.outcome} />
                      <Detail label="Status Code" value={`${selected.result.statusCode} ${selected.result.statusText}`} />
                      <Detail label="Endpoint Health" value={healthLabel(selected.risk)} />
                    </div>

                    <div style={{ marginTop: "4px" }}>
                      <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", marginBottom: "4px" }}>Analysis Summary</strong>
                      <p style={{ margin: "0 0 12px 0", fontSize: "14px", lineHeight: "1.4" }}>{selected.analysisSummary}</p>
                      
                      <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", marginBottom: "4px" }}>Evidence</strong>
                      <p style={{ margin: "0", fontSize: "13px", lineHeight: "1.4", fontStyle: "italic", color: "var(--muted)" }}>{selected.evidence}</p>

                      <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", margin: "12px 0 4px 0" }}>Recommendation</strong>
                      <p style={{ margin: "0", fontSize: "13px", lineHeight: "1.4", color: "var(--muted)" }}>{selected.recommendation || "Review this endpoint behavior against the mapped OWASP category."}</p>
                    </div>
                  </div>
                )}

                {activeTab === "request" && (
                  <div>
                    <div style={{ marginBottom: "12px", padding: "10px", background: "var(--panel-strong)", borderRadius: "6px", border: "1px solid var(--line)" }}>
                      <span className="pill" style={{ marginRight: "8px", background: "var(--accent)", color: "#fff" }}>
                        {selected.method}
                      </span>
                      <code style={{ fontSize: "13px", wordBreak: "break-all" }}>{selected.url}</code>
                    </div>

                    <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", marginBottom: "6px" }}>Headers (Masked)</strong>
                    {selected.request.headersMasked && Object.keys(selected.request.headersMasked).length > 0 ? (
                      <pre style={{ margin: "0 0 14px 0", background: "#0b1014", border: "1px solid var(--line)", padding: "10px" }}>
                        {Object.entries(selected.request.headersMasked).map(([name, value]) => `${name}: ${value}`).join("\n")}
                      </pre>
                    ) : (
                      <p style={{ color: "var(--muted)", fontStyle: "italic", fontSize: "13px", margin: "0 0 14px 0" }}>No request headers recorded.</p>
                    )}

                    <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", marginBottom: "6px" }}>Sent Body</strong>
                    <pre style={{ margin: "0", background: "#0b1014", border: "1px solid var(--line)", padding: "10px" }}>
                      {prettyBody(selected.request.body || "") || "No request body was sent."}
                    </pre>
                  </div>
                )}

                {activeTab === "response" && (
                  <div>
                    <div style={{ marginBottom: "12px", padding: "10px", background: "var(--panel-strong)", borderRadius: "6px", border: "1px solid var(--line)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                      <span className={`pill ${selected.result.statusCode >= 200 && selected.result.statusCode < 300 ? "danger" : "good"}`}>
                        HTTP {selected.result.statusCode} {selected.result.statusText}
                      </span>
                      <span style={{ fontSize: "12px", color: "var(--muted)" }}>
                        {selected.result.contentType || "unknown content-type"} - {selected.result.bodySizeBytes} bytes
                      </span>
                    </div>

                    <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", marginBottom: "6px" }}>Response Body</strong>
                    <pre style={{ margin: "0", background: "#0b1014", border: "1px solid var(--line)", padding: "10px" }}>
                      {prettyBody(selected.resultBody) || "Empty response body."}
                    </pre>

                    <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", margin: "14px 0 6px 0" }}>Response Headers</strong>
                    {selected.result.headersMasked && Object.keys(selected.result.headersMasked).length > 0 ? (
                      <pre style={{ margin: "0", background: "#0b1014", border: "1px solid var(--line)", padding: "10px" }}>
                        {Object.entries(selected.result.headersMasked).map(([name, value]) => `${name}: ${value}`).join("\n")}
                      </pre>
                    ) : (
                      <p style={{ color: "var(--muted)", fontStyle: "italic", fontSize: "13px", margin: "0" }}>No response headers recorded.</p>
                    )}
                  </div>
                )}

                {activeTab === "mutation" && (
                  <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
                    <div style={{ padding: "12px", background: "var(--panel-strong)", borderRadius: "6px", border: "1px solid var(--line)" }}>
                      <strong style={{ display: "block", fontSize: "13px", color: "var(--accent)", marginBottom: "4px" }}>Applied Test Mutation</strong>
                      <p style={{ margin: "0", fontSize: "14px", lineHeight: "1.4" }}>
                        {formatMutation(selected.mutation)}
                      </p>
                    </div>

                    <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", marginBottom: "6px" }}>Raw Mutation Spec</strong>
                    <pre style={{ margin: "0", background: "#0b1014", border: "1px solid var(--line)", padding: "10px" }}>
                      {JSON.stringify(selected.mutation, null, 2)}
                    </pre>
                  </div>
                )}
              </div>
            </>
          )}
        </section>
      </div>
    </section>
  );
}

function Metric(props: { label: string; value: number; tone?: "default" | "good" | "warning" | "danger" }) {
  return <div className={`metric ${props.tone || ""}`}><span>{props.label}</span><strong>{props.value}</strong></div>;
}

function healthLabel(risk: number) {
  if (risk >= 7) return "Threat";
  if (risk >= 5) return "Warning";
  return "Healthy";
}

type EndpointResultRow = {
  endpointId: string;
  method: string;
  url: string;
  total: number;
  maxRisk: number;
  statusCodes: number[];
  lastSeenAt: string;
  representative: TestResult;
};

function endpointResultRows(results: TestResult[]): EndpointResultRow[] {
  const grouped = new Map<string, TestResult[]>();
  for (const result of results) {
    grouped.set(result.endpointId, [...(grouped.get(result.endpointId) || []), result]);
  }

  return Array.from(grouped.entries())
    .map(([endpointId, items]) => {
      const sorted = [...items].sort((left, right) => right.risk - left.risk || new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime());
      const representative = sorted[0];
      return {
        endpointId,
        method: representative.method,
        url: representative.url,
        total: items.length,
        maxRisk: representative.risk,
        statusCodes: Array.from(new Set(items.map((item) => item.result.statusCode))).sort((left, right) => left - right),
        lastSeenAt: items.reduce((latest, item) => new Date(item.createdAt) > new Date(latest) ? item.createdAt : latest, items[0].createdAt),
        representative
      };
    })
    .sort((left, right) => right.maxRisk - left.maxRisk || new Date(right.lastSeenAt).getTime() - new Date(left.lastSeenAt).getTime());
}

function safePath(url: string) {
  try {
    return new URL(url).pathname;
  } catch {
    return url;
  }
}

function formatDateTime(value?: string) {
  return value ? new Date(value).toLocaleString() : "";
}

function isDefaultBatterySettings(props: {
  selectedTypes: string[];
  maxRequests: string;
  parallelWorkers: string;
  fieldSizes: string;
  bodySizes: string;
  excludedPaths: string;
}) {
  const selected = [...props.selectedTypes].sort();
  const defaults = [...defaultBendTypes].sort();
  return selected.length === defaults.length &&
    selected.every((type, index) => type === defaults[index]) &&
    props.maxRequests === "200" &&
    props.parallelWorkers === "6" &&
    props.fieldSizes === "1,10,50,100" &&
    props.bodySizes === "1,10,50,100,150" &&
    props.excludedPaths === "/payment\n/charge\n/transfer\n/withdraw\n/delete";
}

function Detail(props: { label: string; value: string }) {
  return <div className="detail-item"><span>{props.label}</span><strong>{props.value}</strong></div>;
}

function BreakdownItem(props: { label: string; value: number }) {
  return <div className="breakdown-item"><span>{props.label}</span><strong>{props.value}</strong></div>;
}

type HealthSlice = {
  name: string;
  value: number;
  color: string;
};

function EndpointHealthChart(props: { data: HealthSlice[] }) {
  const total = props.data.reduce((sum, item) => sum + item.value, 0);
  return (
    <div className="health-chart">
      <div className="chart-shell">
        <ResponsiveContainer width="100%" height={180}>
          <PieChart>
            <Pie data={props.data} dataKey="value" nameKey="name" innerRadius={48} outerRadius={76} paddingAngle={2}>
              {props.data.map((entry) => <Cell key={entry.name} fill={entry.color} />)}
            </Pie>
            <Tooltip formatter={(value, name) => [`${value ?? 0} endpoints`, String(name)]} />
          </PieChart>
        </ResponsiveContainer>
        <div className="chart-center"><strong>{total}</strong><span>endpoints</span></div>
      </div>
      <div className="chart-legend">
        {props.data.map((item) => (
          <span key={item.name}><i style={{ background: item.color }} />{item.name}: {item.value}</span>
        ))}
      </div>
    </div>
  );
}

type EndpointSummary = {
  total: number;
  confirmed: number;
  likely: number;
  protected: number;
  methodNotAllowed: number;
  testable: number;
  javascript: number;
  openapi: number;
  apiList: number;
};

function summarizeEndpoints(endpoints: Endpoint[]): EndpointSummary {
  return {
    total: endpoints.length,
    confirmed: endpoints.filter((endpoint) => endpoint.classification === "confirmed").length,
    likely: endpoints.filter((endpoint) => endpoint.classification === "likely_exists").length,
    protected: endpoints.filter((endpoint) => endpoint.classification === "protected").length,
    methodNotAllowed: endpoints.filter((endpoint) => endpoint.classification === "method_not_allowed").length,
    testable: endpoints.filter((endpoint) => ["confirmed", "likely_exists", "protected", "method_not_allowed"].includes(endpoint.classification || "") && (endpoint.confidence || 0) >= 60).length,
    javascript: endpoints.filter((endpoint) => endpoint.source.includes("javascript")).length,
    openapi: endpoints.filter((endpoint) => endpoint.source.includes("openapi")).length,
    apiList: endpoints.filter((endpoint) => endpoint.source.includes("nativeApiList") || endpoint.source.includes("customApiList")).length
  };
}

function endpointHealthData(discoveredCount: number, results: TestResult[]): HealthSlice[] {
  const riskByEndpoint = new Map<string, number>();
  for (const result of results) {
    riskByEndpoint.set(result.endpointId, Math.max(riskByEndpoint.get(result.endpointId) || 0, result.risk));
  }

  let warning = 0;
  let threat = 0;
  for (const risk of riskByEndpoint.values()) {
    if (risk >= 7) threat++;
    else if (risk >= 5) warning++;
  }

  const total = Math.max(discoveredCount, riskByEndpoint.size);
  const healthy = Math.max(total - warning - threat, 0);
  return [
    { name: "Healthy", value: healthy, color: "var(--good)" },
    { name: "Warning", value: warning, color: "var(--warning)" },
    { name: "Threat", value: threat, color: "var(--danger)" }
  ];
}

createRoot(document.getElementById("root")!).render(<React.StrictMode><App /></React.StrictMode>);
