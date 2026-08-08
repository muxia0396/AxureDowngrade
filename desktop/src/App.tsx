import { useEffect, useState } from "react";
import {
  Badge,
  Button,
  FluentProvider,
  MessageBar,
  MessageBarBody,
  ProgressBar,
  Spinner,
  Text,
  webLightTheme,
} from "@fluentui/react-components";
import {
  ArrowRight20Regular,
  CheckmarkCircle20Regular,
  DocumentSearch20Regular,
  FolderOpen20Regular,
  LockClosed16Regular,
} from "@fluentui/react-icons";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { open, save } from "@tauri-apps/plugin-dialog";
import "./App.css";

type AnalysisState =
  | "empty"
  | "loading"
  | "ready"
  | "converting"
  | "converted"
  | "error";

interface FormatProbe {
  path: string;
  fileSize: number;
  modifiedAtUnixMs: number | null;
  container:
    | "zip"
    | "oleCompoundFile"
    | "sevenZip"
    | "gzip"
    | "xml"
    | "json"
    | "unknownBinary"
    | "empty";
  magicHex: string;
  versionHints: string[];
  printableRatioPerMille: number;
  warnings: string[];
}

interface DowngradeReport {
  outputPath: string;
  packageCount: number;
  pageCount: number;
  objectPackageCount: number;
  designDocumentsRewritten: number;
  settingsRewritten: number;
  interactionsRemoved: number;
  unsupportedStylePropertiesRemoved: number;
  rp9RequiredFieldsAdded: number;
  unsupportedWorkspaceTabsRemoved: number;
  unsupportedSettingsPropertiesRemoved: number;
  staticRecordsVerified: number;
  staticScalarsVerified: number;
  bridgeOutput: string;
}

interface DowngradeProgress {
  percent: number;
  stage: string;
}

interface DisplayError {
  code: string;
  message: string;
  details: string;
}

