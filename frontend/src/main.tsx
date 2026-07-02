import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { Download, FilePlus2, Moon, Play, RefreshCw, Save, Search, Sun } from "lucide-react";
import { api } from "./api";
import { Button, ConfirmDialog, Field, SelectField, TextArea, TextInput, Toggle } from "./components";
import type { AuthType, Endpoint, Notice, Project, RunDocument, TestResult } from "./types";
import {
  authFromInput,
  downloadJSON,
  maskSecret,
  newProjectTemplate,
  normalizeBaseUrl,
  parseLines,
  parseNumberList,
  prettyBody,
  riskClass,
  slugify
} from "./utils";
import "./styles.css";

type View = "run" | "projects" | "results";
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
  "parameterPollution",
  "contentTypeValidation",
  "corsAnalysis",
  "headerAnalysis",
  "cookieAnalysis",
  "rateLimit",
  "responseDiffing",
  "timingAnalysis"
];

function App() {
  const [theme, setTheme] = useState<Theme>(() => (localStorage.getItem("bendit.theme") as Theme) || "dark");
  const [view, setView] = useState<View>("run");
  const [projects, setProjects] = useState<Project[]>([]);
  const [project, setProject] = useState<Project>(() => newProjectTemplate());
  const [endpoints, setEndpoints] = useState<Endpoint[]>([]);
  const [results, setResults] = useState<TestResult[]>([]);
  const [notice, setNotice] = useState<Notice>(null);
  const [runState, setRunState] = useState("Idle");
  const [progress, setProgress] = useState(0);
  const [logs, setLogs] = useState<string[]>([]);
  const [projectSearch, setProjectSearch] = useState("");
  const [endpointSearch, setEndpointSearch] = useState("");
  const [resultSearch, setResultSearch] = useState("");
  const [resultType, setResultType] = useState("all");
  const [riskFilter, setRiskFilter] = useState("all");
  const [resultMode, setResultMode] = useState<"findings" | "raw">("findings");
  const [selectedResult, setSelectedResult] = useState<TestResult | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const [authType, setAuthType] = useState<AuthType>("jwt");
  const [jwt, setJwt] = useState("");
  const [cookie, setCookie] = useState("");
  const [headers, setHeaders] = useState("");
  const [useSpecDiscovery, setUseSpecDiscovery] = useState(true);
  const [useNativeApiList, setUseNativeApiList] = useState(true);
  const [apiListText, setApiListText] = useState("");
  const [selectedTypes, setSelectedTypes] = useState<string[]>(bendTypes.filter((_, index) => index < 8 || index === 14));
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
    const haystack = `${endpoint.id} ${endpoint.method} ${endpoint.path} ${endpoint.source.join(" ")}`.toLowerCase();
    return !endpointSearch || haystack.includes(endpointSearch.toLowerCase());
  });

  const resultTypes = useMemo(() => Array.from(new Set(results.map((result) => result.bendType))).sort(), [results]);
  const resultTypeSummary = useMemo(() => {
    const grouped = new Map<string, TestResult[]>();
    for (const result of results) {
      const list = grouped.get(result.bendType) || [];
      list.push(result);
      grouped.set(result.bendType, list);
    }
    return Array.from(grouped.entries())
      .map(([bendType, items]) => ({
        bendType,
        total: items.length,
        findings: items.filter((item) => item.interesting).length,
        maxRisk: Math.max(...items.map((item) => item.risk), 0),
        lastOutcome: items[items.length - 1]?.outcome || ""
      }))
      .sort((left, right) => right.findings - left.findings || right.maxRisk - left.maxRisk || left.bendType.localeCompare(right.bendType));
  }, [results]);
  const filteredResults = results.filter((result) => {
    const haystack = `${result.bendType} ${result.url} ${result.title} ${result.result.statusCode}`.toLowerCase();
    const riskMatch = riskFilter === "all" ||
      (riskFilter === "high" && result.risk >= 8) ||
      (riskFilter === "medium" && result.risk >= 5 && result.risk <= 7) ||
      (riskFilter === "low" && result.risk <= 4);
    const modeMatch = resultMode === "raw" || result.interesting;
    return modeMatch && (resultType === "all" || result.bendType === resultType) && riskMatch && (!resultSearch || haystack.includes(resultSearch.toLowerCase()));
  });

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
      await refreshArtifacts(loaded.projectId, true);
      info(`Selected project ${loaded.projectId}`);
      setView("run");
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

  async function saveProject() {
    try {
      const draft: Project = {
        ...project,
        projectId: slugify(project.projectId),
        name: project.name || "My Project",
        description: project.description || "",
        baseUrl: normalizeBaseUrl(project.baseUrl),
        headers: { Accept: "application/json" },
        auth: authFromInput(authType, jwt, cookie, headers),
        outputDir: `bend-results/${slugify(project.projectId)}`
      };
      const saved = await api.saveProject(draft);
      setProject(saved);
      setProjects(await api.listProjects());
      success(`Saved project ${saved.projectId} to ${saved.outputDir}`);
      setView("projects");
    } catch (error) {
      fail(error);
    }
  }

  async function startPipeline() {
    try {
      setConfirmOpen(false);
      setRunState("Saving project");
      setProgress(5);
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
      success(`Pipeline completed: ${completed.endpointCount} endpoints, ${completed.findingCount} findings, ${completed.resultCount} raw results`);
      setView("results");
    } catch (error) {
      setRunState("Idle");
      setProgress(0);
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
    const draft: Project = {
      ...project,
      projectId: slugify(project.projectId),
      baseUrl: normalizeBaseUrl(project.baseUrl),
      auth: authFromInput(authType, jwt, cookie, headers),
      outputDir: `bend-results/${slugify(project.projectId)}`
    };
    const saved = await api.saveProject(draft);
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
    setAuthType("jwt");
    setJwt("");
    setCookie("");
    setHeaders("");
    setView("run");
  }

  function success(message: string) {
    setNotice({ type: "success", message });
    setLogs((current) => [`${new Date().toISOString()} ${message}`, ...current].slice(0, 80));
  }

  function info(message: string) {
    setNotice({ type: "info", message });
    setLogs((current) => [`${new Date().toISOString()} ${message}`, ...current].slice(0, 80));
  }

  function fail(error: unknown) {
    const message = error instanceof Error ? error.message : String(error);
    setNotice({ type: "error", message });
    setLogs((current) => [`${new Date().toISOString()} Error: ${message}`, ...current].slice(0, 80));
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <img src="/bendit-logo1.png" alt="BendIt" className="brand-logo" />
          <div>
            <div className="brand-name">BendIt</div>
            <div className="brand-subtitle">API robustness auditor</div>
          </div>
        </div>
        <nav className="nav-list" aria-label="Primary">
          {[
            ["run", "Run"],
            ["projects", "Projects"],
            ["results", "Results"]
          ].map(([key, label]) => (
            <button className={`nav-item ${view === key ? "is-active" : ""}`} key={key} onClick={() => setView(key as View)}>
              {label}
            </button>
          ))}
        </nav>
        <div className="run-state">
          <div className="run-state-label">Current run</div>
          <div className="run-state-value">{runState}</div>
          <div className="progress-track"><div className="progress-fill" style={{ width: `${progress}%` }} /></div>
        </div>
      </aside>
      <main className="main">
        <header className="topbar">
          <div>
            <h1>{viewTitle(view)}</h1>
            <p>{viewMeta(view, project)}</p>
          </div>
          <div className="topbar-actions">
            <Button className="theme-toggle" title="Toggle theme" onClick={() => setTheme(theme === "dark" ? "light" : "dark")}>
              {theme === "dark" ? <Moon size={16} /> : <Sun size={16} />}
              <span>{theme === "dark" ? "Dark" : "Light"}</span>
            </Button>
            <Button onClick={() => downloadJSON("project.json", project)}><Download size={16} /> JSON</Button>
            <Button className="primary-button" onClick={saveProject}><Save size={16} /> Save project</Button>
          </div>
        </header>
        {notice && <div className={`notice ${notice.type}`}>{notice.message}</div>}
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
            <ProjectTable projects={filteredProjects} selectedId={project.projectId} onSelect={selectProject} />
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
            setApiListText={setApiListText}
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
            onStart={() => setConfirmOpen(true)}
          />
        )}
        {view === "results" && (
          <ResultsView
            results={filteredResults}
            allResults={results}
            resultTypeSummary={resultTypeSummary}
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
        <section className="run-log">
          <div className="section-head"><h2>Execution log</h2><Button onClick={() => setLogs([])}>Clear</Button></div>
          <ol>{logs.map((entry) => <li key={entry}>{entry}</li>)}</ol>
        </section>
      </main>
      <ConfirmDialog open={confirmOpen} onOpenChange={setConfirmOpen} onConfirm={startPipeline} />
    </div>
  );
}

