use flate2::bufread::GzDecoder;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    fmt, fs,
    io::Read,
    path::{Path, PathBuf},
};

const RP_MAGIC: [u8; 2] = [0xAC, 0xEF];
const GZIP_MAGIC: [u8; 3] = [0x1F, 0x8B, 0x08];

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RpPackageReport {
    pub path: PathBuf,
    pub format_major: u16,
    pub header_length_hint: u32,
    pub packages: Vec<RpPackage>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RpPackage {
    pub index: usize,
    pub offset: u64,
    pub compressed_length: u64,
    pub decoded_length: u64,
    pub decoded_sha256: String,
    pub kind: RpPackageKind,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum RpPackageKind {
    HtmlPrototypeConfiguration,
    SpecificationConfiguration,
    CsvReportConfiguration,
    PrintConfiguration,
    Page,
    ObjectPackage,
    DesignDocument,
    BreakingChanges,
    DocumentSettings,
    Unknown,
}

#[derive(Debug)]
pub enum RpPackageError {
    Read {
        path: PathBuf,
        source: std::io::Error,
    },
    InvalidMagic {
        path: PathBuf,
    },
    TruncatedHeader {
        path: PathBuf,
    },
    NoPackages {
        path: PathBuf,
    },
    PackageIndexOutOfRange {
        path: PathBuf,
        index: usize,
        count: usize,
    },
}

impl fmt::Display for RpPackageError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Read { path, source } => {
                write!(formatter, "无法读取 {}：{source}", path.display())
            }
            Self::InvalidMagic { path } => {
                write!(formatter, "{} 不是受支持的 Axure RP 容器", path.display())
            }
            Self::TruncatedHeader { path } => {
                write!(formatter, "{} 的文件头不完整", path.display())
            }
            Self::NoPackages { path } => {
                write!(formatter, "{} 中没有找到有效的 GZip 包", path.display())
            }
            Self::PackageIndexOutOfRange { path, index, count } => write!(
                formatter,
                "{} 只有 {count} 个包，无法提取索引 {index}",
                path.display()
            ),
        }
    }
}

impl std::error::Error for RpPackageError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Read { source, .. } => Some(source),
            _ => None,
        }
    }
}

pub fn inspect_rp_packages(path: impl AsRef<Path>) -> Result<RpPackageReport, RpPackageError> {
    let path = path.as_ref();
    let bytes = fs::read(path).map_err(|source| RpPackageError::Read {
        path: path.to_path_buf(),
        source,
    })?;
    inspect_rp_package_bytes(path, &bytes)
}

pub fn extract_rp_package(path: impl AsRef<Path>, index: usize) -> Result<Vec<u8>, RpPackageError> {
    let path = path.as_ref();
    let bytes = fs::read(path).map_err(|source| RpPackageError::Read {
        path: path.to_path_buf(),
        source,
    })?;
    extract_rp_package_bytes(path, &bytes, index)
}

fn extract_rp_package_bytes(
    path: &Path,
    bytes: &[u8],
    index: usize,
) -> Result<Vec<u8>, RpPackageError> {
    if bytes.len() < 8 {
        return Err(RpPackageError::TruncatedHeader {
            path: path.to_path_buf(),
        });
    }
    if !bytes.starts_with(&RP_MAGIC) {
        return Err(RpPackageError::InvalidMagic {
            path: path.to_path_buf(),
        });
    }

    let mut cursor = 8;
    let mut package_index = 0;
    while let Some(relative_offset) = find_signature(&bytes[cursor..], &GZIP_MAGIC) {
        let offset = cursor + relative_offset;
        let source = &bytes[offset..];
        let mut decoder = GzDecoder::new(source);
        let mut decoded = Vec::new();

        if decoder.read_to_end(&mut decoded).is_ok() && !decoded.is_empty() {
            let compressed_length = source.len().saturating_sub(decoder.get_ref().len());
            if compressed_length > 0 {
                if package_index == index {
                    return Ok(decoded);
                }
                package_index += 1;
                cursor = offset + compressed_length;
                continue;
            }
        }
        cursor = offset + GZIP_MAGIC.len();
    }

    Err(RpPackageError::PackageIndexOutOfRange {
        path: path.to_path_buf(),
        index,
        count: package_index,
    })
}

fn inspect_rp_package_bytes(path: &Path, bytes: &[u8]) -> Result<RpPackageReport, RpPackageError> {
    if bytes.len() < 8 {
        return Err(RpPackageError::TruncatedHeader {
            path: path.to_path_buf(),
        });
    }
    if !bytes.starts_with(&RP_MAGIC) {
        return Err(RpPackageError::InvalidMagic {
            path: path.to_path_buf(),
        });
    }

    let format_major = u16::from_le_bytes([bytes[2], bytes[3]]);
    let header_length_hint = u32::from_le_bytes([bytes[4], bytes[5], bytes[6], bytes[7]]);
    let mut packages = Vec::new();
    let mut cursor = 8;

    while let Some(relative_offset) = find_signature(&bytes[cursor..], &GZIP_MAGIC) {
        let offset = cursor + relative_offset;
        let source = &bytes[offset..];
        let mut decoder = GzDecoder::new(source);
        let mut decoded = Vec::new();

        if decoder.read_to_end(&mut decoded).is_ok() && !decoded.is_empty() {
            let compressed_length = source.len().saturating_sub(decoder.get_ref().len());
            if compressed_length > 0 {
                packages.push(RpPackage {
                    index: packages.len(),
                    offset: offset as u64,
                    compressed_length: compressed_length as u64,
                    decoded_length: decoded.len() as u64,
                    decoded_sha256: hex::encode(Sha256::digest(&decoded)),
                    kind: classify_package(&decoded),
                });
                cursor = offset + compressed_length;
                continue;
            }
        }

        cursor = offset + GZIP_MAGIC.len();
    }

    if packages.is_empty() {
        return Err(RpPackageError::NoPackages {
            path: path.to_path_buf(),
        });
    }

    Ok(RpPackageReport {
        path: path.to_path_buf(),
        format_major,
        header_length_hint,
        packages,
    })
}