const AXURE9_DIRECTORY_STORAGE_KEY = "axureDowngrade.axure9Directory";

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KiB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MiB`;
}

function fileName(path: string): string {
  const parts = path.split(/[\\/]/);
  return parts[parts.length - 1] || path;
}

function normalizeError(reason: unknown, fallbackCode = "ADG-9000"): DisplayError {
  if (typeof reason === "string") {
    try {
      return normalizeError(JSON.parse(reason), fallbackCode);
    } catch {
      if (reason.includes("dialog|save not allowed by ACL")) {
        return {
          code: "ADG-2101",
          message: "程序没有获得打开保存对话框的权限",
          details: reason,
        };
      }
      return { code: fallbackCode, message: "操作失败", details: reason };
    }
  }

  if (reason && typeof reason === "object") {
    const value = reason as Partial<DisplayError>;
    if (value.code || value.message || value.details) {
      return {
        code: value.code || fallbackCode,
        message: value.message || "发生未知错误",
        details: value.details || "",
      };
    }
  }

  return { code: fallbackCode, message: "操作失败", details: String(reason) };
}

function App() {
  const [state, setState] = useState<AnalysisState>("empty");
  const [probe, setProbe] = useState<FormatProbe | null>(null);
  const [downgradeReport, setDowngradeReport] =
    useState<DowngradeReport | null>(null);
  const [error, setError] = useState<DisplayError | null>(null);
  const [progress, setProgress] = useState<DowngradeProgress>({
    percent: 0,
    stage: "正在准备静态降级",
  });
  const [axure9Directory, setAxure9Directory] = useState(
    () => localStorage.getItem(AXURE9_DIRECTORY_STORAGE_KEY) ?? "",
  );

  useEffect(() => {
    let stopListening: (() => void) | undefined;
    void listen<DowngradeProgress>("downgrade-progress", (event) => {
      setProgress({
        percent: Math.max(0, Math.min(100, event.payload.percent)),
        stage: event.payload.stage,
      });
    }).then((unlisten) => {
      stopListening = unlisten;
    });
    return () => stopListening?.();
  }, []);

  async function selectAndAnalyze() {
    const selected = await open({
      title: "选择 Axure RP 文件",
      multiple: false,
      directory: false,
      filters: [{ name: "Axure RP 文件", extensions: ["rp"] }],
    });

    if (!selected) return;

    setState("loading");
    setProbe(null);
    setDowngradeReport(null);
    setError(null);
    try {
      const result = await invoke<FormatProbe>("analyze_rp", { path: selected });
      setProbe(result);
      setState("ready");
    } catch (reason) {
      setError(normalizeError(reason, "ADG-2001"));
      setState("error");
    }
  }

  async function selectAndDowngrade() {
    if (!probe) return;

    const sourceProbe = probe;
    setState("converting");
    setDowngradeReport(null);
    setError(null);
    setProgress({ percent: 0, stage: "正在响应转换请求" });

    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

    try {
      let selectedAxure9Directory = axure9Directory;
      setProgress({ percent: 1, stage: "正在检查 RP9 安装目录" });
      if (
        !selectedAxure9Directory ||
        !(await invoke<boolean>("validate_axure9_directory", {
          path: selectedAxure9Directory,
        }))
      ) {
        localStorage.removeItem(AXURE9_DIRECTORY_STORAGE_KEY);
        selectedAxure9Directory = "";
        setProgress({ percent: 1, stage: "等待选择 RP9 安装目录" });

        const selected = await open({
          title: "选择包含 AxureRP9.exe 的安装目录",
          multiple: false,
          directory: true,
        });
        if (!selected) {
          setState("ready");
          return;
        }

        const isValid = await invoke<boolean>("validate_axure9_directory", {
          path: selected,
        });
        if (!isValid) {
          setError({
            code: "ADG-1003",
            message: "所选目录中没有 AxureRP9.exe",
            details: selected,
          });
          setState("error");
          return;
        }

        selectedAxure9Directory = selected;
        setAxure9Directory(selected);
        localStorage.setItem(AXURE9_DIRECTORY_STORAGE_KEY, selected);
      }

      setProgress({ percent: 2, stage: "等待选择 RP9 文件保存位置" });
      const suggestedOutput = sourceProbe.path.replace(/\.rp$/i, "-rp9.rp");
      const outputPath = await save({
        title: "保存降级后的 Axure RP 9 文件",
        defaultPath: suggestedOutput,
        filters: [{ name: "Axure RP 文件", extensions: ["rp"] }],
      });
      if (!outputPath) {
        setState("ready");
        return;
      }

      setProgress({ percent: 3, stage: "正在启动静态降级" });
      const result = await invoke<DowngradeReport>("downgrade_rp", {
        sourcePath: sourceProbe.path,
        outputPath,
        axure9Directory: selectedAxure9Directory,
      });
      setDowngradeReport(result);
      setState("converted");
    } catch (reason) {
      setError(normalizeError(reason));
      setState("error");
    }
  }

  function clearSelectedFile() {
    setProbe(null);
    setDowngradeReport(null);
    setError(null);
    setProgress({ percent: 0, stage: "正在准备静态降级" });
    setState("empty");
  }

  async function changeAxure9Directory() {
    const selected = await open({
      title: "选择包含 AxureRP9.exe 的安装目录",
      defaultPath: axure9Directory || undefined,
      multiple: false,
      directory: true,
    });
    if (!selected) return;

    const isValid = await invoke<boolean>("validate_axure9_directory", {
      path: selected,
    });
    if (!isValid) {
      setError({
        code: "ADG-1003",
        message: "所选目录中没有 AxureRP9.exe",
        details: selected,
      });
      setState("error");
      return;
    }

    setAxure9Directory(selected);
    localStorage.setItem(AXURE9_DIRECTORY_STORAGE_KEY, selected);
    setError(null);
    if (probe) setState("ready");
  }

  return (
    <FluentProvider theme={webLightTheme}>
      <main className="app-shell">
        {state === "converting" && (
          <div
            className="progress-overlay"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="downgrade-progress-title"
            aria-describedby="downgrade-progress-status"
          >
            <div className="progress-dialog">
              <div className="progress-heading">
                <Text id="downgrade-progress-title" size={500} weight="semibold">
                  正在转换为 RP 9 工程
                </Text>
                <Text weight="semibold">{progress.percent}%</Text>
              </div>
              <Text
                id="downgrade-progress-status"
                className="progress-status"
                title={progress.stage}
              >
                {progress.stage}
              </Text>
              <ProgressBar
                value={progress.percent / 100}
                max={1}
                thickness="large"
              />
              <Text className="progress-hint">请勿关闭窗口，原文件不会被修改</Text>
            </div>
          </div>
        )}

        <header className="app-header">
          <div className="brand">
            <div className="brand-symbol" aria-hidden="true">
              <img src="/app-logo-rounded.png" alt="" />
            </div>
            <div className="brand-copy">
              <Text className="product-mark" weight="semibold">
                Axure Downgrade
              </Text>
              <Text className="product-caption">静态界面保真降级工具</Text>
            </div>
          </div>
          <Badge appearance="tint" color="informative" shape="rounded">
            版本 0.1.7
          </Badge>
        </header>

        <section className="intro">
          <div className="intro-copy">
            <div className="eyebrow">RP 11 → RP 9</div>
            <h1>
              把 RP 11 文件转换为
              <span>RP 9 可编辑工程</span>
            </h1>
            <Text size={400}>
              保留页面、文字、图片、基础样式与绝对位置
            </Text>
          </div>
          <Button
            className="select-button"
            appearance="primary"
            icon={<FolderOpen20Regular />}
            size="large"
            onClick={selectAndAnalyze}
            disabled={state === "loading"}
          >
            选择文件
          </Button>
        </section>

        {state === "empty" && (
          <section className="workspace-panel empty-panel" aria-label="等待选择文件">
            <div className="empty-visual">
              <DocumentSearch20Regular />
            </div>
            <Text size={500} weight="semibold">
              文件待选择
            </Text>
            <div className="empty-description">
              <Text>请选择一个 Axure RP 11 文件</Text>
              <Text>文件仅在本机读取，不会上传</Text>
            </div>
          </section>
        )}

        {state === "loading" && (
          <section className="workspace-panel loading-panel" aria-live="polite">
            <Spinner size="medium" label="正在读取文件信息…" />
            <Text className="secondary-text">正在识别容器、文件头与版本线索</Text>
          </section>
        )}

        {state === "error" && (
          <MessageBar intent="error" className="error-bar">
            <MessageBarBody className="error-report">
              <strong>
                [{error?.code ?? "ADG-9000"}] {error?.message ?? "操作失败"}
              </strong>
              {error?.details && <pre className="error-details">{error.details}</pre>}
              <Button appearance="subtle" onClick={selectAndAnalyze}>
                重新选择文件
              </Button>
            </MessageBarBody>
          </MessageBar>
        )}

        {(state === "ready" || state === "converting" || state === "converted") &&
          probe && (
            <section className="workspace-panel file-panel" aria-live="polite">
              <div className="file-summary">
                <div className="file-icon">
                  <CheckmarkCircle20Regular />
                </div>
                <div className="file-title">
                  <Text size={500} weight="semibold">
                    {fileName(probe.path)}
                  </Text>
                  <Text className="file-path" title={probe.path}>
                    {probe.path}
                  </Text>
                </div>
                <div className="file-actions">
                  <Button appearance="subtle" onClick={selectAndAnalyze}>
                    更换文件
                  </Button>
                  <Button appearance="subtle" onClick={clearSelectedFile}>
                    清除文件
                  </Button>
                </div>
              </div>

              <div className="file-content">
                <div className="detail-block">
                  <Text className="section-label" weight="semibold">
                    文件详情
                  </Text>
                  <dl className="facts">
                    <div>
                      <dt>文件大小</dt>
                      <dd>{formatBytes(probe.fileSize)}</dd>
                    </div>
                    <div>
                      <dt>文件修改日期</dt>
                      <dd>
                        {probe.modifiedAtUnixMs
                          ? new Date(probe.modifiedAtUnixMs).toLocaleString(
                              "zh-CN",
                              {
                                year: "numeric",
                                month: "2-digit",
                                day: "2-digit",
                                hour: "2-digit",
                                minute: "2-digit",
                                hour12: false,
                              },
                            )
                          : "无法读取"}
                      </dd>
                    </div>
                    <div>
                      <dt>文件格式</dt>
                      <dd>Axure RP 文件（.rp）</dd>
                    </div>
                  </dl>
                </div>

                <aside className="conversion-block">
                  <div>
                    <Text className="section-label" weight="semibold">
                      转换设置
                    </Text>
                    <Text className="conversion-copy">
                      静态兼容模式将保留页面结构、文本、图片与基础样式
                    </Text>
                  </div>
                  <div className="directory-setting">
                    <Text className="directory-label">Axure RP 9 目录</Text>
                    <Text className="directory-value" title={axure9Directory}>
                      {axure9Directory || "首次转换时选择"}
                    </Text>
                    <Button
                      appearance="subtle"
                      size="small"
                      disabled={state === "converting"}
                      onClick={changeAxure9Directory}
                    >
                      {axure9Directory ? "更改目录" : "提前设置"}
                    </Button>
                  </div>
                  <Button
                    className="convert-button"
                    appearance="primary"
                    icon={<ArrowRight20Regular />}
                    iconPosition="after"
                    size="large"
                    disabled={state === "converting"}
                    onClick={selectAndDowngrade}
                  >
                    {state === "converting" ? "正在转换…" : "开始转换"}
                  </Button>
                </aside>
              </div>

              {state === "converted" && downgradeReport && (
                <div className="success-report">
                  <CheckmarkCircle20Regular />
                  <div>
                    <Text weight="semibold">RP 9 文件已生成</Text>
                    <Text className="success-path">
                      {downgradeReport.outputPath}
                    </Text>
                    <Text className="secondary-text">
                      已处理 {downgradeReport.pageCount} 个页面、
                      {downgradeReport.objectPackageCount} 个附属对象包，并重写{" "}
                      {downgradeReport.designDocumentsRewritten} 个设计文档
                    </Text>
                  </div>
                </div>
              )}
            </section>
          )}

        <div className="privacy-note">
          <LockClosed16Regular />
          <Text>本地处理 · 原文件保持不变</Text>
        </div>
      </main>
    </FluentProvider>
  );
}

export default App;