function viewTitle(view: View) {
  return ({ run: "Run", projects: "Projects", results: "Results" })[view];
}

function viewMeta(view: View, project: Project) {
  const selected = project?.projectId ? `Selected: ${project.projectId}` : "No project selected";
  return ({
    run: `${selected}. Configure the target, discover endpoints, and run the test battery.`,
    projects: "Select a project or create a target.",
    results: `${selected}. Inspect stored evidence and export results.`
  })[view];
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
  return (
    <div className="table-shell">
      <table>
        <thead><tr><th>ID</th><th>Method</th><th>Path</th><th>Source</th><th>Class</th><th>Confidence</th><th>HTTP</th><th>Auth</th></tr></thead>
        <tbody>
          {props.endpoints.map((endpoint) => (
            <tr key={endpoint.id}>
              <td><code>{endpoint.id}</code></td>
              <td><span className="pill">{endpoint.method}</span></td>
              <td className="path-cell">{endpoint.path}</td>
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
  );
}

function endpointClass(classification?: string): "good" | "warn" | "danger" | "" {
  if (classification === "confirmed" || classification === "protected" || classification === "method_not_allowed") return "good";
  if (classification === "not_found" || classification === "soft_404") return "danger";
  if (classification) return "warn";
  return "";
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
  setApiListText: (value: string) => void;
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
  onStart: () => void;
}) {
  const processed = props.endpoints.filter((endpoint) => endpoint.status === "processed").length;
  return (
    <section className="run-dashboard">
      <section className="surface run-command">
        <div>
          <h2>Pipeline</h2>
          <p>Start saves the project, runs discovery, enqueues endpoints, then executes the selected test battery across the discovered endpoints.</p>
        </div>
        <div className="run-command-actions">
          <div className="run-state-value">{props.runState}</div>
          <div className="progress-track wide"><div className="progress-fill" style={{ width: `${props.progress}%` }} /></div>
          <Button className="primary-button start-button" onClick={props.onStart}><Play size={18} /> Start</Button>
        </div>
      </section>

      <Stepper progress={props.progress} />

      <SetupView
        project={props.project}
        setProject={props.setProject}
        authType={props.authType}
        setAuthType={props.setAuthType}
        jwt={props.jwt}
        setJwt={props.setJwt}
        cookie={props.cookie}
        setCookie={props.setCookie}
        headers={props.headers}
        setHeaders={props.setHeaders}
      />

      <div className="tests-layout">
        <section className="surface discovery-source">
          <div className="section-head">
            <h2>Discovery</h2>
            <div className="toggle-stack">
              <Toggle checked={props.useSpecDiscovery} onCheckedChange={props.setUseSpecDiscovery} label="Probe OpenAPI / Swagger" />
              <Toggle checked={props.useNativeApiList} onCheckedChange={props.setUseNativeApiList} label="Use native API list" />
            </div>
          </div>
          <Field label="Custom API list">
            <TextArea rows={7} value={props.apiListText} onChange={(event) => props.setApiListText(event.target.value)} placeholder={"GET /api/users\nPOST /api/users\nPATCH /api/users/{id}"} />
          </Field>
          <div className="file-row">
            <input type="file" accept=".txt,.json,text/plain,application/json" onChange={(event) => props.onLoadFile(event.target.files?.[0] || null)} />
          </div>
        </section>

        <section className="surface">
          <div className="section-head"><h2>Battery</h2><span className="pill">{props.selectedTypes.length} selected</span></div>
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
        </section>
      </div>

      <section className="surface form-grid">
        <Field label="Max requests per endpoint"><TextInput value={props.maxRequests} onChange={(event) => props.setMaxRequests(event.target.value)} /></Field>
        <Field label="Parallel workers"><TextInput value={props.parallelWorkers} onChange={(event) => props.setParallelWorkers(event.target.value)} /></Field>
        <Field label="Field sizes KB"><TextInput value={props.fieldSizes} onChange={(event) => props.setFieldSizes(event.target.value)} /></Field>
        <Field label="Body sizes KB"><TextInput value={props.bodySizes} onChange={(event) => props.setBodySizes(event.target.value)} /></Field>
        <Field label="Excluded path patterns" className="full-span"><TextArea rows={4} value={props.excludedPaths} onChange={(event) => props.setExcludedPaths(event.target.value)} /></Field>
      </section>

      <section>
        <div className="toolbar">
          <div className="metrics-grid compact">
            <Metric label="Registered" value={props.endpoints.length} />
            <Metric label="Processed" value={processed} />
          </div>
          <div className="toolbar-group">
            <Button onClick={() => downloadJSON("endpoints.json", { generatedAt: new Date().toISOString(), endpoints: props.endpoints })}><Download size={16} /> Export endpoints</Button>
            <TextInput value={props.endpointSearch} onChange={(event) => props.setEndpointSearch(event.target.value)} placeholder="Filter endpoints" />
          </div>
        </div>
        <EndpointTable endpoints={props.endpoints} />
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

function ProjectTable(props: { projects: Project[]; selectedId: string; onSelect: (id: string) => void }) {
  return (
    <div className="table-shell">
      <table>
        <thead><tr><th>Project</th><th>Base URL</th><th>Auth</th><th>Output</th><th>Updated</th></tr></thead>
        <tbody>
          {props.projects.map((project) => (
            <tr key={project.projectId} className={props.selectedId === project.projectId ? "is-selected" : ""} onClick={() => props.onSelect(project.projectId)}>
              <td><strong>{project.name || project.projectId}</strong><br /><code>{project.projectId}</code></td>
              <td className="path-cell">{project.baseUrl}</td>
              <td><span className="pill">{project.auth?.type || "none"}</span></td>
              <td className="path-cell">{project.outputDir}</td>
              <td>{project.updatedAt || ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SetupView(props: {
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
        <Field label="Project ID"><TextInput value={props.project.projectId} onChange={(event) => update({ projectId: event.target.value })} /></Field>
        <Field label="Name"><TextInput value={props.project.name} onChange={(event) => update({ name: event.target.value })} /></Field>
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
  results: TestResult[];
  allResults: TestResult[];
  resultTypeSummary: Array<{ bendType: string; total: number; findings: number; maxRisk: number; lastOutcome: string }>;
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
  const selected = props.selectedResult || props.results[0] || null;
  const critical = props.allResults.filter((result) => result.risk >= 9).length;
  const high = props.allResults.filter((result) => result.risk >= 7 && result.risk < 9).length;
  const medium = props.allResults.filter((result) => result.risk >= 5 && result.risk < 7).length;
  const findings = props.allResults.filter((result) => result.interesting).length;
  const raw = props.allResults.length;

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
      <div className="metrics-grid">
        <Metric label="Critical" value={critical} />
        <Metric label="High" value={high} />
        <Metric label="Medium" value={medium} />
        <Metric label="Findings" value={findings} />
      </div>

      <section className="surface type-summary" style={{ marginBottom: "18px" }}>
        <div className="section-head">
          <h2>Test execution summary by type</h2>
          <span className="muted-note">Click a test type to filter endpoints below</span>
        </div>
        <div className="table-shell">
          <table>
            <thead>
              <tr>
                <th>Test Type</th>
                <th>Probed Endpoints</th>
                <th>Success Status</th>
                <th>Risk Level</th>
                <th>Last Outcome</th>
              </tr>
            </thead>
            <tbody>
              {props.resultTypeSummary.map((item) => {
                const succeededCount = item.total - item.findings;
                const successPct = item.total > 0 ? Math.round((succeededCount / item.total) * 100) : 100;
                
                return (
                  <tr key={item.bendType} onClick={() => props.setResultType(item.bendType)} className={props.resultType === item.bendType ? "is-selected" : ""}>
                    <td>
                      <strong style={{ color: "var(--accent)" }}>{item.bendType}</strong>
                    </td>
                    <td>{item.total} endpoints tested</td>
                    <td>
                      <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                        <span className={`pill ${item.findings > 0 ? "danger" : "good"}`}>
                          {item.findings > 0 ? `${item.findings} Vulnerable` : "All Passed"}
                        </span>
                        <span style={{ fontSize: "12px", color: "var(--muted)" }}>
                          ({successPct}% secure)
                        </span>
                      </div>
                    </td>
                    <td>
                      <span className={`pill ${riskClass(item.maxRisk)}`}>
                        Risk {item.maxRisk}/10
                      </span>
                    </td>
                    <td>
                      <code style={{ fontSize: "12px" }}>{item.lastOutcome || "-"}</code>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
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
            <option value="all">All risks</option>
            <option value="high">Risk 8-10</option>
            <option value="medium">Risk 5-7</option>
            <option value="low">Risk 1-4</option>
          </select>
        </div>
        <div className="toolbar-group">
          <Button onClick={() => downloadJSON("results.json", { generatedAt: new Date().toISOString(), results: props.allResults })}><Download size={16} /> Export</Button>
          <TextInput value={props.resultSearch} onChange={(event) => props.setResultSearch(event.target.value)} placeholder="Filter results" />
        </div>
      </div>

      <div className="results-layout">
        <div className="table-shell">
          <table>
            <thead><tr><th>Risk</th><th>Outcome</th><th>Type</th><th>Status</th><th>Endpoint</th><th>Time</th></tr></thead>
            <tbody>
              {props.results.map((result) => (
                <tr key={result.id} className={selected?.id === result.id ? "is-selected" : ""} onClick={() => props.setSelectedResult(result)}>
                  <td><span className={`pill ${riskClass(result.risk)}`}>{result.risk}</span></td>
                  <td>
                    <span className={`pill ${result.interesting ? "danger" : "good"}`}>
                      {result.interesting ? "Insecure" : "Secure"}
                    </span>
                  </td>
                  <td>{result.bendType}</td>
                  <td>{result.result.statusCode}</td>
                  <td className="path-cell">{result.method} {new URL(result.url).pathname}</td>
                  <td>{new Date(result.createdAt).toLocaleTimeString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <section className="result-detail" style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
          {!selected ? <div className="empty-state">Select a result to inspect request/response payloads.</div> : (
            <>
              <div className="section-head" style={{ marginBottom: "8px" }}>
                <h2>{selected.title}</h2>
                <span className={`pill ${riskClass(selected.risk)}`}>Risk {selected.risk}</span>
              </div>

              {/* Navigation Tabs */}
              <div style={{ display: "flex", gap: "6px", borderBottom: "1px solid var(--line)", paddingBottom: "8px", marginBottom: "8px" }}>
                {(["verdict", "request", "response", "mutation"] as const).map((tab) => (
                  <button
                    key={tab}
                    onClick={() => setActiveTab(tab)}
                    style={{
                      flex: 1,
                      minHeight: "30px",
                      fontSize: "12px",
                      fontWeight: activeTab === tab ? "700" : "450",
                      border: "1px solid",
                      borderColor: activeTab === tab ? "var(--accent)" : "var(--line)",
                      background: activeTab === tab ? "color-mix(in srgb, var(--accent) 15%, var(--panel))" : "var(--panel-strong)",
                      color: activeTab === tab ? "var(--accent)" : "var(--ink)",
                      borderRadius: "4px",
                      padding: "2px 6px"
                    }}
                  >
                    {tab.toUpperCase()}
                  </button>
                ))}
              </div>

              {/* Tab Contents */}
              <div style={{ flex: 1, overflow: "auto" }}>
                {activeTab === "verdict" && (
                  <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
                    {selected.interesting ? (
                      <div style={{
                        border: "1px solid color-mix(in srgb, var(--danger) 60%, var(--line))",
                        background: "color-mix(in srgb, var(--danger) 10%, var(--panel))",
                        color: "var(--danger)",
                        padding: "10px 12px",
                        borderRadius: "6px",
                        fontSize: "13px",
                        fontWeight: "600"
                      }}>
                        ⚠️ Finding Detected: Endpoint returned vulnerable or unexpected response to payload audit.
                      </div>
                    ) : (
                      <div style={{
                        border: "1px solid color-mix(in srgb, var(--good) 60%, var(--line))",
                        background: "color-mix(in srgb, var(--good) 10%, var(--panel))",
                        color: "var(--good)",
                        padding: "10px 12px",
                        borderRadius: "6px",
                        fontSize: "13px",
                        fontWeight: "600"
                      }}>
                        ✓ Correct Handling: Endpoint rejected or safely handled mutated payload (Secure/Blocked).
                      </div>
                    )}

                    <div className="detail-grid" style={{ margin: "0" }}>
                      <Detail label="Test Type" value={selected.bendType} />
                      <Detail label="Verdict Class" value={selected.outcome} />
                      <Detail label="Status Code" value={`${selected.result.statusCode} ${selected.result.statusText}`} />
                      <Detail label="Severity Level" value={selected.severity} />
                    </div>

                    <div style={{ marginTop: "4px" }}>
                      <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", marginBottom: "4px" }}>Analysis Summary</strong>
                      <p style={{ margin: "0 0 12px 0", fontSize: "14px", lineHeight: "1.4" }}>{selected.analysisSummary}</p>
                      
                      <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", marginBottom: "4px" }}>Evidence</strong>
                      <p style={{ margin: "0", fontSize: "13px", lineHeight: "1.4", fontStyle: "italic", color: "var(--muted)" }}>{selected.evidence}</p>
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
                        {selected.result.contentType || "unknown content-type"} • {selected.result.bodySizeBytes} bytes
                      </span>
                    </div>

                    <strong style={{ display: "block", fontSize: "13px", color: "var(--muted)", marginBottom: "6px" }}>Response Body</strong>
                    <pre style={{ margin: "0", background: "#0b1014", border: "1px solid var(--line)", padding: "10px" }}>
                      {prettyBody(selected.resultBody) || "Empty response body."}
                    </pre>
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

function Metric(props: { label: string; value: number }) {
  return <div className="metric"><span>{props.label}</span><strong>{props.value}</strong></div>;
}

function Detail(props: { label: string; value: string }) {
  return <div className="detail-item"><span>{props.label}</span><strong>{props.value}</strong></div>;
}

createRoot(document.getElementById("root")!).render(<React.StrictMode><App /></React.StrictMode>);
