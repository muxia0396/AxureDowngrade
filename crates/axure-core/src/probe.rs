use serde::{Deserialize, Serialize};
use std::{
    fmt,
    fs::File,
    io::{Read, Seek, SeekFrom},
    path::{Path, PathBuf},
};

const PROBE_BYTES: usize = 1024 * 1024;
const MAX_VERSION_HINTS: usize = 16;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FormatProbe {
    pub path: PathBuf,
    pub file_size: u64,
    pub container: ContainerKind,
    pub magic_hex: String,
    pub version_hints: Vec<String>,
    pub printable_ratio_per_mille: u16,
    pub warnings: Vec<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum ContainerKind {
    Zip,
    OleCompoundFile,
    SevenZip,
    Gzip,
    Xml,
    Json,
    UnknownBinary,
    Empty,
}

#[derive(Debug)]
pub enum ProbeError {
    Open {
        path: PathBuf,
        source: std::io::Error,
    },
    Read {
        path: PathBuf,
        source: std::io::Error,
    },
}

impl fmt::Display for ProbeError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Open { path, source } => {
                write!(formatter, "无法打开 {}：{source}", path.display())
            }
            Self::Read { path, source } => {
                write!(formatter, "无法读取 {}：{source}", path.display())
            }
        }
    }
}

impl std::error::Error for ProbeError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Open { source, .. } | Self::Read { source, .. } => Some(source),
        }
    }
}

pub fn probe_file(path: impl AsRef<Path>) -> Result<FormatProbe, ProbeError> {
    let path = path.as_ref().to_path_buf();
    let mut file = File::open(&path).map_err(|source| ProbeError::Open {
        path: path.clone(),
        source,
    })?;
    let file_size = file
        .seek(SeekFrom::End(0))
        .and_then(|size| {
            file.seek(SeekFrom::Start(0))?;
            Ok(size)
        })
        .map_err(|source| ProbeError::Read {
            path: path.clone(),
            source,
        })?;

    let mut sample = vec![0; usize::try_from(file_size.min(PROBE_BYTES as u64)).unwrap_or(0)];
    file.read_exact(&mut sample)
        .map_err(|source| ProbeError::Read {
            path: path.clone(),
            source,
        })?;

    let container = detect_container(&sample);
    let magic_hex = sample
        .iter()
        .take(16)
        .map(|byte| format!("{byte:02X}"))
        .collect::<Vec<_>>()
        .join(" ");
    let version_hints = find_version_hints(&sample);
    let printable_ratio_per_mille = printable_ratio(&sample);
    let mut warnings = Vec::new();

    if path
        .extension()
        .and_then(|extension| extension.to_str())
        .is_none_or(|extension| !extension.eq_ignore_ascii_case("rp"))
    {
        warnings.push("文件扩展名不是 .rp；探测结果仅供参考。".into());
    }
    if matches!(container, ContainerKind::UnknownBinary) {
        warnings.push("未识别出标准容器签名；该文件可能使用 Axure 私有头部、分块或加密。".into());
    }
    if (sample.len() as u64) < file_size {
        warnings.push("版本线索只扫描文件开头 1 MiB，不能代表完整结构。".into());
    }

    Ok(FormatProbe {
        path,
        file_size,
        container,
        magic_hex,
        version_hints,
        printable_ratio_per_mille,
        warnings,
    })
}

fn detect_container(bytes: &[u8]) -> ContainerKind {
    if bytes.is_empty() {
        return ContainerKind::Empty;
    }
    if bytes.starts_with(b"PK\x03\x04")
        || bytes.starts_with(b"PK\x05\x06")
        || bytes.starts_with(b"PK\x07\x08")
    {
        return ContainerKind::Zip;
    }
    if bytes.starts_with(&[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]) {
        return ContainerKind::OleCompoundFile;
    }
    if bytes.starts_with(&[0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]) {
        return ContainerKind::SevenZip;
    }
    if bytes.starts_with(&[0x1F, 0x8B]) {
        return ContainerKind::Gzip;
    }

    let first_non_whitespace = bytes
        .iter()
        .copied()
        .find(|byte| !byte.is_ascii_whitespace());
    match first_non_whitespace {
        Some(b'<') => ContainerKind::Xml,
        Some(b'{') | Some(b'[') => ContainerKind::Json,
        _ => ContainerKind::UnknownBinary,
    }
}

fn printable_ratio(bytes: &[u8]) -> u16 {
    if bytes.is_empty() {
        return 0;
    }
    let printable = bytes
        .iter()
        .filter(|byte| byte.is_ascii_graphic() || byte.is_ascii_whitespace())
        .count();
    ((printable * 1000) / bytes.len()) as u16
}

fn find_version_hints(bytes: &[u8]) -> Vec<String> {
    let ascii = String::from_utf8_lossy(bytes);
    let lower = ascii.to_ascii_lowercase();
    let needles = ["axure", "version", "documentversion", "appversion"];
    let mut hints = Vec::new();

    for needle in needles {
        let mut cursor = 0;
        while let Some(relative) = lower[cursor..].find(needle) {
            let start = cursor + relative;
            let end = (start + 96).min(ascii.len());
            let hint: String = String::from_utf8_lossy(&bytes[start..end])
                .chars()
                .map(|character| {
                    if character.is_ascii_graphic() || character == ' ' {
                        character
                    } else {
                        ' '
                    }
                })
                .collect::<String>()
                .split_whitespace()
                .collect::<Vec<_>>()
                .join(" ");

            if !hint.is_empty() && !hints.contains(&hint) {
                hints.push(hint);
            }
            if hints.len() == MAX_VERSION_HINTS {
                return hints;
            }
            cursor = start + needle.len();
        }
    }
    hints
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn detects_common_container_signatures() {
        assert_eq!(detect_container(b"PK\x03\x04rest"), ContainerKind::Zip);
        assert_eq!(
            detect_container(&[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            ContainerKind::OleCompoundFile
        );
        assert_eq!(detect_container(b" \n<?xml"), ContainerKind::Xml);
        assert_eq!(detect_container(b"\t{\"a\":1}"), ContainerKind::Json);
        assert_eq!(
            detect_container(&[0, 1, 2, 3]),
            ContainerKind::UnknownBinary
        );
    }

    #[test]
    fn extracts_and_deduplicates_version_hints() {
        let hints = find_version_hints(
            "中文前缀 Axure RP 11 documentVersion=11.0 ignored ignored Axure RP 11 documentVersion=11.0"
                .as_bytes(),
        );
        assert!(!hints.is_empty());
        assert!(hints.iter().any(|hint| hint.contains("Axure RP 11")));
    }

    #[test]
    fn computes_printable_ratio() {
        assert_eq!(printable_ratio(b"abcd"), 1000);
        assert_eq!(printable_ratio(&[0, 1, b'a', b'b']), 500);
        assert_eq!(printable_ratio(&[]), 0);
    }
}