fn find_signature(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    haystack
        .windows(needle.len())
        .position(|window| window == needle)
}

fn classify_package(decoded: &[u8]) -> RpPackageKind {
    const MARKERS: &[(&[u8], RpPackageKind)] = &[
        (b"Axure:DesignDocument", RpPackageKind::DesignDocument),
        (b"Axure:DocumentSettings", RpPackageKind::DocumentSettings),
        (b"<BreakingChanges>", RpPackageKind::BreakingChanges),
        (
            b"Axure:HtmlPrototypeGeneratorConfiguration",
            RpPackageKind::HtmlPrototypeConfiguration,
        ),
        (
            b"Axure:Word2007SpecificationGeneratorConfiguration",
            RpPackageKind::SpecificationConfiguration,
        ),
        (
            b"Axure:CsvAnnotationReportGeneratorConfiguration",
            RpPackageKind::CsvReportConfiguration,
        ),
        (b"Axure:PrintConfig", RpPackageKind::PrintConfiguration),
    ];

    if let Some(kind) = MARKERS
        .iter()
        .find_map(|(marker, kind)| {
            decoded
                .windows(marker.len())
                .any(|window| window == *marker)
                .then_some(*kind)
        })
    {
        return kind;
    }
    if contains_ascii_token(decoded, b"Axure:Page") {
        return RpPackageKind::Page;
    }
    if decoded
        .windows(b"Axure:Page".len())
        .any(|window| window == b"Axure:Page")
    {
        return RpPackageKind::ObjectPackage;
    }
    RpPackageKind::Unknown
}

fn contains_ascii_token(haystack: &[u8], token: &[u8]) -> bool {
    haystack
        .windows(token.len())
        .enumerate()
        .any(|(offset, window)| {
            if window != token {
                return false;
            }
            haystack.get(offset + token.len()).is_none_or(|next| {
                !next.is_ascii_alphanumeric() && !matches!(*next, b'_' | b':')
            })
        })
}

#[cfg(test)]
mod tests {
    use super::*;
    use flate2::{Compression, write::GzEncoder};
    use std::io::Write;

    fn gzip(value: &[u8]) -> Vec<u8> {
        let mut encoder = GzEncoder::new(Vec::new(), Compression::default());
        encoder.write_all(value).unwrap();
        encoder.finish().unwrap()
    }

    #[test]
    fn reads_version_and_classifies_embedded_packages() {
        let page = gzip(b"prefix Axure:Page suffix");
        let settings = gzip(b"prefix Axure:DocumentSettings suffix");
        let mut bytes = vec![0xAC, 0xEF, 0x0B, 0x00, 0x10, 0x00, 0x00, 0x00];
        bytes.extend_from_slice(b"header");
        bytes.extend_from_slice(&page);
        bytes.extend_from_slice(&[0, 0, 0, 0]);
        bytes.extend_from_slice(&settings);

        let report = inspect_rp_package_bytes(Path::new("fixture.rp"), &bytes).unwrap();
        assert_eq!(report.format_major, 11);
        assert_eq!(report.header_length_hint, 16);
        assert_eq!(report.packages.len(), 2);
        assert_eq!(report.packages[0].kind, RpPackageKind::Page);
        assert_eq!(report.packages[1].kind, RpPackageKind::DocumentSettings);
    }

    #[test]
    fn rejects_non_rp_data() {
        let error = inspect_rp_package_bytes(Path::new("fixture.bin"), b"not axure").unwrap_err();
        assert!(matches!(error, RpPackageError::InvalidMagic { .. }));
    }

    #[test]
    fn extracts_a_package_by_index() {
        let first = gzip(b"Axure:Page first");
        let second = gzip(b"Axure:DocumentSettings second");
        let mut bytes = vec![0xAC, 0xEF, 0x09, 0x00, 0, 0, 0, 0];
        bytes.extend_from_slice(&first);
        bytes.extend_from_slice(&[0, 0, 0, 0]);
        bytes.extend_from_slice(&second);

        let decoded = extract_rp_package_bytes(Path::new("fixture.rp"), &bytes, 1).unwrap();
        assert_eq!(decoded, b"Axure:DocumentSettings second");

        let error = extract_rp_package_bytes(Path::new("fixture.rp"), &bytes, 2).unwrap_err();
        assert!(matches!(
            error,
            RpPackageError::PackageIndexOutOfRange { count: 2, .. }
        ));
    }

    #[test]
    fn distinguishes_pages_from_page_style_object_packages() {
        assert_eq!(
            classify_package(b"prefix Axure:Page suffix"),
            RpPackageKind::Page
        );
        assert_eq!(
            classify_package(b"prefix Axure:PageStyle suffix"),
            RpPackageKind::ObjectPackage
        );
    }
}
