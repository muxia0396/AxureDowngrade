use axure_core::{
    PairReport, ResearchReport, StaticizationOptions, compare_files, extract_rp_package,
    inspect_file, inspect_rp_packages, search_string_evidence, staticize_document,
};
use serde_json::{from_slice, to_string_pretty};
use std::{env, fs, path::Path, process::ExitCode};

fn main() -> ExitCode {
    match run(env::args().skip(1).collect()) {
        Ok(output) => {
            println!("{output}");
            ExitCode::SUCCESS
        }
        Err(error) => {
            eprintln!("错误：{error}");
            eprintln!();
            eprintln!("{}", usage());
            ExitCode::FAILURE
        }
    }
}

fn run(arguments: Vec<String>) -> Result<String, String> {
    match arguments.as_slice() {
        [command, path] if command == "inspect" => {
            let report = inspect_file(path).map_err(|error| error.to_string())?;
            to_string_pretty(&report).map_err(|error| error.to_string())
        }
        [command, left, right] if command == "compare" => {
            let report = compare_files(left, right).map_err(|error| error.to_string())?;
            to_string_pretty(&report).map_err(|error| error.to_string())
        }
        [command, path, flag] if command == "inspect" && flag == "--summary" => {
            let report = inspect_file(path).map_err(|error| error.to_string())?;
            Ok(inspect_summary(&report))
        }
        [command, left, right, flag] if command == "compare" && flag == "--summary" => {
            let report = compare_files(left, right).map_err(|error| error.to_string())?;
            Ok(compare_summary(&report))
        }
        [command, path] if command == "staticize" => {
            let bytes = fs::read(path).map_err(|error| format!("无法读取 {path}：{error}"))?;
            let document = from_slice(&bytes)
                .map_err(|error| format!("{path} 不是有效的 Axure IR：{error}"))?;
            let result = staticize_document(document, StaticizationOptions::default())
                .map_err(|error| error.to_string())?;
            to_string_pretty(&result).map_err(|error| error.to_string())
        }
        [command, path] if command == "inspect-packages" => {
            let report = inspect_rp_packages(path).map_err(|error| error.to_string())?;
            to_string_pretty(&report).map_err(|error| error.to_string())
        }
        [command, path, index, output] if command == "extract-package" => {
            let index = index
                .parse::<usize>()
                .map_err(|_| format!("包索引必须是非负整数，收到：{index}"))?;
            let decoded = extract_rp_package(path, index).map_err(|error| error.to_string())?;
            fs::write(output, &decoded).map_err(|error| format!("无法写入 {output}：{error}"))?;
            Ok(format!(
                "已提取包 {index}：{} 字节 → {output}",
                decoded.len()
            ))
        }
        [command, path, query] if command == "search-strings" => {
            search_strings_output(path, query, 100)
        }
        [command, path, query, flag, limit] if command == "search-strings" && flag == "--limit" => {
            let limit = limit
                .parse::<usize>()
                .map_err(|_| format!("--limit 必须是非负整数，收到：{limit}"))?;
            search_strings_output(path, query, limit)
        }
        _ => Err("参数不正确".into()),
    }
}

fn search_strings_output(path: &str, query: &str, limit: usize) -> Result<String, String> {
    let evidence = search_string_evidence(path, query, limit).map_err(|error| error.to_string())?;
    to_string_pretty(&evidence).map_err(|error| error.to_string())
}

fn inspect_summary(report: &ResearchReport) -> String {
    format!(
        "文件：{}\n大小：{} 字节\nSHA-256：{}\n容器：{:?}\n签名：{} 个\n字符串：{} 个\n数据块：{} 个",
        report.probe.path.display(),
        report.probe.file_size,
        report.sha256,
        report.probe.container,
        report.signatures.len(),
        report.strings.len(),
        report.blocks.len()
    )
}

fn compare_summary(report: &PairReport) -> String {
    format!(
        "Axure 9 候选：{}\nAxure 11 候选：{}\n大小变化：{:+} 字节\n共同前缀：{} 字节\n共同后缀：{} 字节\n对齐相似度：{:.1}%\n相同 4 KiB 块：{}/{}\n变化区间：{}{}",
        display_name(&report.left.probe.path),
        display_name(&report.right.probe.path),
        report.size_delta,
        report.common_prefix_bytes,
        report.common_suffix_bytes,
        report.aligned_similarity_per_mille as f64 / 10.0,
        report.equal_blocks,
        report.compared_blocks,
        report.changed_ranges.len(),
        if report.changed_ranges_truncated {
            "（已截断）"
        } else {
            ""
        }
    )
}

fn display_name(path: &Path) -> String {
    path.file_name()
        .and_then(|name| name.to_str())
        .unwrap_or_else(|| path.as_os_str().to_str().unwrap_or("<未知文件>"))
        .to_owned()
}

fn usage() -> &'static str {
    "用法：\n  axure-lab inspect <file.rp> [--summary]\n  axure-lab inspect-packages <file.rp>\n  axure-lab extract-package <file.rp> <index> <output>\n  axure-lab compare <axure9.rp> <axure11.rp> [--summary]\n  axure-lab search-strings <file> <query> [--limit N]\n  axure-lab staticize <document-ir.json>"
}
