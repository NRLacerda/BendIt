import type { AuthConfig, AuthType, Project } from "./types";

export const nowUtc = () => new Date().toISOString().replace(/\.\d{3}Z$/, "Z");

export function slugify(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "my-project";
}

export function normalizeBaseUrl(value: string): string {
  try {
    return new URL(value).origin;
  } catch {
    return value;
  }
}

export function normalizeTargetUrl(value: string, isWebPage: boolean): string {
  if (!isWebPage) return normalizeBaseUrl(value);
  try {
    const url = new URL(value);
    url.hash = "";
    return url.toString();
  } catch {
    return value;
  }
}

export function maskSecret(value: string): string {
  if (!value) return "";
  if (value.length <= 10) return "***";
  return `${value.slice(0, 8)}...***`;
}

export function parseNumberList(value: string): number[] {
  const parsed = value.split(",").map((item) => Number(item.trim())).filter((item) => Number.isFinite(item) && item > 0);
  return parsed.length ? parsed : [1, 10, 50, 100];
}

export function parseLines(value: string): string[] {
  return value.split("\n").map((line) => line.trim()).filter((line) => line && !line.startsWith("#"));
}

export function parseHeaders(value: string): Array<{ name: string; value: string }> {
  return value.split("\n").map((line) => {
    const index = line.indexOf(":");
    if (index === -1) return null;
    return { name: line.slice(0, index).trim(), value: line.slice(index + 1).trim() };
  }).filter((header): header is { name: string; value: string } => Boolean(header?.name));
}

export function authFromInput(type: AuthType, jwt: string, cookie: string, headers: string): AuthConfig {
  if (type === "jwt") {
    return { type, headerName: "Authorization", scheme: "Bearer", tokenMasked: maskSecret(jwt) };
  }
  if (type === "cookie") {
    return { type, headerName: "Cookie", cookieMasked: maskSecret(cookie) };
  }
  if (type === "headers") {
    return {
      type,
      headersMasked: parseHeaders(headers).map((header) => ({ name: header.name, value: maskSecret(header.value) }))
    };
  }
  return { type: "none" };
}

export function newProjectTemplate(): Project {
  const createdAt = nowUtc();
  return {
    projectId: "my-project",
    name: "My Project",
    description: "",
    baseUrl: "https://api.example.com",
    isWebPage: false,
    headers: { Accept: "application/json" },
    auth: { type: "jwt", headerName: "Authorization", scheme: "Bearer", tokenMasked: "" },
    outputDir: "bend-results/my-project",
    createdAt,
    updatedAt: createdAt
  };
}

export function downloadJSON(filename: string, value: unknown) {
  const blob = new Blob([JSON.stringify(value, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export function prettyBody(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

export function riskClass(risk: number): "good" | "warn" | "danger" {
  if (risk >= 8) return "danger";
  if (risk >= 5) return "warn";
  return "good";
}
