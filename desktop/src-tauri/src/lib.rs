use serde::{Deserialize, Serialize};
use std::{
    fs::OpenOptions,
    io::{BufRead, BufReader, Read},
    path::{Path, PathBuf},
    process::{Command, Stdio},
    time::UNIX_EPOCH,
};
#[cfg(windows)]
use std::os::windows::fs::OpenOptionsExt;
use tauri::{Emitter, Manager};

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct DowngradeProgress {
    percent: u8,
    stage: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct CommandError {
    code: &'static str,
    message: String,
    details: String,
}

impl CommandError {
    fn new(code: &'static str, message: impl Into<String>, details: impl Into<String>) -> Self {
        Self {
            code,
            message: message.into(),
            details: details.into(),
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct DowngradeReport {
    output_path: String,
    package_count: usize,
    page_count: usize,
    object_package_count: usize,
    design_documents_rewritten: usize,
    settings_rewritten: usize,
    interactions_removed: usize,
    unsupported_style_properties_removed: usize,
    rp9_required_fields_added: usize,
    unsupported_workspace_tabs_removed: usize,
    unsupported_settings_properties_removed: usize,
    static_records_verified: usize,
    static_scalars_verified: usize,
    bridge_output: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct AnalysisReport {
    #[serde(flatten)]
    probe: axure_core::FormatProbe,
    modified_at_unix_ms: Option<u64>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct BridgeReport {
    status: String,
    parts: usize,
    gzip_parts: usize,
    pages_rewritten: usize,
    object_packages_rewritten: usize,
    design_documents_rewritten: usize,
    settings_rewritten: usize,
    interactions_removed: usize,
    unsupported_style_properties_removed: usize,
    rp9_required_fields_added: usize,
    unsupported_workspace_tabs_removed: usize,
    unsupported_settings_properties_removed: usize,
    static_records_verified: usize,
    static_scalars_verified: usize,
}

#[tauri::command]
fn analyze_rp(path: String) -> Result<AnalysisReport, String> {
    let modified_at_unix_ms = std::fs::metadata(&path)
        .and_then(|metadata| metadata.modified())
        .ok()
        .and_then(|modified| modified.duration_since(UNIX_EPOCH).ok())
        .and_then(|duration| u64::try_from(duration.as_millis()).ok());
    let probe = axure_core::probe_file(path).map_err(|error| error.to_string())?;
    Ok(AnalysisReport {
        probe,
        modified_at_unix_ms,
    })
}

#[tauri::command]
fn compare_rp(left_path: String, right_path: String) -> Result<axure_core::PairReport, String> {
    axure_core::compare_files(left_path, right_path).map_err(|error| error.to_string())
}

#[tauri::command]
fn validate_axure9_directory(path: String) -> bool {
    Path::new(&path).join("AxureRP9.exe").is_file()
}

#[tauri::command]
fn downgrade_rp(
    app: tauri::AppHandle,
    source_path: String,
    output_path: String,
    axure9_directory: String,
) -> Result<DowngradeReport, CommandError> {
    let source = PathBuf::from(&source_path);
    let output = PathBuf::from(&output_path);
    let axure9_directory = PathBuf::from(&axure9_directory);

    emit_progress(&app, 2, "正在检查输入、输出路径");
    if source == output {
        return Err(CommandError::new(
            "ADG-1001",
            "输出路径不能覆盖原始 RP11 文件",
            format!("输入和输出均为：{}", source.display()),
        ));
    }
    if !source.is_file() {
        return Err(CommandError::new(
            "ADG-1002",
            "找不到输入文件",
            source.display().to_string(),
        ));
    }
    if !axure9_directory.join("AxureRP9.exe").is_file() {
        return Err(CommandError::new(
            "ADG-1003",
            "所选目录中没有 AxureRP9.exe",
            axure9_directory.display().to_string(),
        ));
    }
    ensure_output_is_available(&output).map_err(|details| {
        CommandError::new("ADG-1004", "输出文件不可写或正在被占用", details)
    })?;

    emit_progress(&app, 7, "正在解析 RP11 文件结构");
    let source_report =
        axure_core::inspect_rp_packages(&source).map_err(|error| {
            CommandError::new("ADG-1101", "无法解析输入文件结构", error.to_string())
        })?;
    if source_report.format_major != 11 {
        return Err(CommandError::new(
            "ADG-1102",
            "当前只支持 RP11 输入",
            format!("检测到主版本 {}", source_report.format_major),
        ));
    }

    emit_progress(&app, 11, "正在加载 RP9 转换组件");
    let bridge = locate_bridge(&app)
        .map_err(|details| CommandError::new("ADG-1201", "找不到转换桥接器", details))?;
    let bridge_directory = bridge
        .parent()
        .ok_or_else(|| {
            CommandError::new(
                "ADG-1201",
                "无法确定转换桥接器目录",
                bridge.display().to_string(),
            )
        })?;
    let mut child = Command::new(&bridge)
        .current_dir(bridge_directory)
        .arg(&axure9_directory)
        .arg(&source)
        .arg(&output)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|error| {
            CommandError::new(
                "ADG-1202",
                "无法启动转换桥接器",
                error.to_string(),
            )
        })?;

    let stderr = child.stderr.take().ok_or_else(|| {
        CommandError::new("ADG-1202", "无法读取转换桥接器状态", "stderr 管道不可用")
    })?;
    let mut bridge_errors = Vec::new();
    for line in BufReader::new(stderr).lines() {
        match line {
            Ok(line) => {
                if let Some((percent, stage)) = parse_bridge_progress(&line) {
                    emit_progress(&app, percent, bridge_stage_label(stage));
                } else if !line.trim().is_empty() {
                    bridge_errors.push(line);
                }
            }
            Err(error) => bridge_errors.push(format!("读取桥接器输出失败：{error}")),
        }
    }

    let mut stdout = Vec::new();
    child
        .stdout
        .take()
        .ok_or_else(|| {
            CommandError::new("ADG-1202", "无法读取转换桥接器报告", "stdout 管道不可用")
        })?
        .read_to_end(&mut stdout)
        .map_err(|error| {
            CommandError::new("ADG-1204", "无法读取转换桥接器报告", error.to_string())
        })?;
    let status = child.wait().map_err(|error| {
        CommandError::new("ADG-1203", "等待转换桥接器结束时失败", error.to_string())
    })?;

    if !status.success() {
        let details = if bridge_errors.is_empty() {
            format!("进程退出状态：{status}")
        } else {
            bridge_errors.join("\n")
        };
        return Err(CommandError::new(
            "ADG-1203",
            "转换桥接器执行失败",
            details,
        ));
    }

    let bridge_output = String::from_utf8_lossy(&stdout).trim().to_owned();
    let bridge_report: BridgeReport = serde_json::from_str(&bridge_output)
        .map_err(|error| {
            CommandError::new(
                "ADG-1204",
                "转换桥接器返回了无法识别的验证报告",
                format!("{error}\n原始报告：{bridge_output}"),
            )
        })?;
    if bridge_report.status != "success" {
        return Err(CommandError::new(
            "ADG-1205",
            "转换桥接器未报告成功",
            bridge_report.status,
        ));
    }

    emit_progress(&app, 96, "正在回读并验证 RP9 文件");
    let output_report =
        axure_core::inspect_rp_packages(&output).map_err(|error| {
            CommandError::new("ADG-1301", "无法回读转换结果", error.to_string())
        })?;
    if output_report.format_major != 9 {
        return Err(CommandError::new(
            "ADG-1302",
            "转换结果版本验证失败",
            format!("输出主版本为 {}", output_report.format_major),
        ));
    }
    let page_count = bridge_report.pages_rewritten;
    let object_package_count = bridge_report.object_packages_rewritten;
    if bridge_report.gzip_parts != output_report.packages.len()
        || bridge_report.design_documents_rewritten == 0
        || bridge_report.settings_rewritten == 0
        || bridge_report.rp9_required_fields_added == 0
        || bridge_report.static_records_verified == 0
        || bridge_report.static_scalars_verified == 0
    {
        return Err(CommandError::new(
            "ADG-1303",
            "转换结果完整性校验失败",
            format!(
                "桥接器报告 {} 个容器部件（其中 {} 个 GZip 包）、{} 个页面、{} 个设计文档、{} 个设置包；实际识别到 {} 个 GZip 包、{} 个页面",
                bridge_report.parts,
                bridge_report.gzip_parts,
                bridge_report.pages_rewritten,
                bridge_report.design_documents_rewritten,
                bridge_report.settings_rewritten,
                output_report.packages.len(),
                page_count
            ),
        ));
    }

    emit_progress(&app, 99, "正在确认输出文件已释放");
    wait_for_output_release(&output).map_err(|details| {
        CommandError::new(
            "ADG-1304",
            "转换完成，但输出文件仍被其他进程占用",
            details,
        )
    })?;
    emit_progress(&app, 100, "静态降级完成");
    Ok(DowngradeReport {
        output_path: output.display().to_string(),
        package_count: output_report.packages.len(),
        page_count,
        object_package_count,
        design_documents_rewritten: bridge_report.design_documents_rewritten,
        settings_rewritten: bridge_report.settings_rewritten,
        interactions_removed: bridge_report.interactions_removed,
        unsupported_style_properties_removed:
            bridge_report.unsupported_style_properties_removed,
        rp9_required_fields_added: bridge_report.rp9_required_fields_added,
        unsupported_workspace_tabs_removed:
            bridge_report.unsupported_workspace_tabs_removed,
        unsupported_settings_properties_removed:
            bridge_report.unsupported_settings_properties_removed,
        static_records_verified: bridge_report.static_records_verified,
        static_scalars_verified: bridge_report.static_scalars_verified,
        bridge_output,
    })
}

fn wait_for_output_release(output: &Path) -> Result<(), String> {
    let mut last_error = None;
    for _ in 0..20 {
        match ensure_output_is_available(output) {
            Ok(()) => return Ok(()),
            Err(error) => last_error = Some(error),
        }
        std::thread::sleep(std::time::Duration::from_millis(100));
    }
    Err(last_error.unwrap_or_else(|| {
        format!("无法确认输出文件已释放：{}", output.display())
    }))
}

fn emit_progress(app: &tauri::AppHandle, percent: u8, stage: impl Into<String>) {
    let _ = app.emit(
        "downgrade-progress",
        DowngradeProgress {
            percent,
            stage: stage.into(),
        },
    );
}

fn parse_bridge_progress(line: &str) -> Option<(u8, &str)> {
    let mut fields = line.splitn(3, '\t');
    if fields.next()? != "PROGRESS" {
        return None;
    }
    let percent = fields.next()?.parse().ok()?;
    Some((percent, fields.next()?))
}

fn bridge_stage_label(stage: &str) -> &'static str {
    match stage {
        "initialize_serializer" => "正在初始化 RP9 序列化引擎",
        "read_container" => "正在读取 Axure 容器",
        "read_source" => "正在载入 RP11 源数据",
        "rewrite_version_metadata" => "正在降级文件版本元数据",
        "scan_package" => "正在扫描页面与资源数据包",
        "rewrite_design_document" => "正在转换页面树与设计文档",
        "rewrite_document_settings" => "正在转换文档设置",
        "rewrite_page_and_widgets" => "正在转换页面与静态元件",
        "rebuild_package_index" => "正在重建 RP9 数据包索引",
        "write_rp9_file" => "正在写入 RP9 工程文件",
        "bridge_complete" => "核心数据转换完成",
        _ => "正在转换 Axure 数据",
    }
}

fn ensure_output_is_available(output: &Path) -> Result<(), String> {
    if !output.exists() {
        return Ok(());
    }

    let mut options = OpenOptions::new();
    options.read(true).write(true);
    #[cfg(windows)]
    options.share_mode(0);

    options.open(output).map(|_| ()).map_err(|error| {
        format!(
            "输出文件正在被其他程序占用，请先在 Axure 中关闭它，或换一个文件名：{}\n{}",
            output.display(),
            error
        )
    })
}

fn locate_bridge(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    let executable_name = "AxureDowngradeBridge.exe";
    let bundled = app
        .path()
        .resource_dir()
        .map_err(|error| format!("无法定位应用资源目录：{error}"))?
        .join("bin")
        .join(executable_name);
    if bundled.is_file() {
        return Ok(bundled);
    }

    let development = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("bin")
        .join(executable_name);
    if development.is_file() {
        return Ok(development);
    }
    Err(format!("找不到转换桥接器：{}", bundled.display()))
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![
            analyze_rp,
            compare_rp,
            validate_axure9_directory,
            downgrade_rp
        ])
        .run(tauri::generate_context!())
        .expect("failed to run Axure Downgrade");
}

#[cfg(test)]
mod tests {
    use super::{bridge_stage_label, parse_bridge_progress};

    #[test]
    fn parses_bridge_progress_protocol() {
        assert_eq!(
            parse_bridge_progress("PROGRESS\t59\trewrite_page_and_widgets"),
            Some((59, "rewrite_page_and_widgets"))
        );
        assert_eq!(parse_bridge_progress("System.Exception: failed"), None);
    }

    #[test]
    fn maps_known_and_unknown_bridge_stages() {
        assert_eq!(
            bridge_stage_label("rewrite_document_settings"),
            "正在转换文档设置"
        );
        assert_eq!(bridge_stage_label("future_stage"), "正在转换 Axure 数据");
    }

    #[test]
    fn main_window_can_open_and_save_files() {
        let capability = include_str!("../capabilities/default.json");
        assert!(capability.contains("\"dialog:allow-open\""));
        assert!(capability.contains("\"dialog:allow-save\""));
    }
}
