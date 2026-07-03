export type AuthType = "jwt" | "cookie" | "headers" | "none";
export type TargetType = "webApi" | "webPage";

export type Project = {
  projectId: string;
  name: string;
  description?: string;
  baseUrl: string;
  isWebPage?: boolean;
  headers?: Record<string, string>;
  auth: AuthConfig;
  outputDir: string;
  createdAt?: string;
  updatedAt?: string;
};

export type AuthConfig = {
  type: AuthType;
  headerName?: string;
  scheme?: string;
  tokenMasked?: string;
  cookieMasked?: string;
  headersMasked?: Array<{ name: string; value: string }>;
};

export type Endpoint = {
  id: string;
  method: string;
  scheme: string;
  host: string;
  port?: number | null;
  path: string;
  queryParams: string[];
  source: string[];
  sourceDetail?: string;
  authRequired: boolean;
  status: string;
  statusCode?: number;
  contentType?: string;
  responseLength?: number;
  responseHash?: string;
  confidence?: number;
  classification?: string;
  protected?: boolean;
  redirectedTo?: string;
  firstSeenAt: string;
  lastSeenAt: string;
  verifiedAt?: string;
};

export type TestResult = {
  id: string;
  projectId: string;
  testRunId: string;
  endpointId: string;
  bendType: string;
  category: string;
  title: string;
  method: string;
  url: string;
  originalUrl: string;
  mutation: Record<string, unknown>;
  request: {
    headersMasked: Record<string, string>;
    body: string | null;
    bodySizeBytes: number;
  };
  result: {
    statusCode: number;
    statusText: string;
    contentType: string;
    bodySizeBytes: number;
    durationMs: number;
  };
  resultBody: string;
  evidence: string;
  outcome: string;
  interesting: boolean;
  analysisSummary: string;
  risk: number;
  severity: string;
  confidence: number;
  reproducible: boolean;
  sensitiveDataDetected: boolean;
  tokenUsed: boolean;
  authContext: AuthConfig;
  createdAt: string;
};

export type DiscoveryConfig = {
  useNativeApiList: boolean;
  useSpecDiscovery: boolean;
  apiList: string[];
};

export type TestConfig = {
  bendTypes: string[];
  maxRequestsPerEndpoint: number;
  parallelWorkers: number;
  fieldSizesKb: number[];
  bodySizesKb: number[];
  excludedPathPatterns: string[];
};

export type RunDocument = {
  runId: string;
  projectId: string;
  status: "queued" | "running" | "completed" | "failed";
  currentStep: "api_specs" | "discovery" | "tests" | "results" | "analysis";
  progress: number;
  startedAt: string;
  completedAt?: string;
  endpointCount: number;
  resultCount: number;
  findingCount: number;
  error?: string;
};

export type RunConfig = {
  discovery: DiscoveryConfig;
  tests: TestConfig;
};

export type Notice = {
  type: "success" | "error" | "info";
  message: string;
} | null;
