use crate::{FormatProbe, ProbeError, probe_file};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    fmt, fs,
    path::{Path, PathBuf},
};

const DEFAULT_BLOCK_SIZE: usize = 4096;
const MIN_STRING_LENGTH: usize = 6;
const MAX_STRINGS: usize = 512;
const MAX_SIGNATURES: usize = 1024;
const MAX_CHANGED_RANGES: usize = 4096;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ResearchReport {
    pub probe: FormatProbe,
    pub sha256: String,
    pub signatures: Vec<EmbeddedSignature>,
    pub strings: Vec<StringEvidence>,
    pub blocks: Vec<BlockStats>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PairReport {
    pub left: ResearchReport,
    pub right: ResearchReport,
    pub size_delta: i64,
    pub common_prefix_bytes: u64,
    pub common_suffix_bytes: u64,
    pub equal_aligned_bytes: u64,
    pub aligned_similarity_per_mille: u16,
    pub changed_ranges: Vec<ChangedRange>,
    pub changed_ranges_truncated: bool,
    pub equal_blocks: u64,
    pub compared_blocks: u64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EmbeddedSignature {
    pub offset: u64,
    pub kind: String,
    pub magic_hex: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StringEvidence {
    pub offset: u64,
    pub encoding: StringEncoding,
    pub value: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum StringEncoding {
    Ascii,
    Utf16Le,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BlockStats {
    pub offset: u64,
    pub length: u32,
    /// Shannon entropy in bits per byte. Values near 8 often indicate
    /// compressed or encrypted data; they do not prove either condition.
    pub entropy_bits_per_byte: f64,
    pub distinct_byte_count: u16,
    pub zero_ratio_per_mille: u16,
    pub sha256_prefix: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChangedRange {
    pub offset: u64,
    pub length: u64,
}

#[derive(Debug)]
pub enum ResearchError {
    Probe(ProbeError),
    Read {
        path: PathBuf,
        source: std::io::Error,
    },
    FileTooLarge {
        path: PathBuf,
        size: u64,
    },
}

impl fmt::Display for ResearchError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Probe(error) => error.fmt(formatter),
            Self::Read { path, source } => {
                write!(formatter, "无法读取 {}：{source}", path.display())
            }
            Self::FileTooLarge { path, size } => {
                write!(
                    formatter,
                    "{} 大小为 {size} 字节，超过当前进程可一次性分析的范围",
                    path.display()
                )
            }
        }
    }
}

impl std::error::Error for ResearchError {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Probe(error) => Some(error),
            Self::Read { source, .. } => Some(source),
            Self::FileTooLarge { .. } => None,
        }
    }
}

impl From<ProbeError> for ResearchError {
    fn from(value: ProbeError) -> Self {
        Self::Probe(value)
    }
}

pub fn inspect_file(path: impl AsRef<Path>) -> Result<ResearchReport, ResearchError> {
    let path = path.as_ref();
    let probe = probe_file(path)?;
    ensure_addressable(path, probe.file_size)?;
    let bytes = fs::read(path).map_err(|source| ResearchError::Read {
        path: path.to_path_buf(),
        source,
    })?;

    Ok(inspect_bytes(probe, &bytes))
}

pub fn compare_files(
    left_path: impl AsRef<Path>,
    right_path: impl AsRef<Path>,
) -> Result<PairReport, ResearchError> {
    let left_path = left_path.as_ref();
    let right_path = right_path.as_ref();
    let left_probe = probe_file(left_path)?;
    let right_probe = probe_file(right_path)?;
    ensure_addressable(left_path, left_probe.file_size)?;
    ensure_addressable(right_path, right_probe.file_size)?;

    let left_bytes = fs::read(left_path).map_err(|source| ResearchError::Read {
        path: left_path.to_path_buf(),
        source,
    })?;
    let right_bytes = fs::read(right_path).map_err(|source| ResearchError::Read {
        path: right_path.to_path_buf(),
        source,
    })?;

    Ok(compare_bytes(
        inspect_bytes(left_probe, &left_bytes),
        inspect_bytes(right_probe, &right_bytes),
        &left_bytes,
        &right_bytes,
    ))
}

pub fn search_string_evidence(
    path: impl AsRef<Path>,
    query: &str,
    limit: usize,
) -> Result<Vec<StringEvidence>, ResearchError> {
    let path = path.as_ref();
    let metadata = fs::metadata(path).map_err(|source| ResearchError::Read {
        path: path.to_path_buf(),
        source,
    })?;
    ensure_addressable(path, metadata.len())?;
    let bytes = fs::read(path).map_err(|source| ResearchError::Read {
        path: path.to_path_buf(),
        source,
    })?;
    Ok(search_strings(&bytes, query, limit))
}

fn ensure_addressable(path: &Path, size: u64) -> Result<(), ResearchError> {
    if usize::try_from(size).is_err() {
        return Err(ResearchError::FileTooLarge {
            path: path.to_path_buf(),
            size,
        });
    }
    Ok(())
}

fn inspect_bytes(probe: FormatProbe, bytes: &[u8]) -> ResearchReport {
    ResearchReport {
        probe,
        sha256: hex::encode(Sha256::digest(bytes)),
        signatures: scan_signatures(bytes),
        strings: extract_strings(bytes),
        blocks: block_stats(bytes, DEFAULT_BLOCK_SIZE),
    }
}

fn compare_bytes(
    left: ResearchReport,
    right: ResearchReport,
    left_bytes: &[u8],
    right_bytes: &[u8],
) -> PairReport {
    let aligned_length = left_bytes.len().min(right_bytes.len());
    let common_prefix = left_bytes
        .iter()
        .zip(right_bytes)
        .take_while(|(left, right)| left == right)
        .count();

    let suffix_limit = aligned_length.saturating_sub(common_prefix);
    let common_suffix = left_bytes
        .iter()
        .rev()
        .zip(right_bytes.iter().rev())
        .take(suffix_limit)
        .take_while(|(left, right)| left == right)
        .count();

    let equal_aligned_bytes = left_bytes
        .iter()
        .zip(right_bytes)
        .filter(|(left, right)| left == right)
        .count();
    let aligned_similarity_per_mille = equal_aligned_bytes
        .saturating_mul(1000)
        .checked_div(aligned_length)
        .map(|value| value as u16)
        .unwrap_or_else(|| u16::from(left_bytes.is_empty() && right_bytes.is_empty()) * 1000);

    let (changed_ranges, changed_ranges_truncated) =
        changed_ranges(left_bytes, right_bytes, MAX_CHANGED_RANGES);
    let compared_blocks = left.blocks.len().max(right.blocks.len());
    let equal_blocks = left
        .blocks
        .iter()
        .zip(&right.blocks)
        .filter(|(left, right)| {
            left.length == right.length && left.sha256_prefix == right.sha256_prefix
        })
        .count();

    PairReport {
        size_delta: right_bytes.len() as i64 - left_bytes.len() as i64,
        common_prefix_bytes: common_prefix as u64,
        common_suffix_bytes: common_suffix as u64,
        equal_aligned_bytes: equal_aligned_bytes as u64,
        aligned_similarity_per_mille,
        changed_ranges,
        changed_ranges_truncated,
        equal_blocks: equal_blocks as u64,
        compared_blocks: compared_blocks as u64,
        left,
        right,
    }
}

fn scan_signatures(bytes: &[u8]) -> Vec<EmbeddedSignature> {
    const SIGNATURES: &[(&str, &[u8])] = &[
        ("zipLocalFile", b"PK\x03\x04"),
        ("zipCentralDirectory", b"PK\x01\x02"),
        ("zipEndOfCentralDirectory", b"PK\x05\x06"),
        (
            "oleCompoundFile",
            &[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1],
        ),
        ("sevenZip", &[0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]),
        ("gzip", &[0x1F, 0x8B, 0x08]),
        ("png", &[0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A]),
        ("jpeg", &[0xFF, 0xD8, 0xFF]),
        ("gif87a", b"GIF87a"),
        ("gif89a", b"GIF89a"),
        ("pdf", b"%PDF-"),
        ("xmlDeclaration", b"<?xml"),
        ("sqlite3", b"SQLite format 3\0"),
    ];

    let mut result = Vec::new();
    for offset in 0..bytes.len() {
        for (kind, signature) in SIGNATURES {
            if bytes[offset..].starts_with(signature) {
                result.push(EmbeddedSignature {
                    offset: offset as u64,
                    kind: (*kind).into(),
                    magic_hex: hex::encode_upper(signature),
                });
                if result.len() == MAX_SIGNATURES {
                    return result;
                }
            }
        }
    }
    result.sort_by_key(|signature| signature.offset);
    result
}

fn extract_strings(bytes: &[u8]) -> Vec<StringEvidence> {
    let mut strings = extract_ascii_strings(bytes);
    strings.extend(extract_utf16le_strings(bytes));
    strings.sort_by_key(|value| value.offset);
    strings.truncate(MAX_STRINGS);
    strings
}

fn search_strings(bytes: &[u8], query: &str, limit: usize) -> Vec<StringEvidence> {
    if query.is_empty() || limit == 0 {
        return Vec::new();
    }

    let query = query.to_lowercase();
    let mut strings = extract_ascii_strings(bytes);
    strings.extend(extract_utf16le_strings(bytes));
    strings.retain(|evidence| evidence.value.to_lowercase().contains(&query));
    // Misaligned UTF-16 decoding can produce a long printable run that happens
    // to contain a real symbol near its tail. Prefer the shortest enclosing
    // string so exact symbols rank ahead of those false-positive supersets.
    strings.sort_by_key(|value| (value.value.chars().count(), value.offset));
    strings.truncate(limit);
    strings
}

fn extract_ascii_strings(bytes: &[u8]) -> Vec<StringEvidence> {
    let mut result = Vec::new();
    let mut start = None;

    for (index, byte) in bytes.iter().copied().enumerate() {
        if is_research_text_byte(byte) {
            start.get_or_insert(index);
        } else if let Some(start_index) = start.take() {
            push_ascii_string(bytes, start_index, index, &mut result);
        }
    }
    if let Some(start_index) = start {
        push_ascii_string(bytes, start_index, bytes.len(), &mut result);
    }
    result
}

fn push_ascii_string(bytes: &[u8], start: usize, end: usize, result: &mut Vec<StringEvidence>) {
    if end.saturating_sub(start) < MIN_STRING_LENGTH {
        return;
    }
    result.push(StringEvidence {
        offset: start as u64,
        encoding: StringEncoding::Ascii,
        value: String::from_utf8_lossy(&bytes[start..end]).into_owned(),
    });
}

fn extract_utf16le_strings(bytes: &[u8]) -> Vec<StringEvidence> {
    let mut result = Vec::new();
    for parity in 0..=1 {
        let mut cursor = parity;
        let mut start = None;
        let mut units = Vec::new();

        while cursor + 1 < bytes.len() {
            let unit = u16::from_le_bytes([bytes[cursor], bytes[cursor + 1]]);
            let boundary_before_non_ascii = start.is_some()
                && cursor > 0
                && bytes[cursor - 1] == 0
                && units.last().is_some_and(|previous| *previous <= 0xFF)
                && unit > 0xFF;

            if boundary_before_non_ascii && let Some(start_index) = start.take() {
                push_utf16_string(start_index, &mut units, &mut result);
            }

            let printable = char::from_u32(unit as u32).is_some_and(is_research_text_char);
            if printable {
                start.get_or_insert(cursor);
                units.push(unit);
            } else if let Some(start_index) = start.take() {
                push_utf16_string(start_index, &mut units, &mut result);
            }
            cursor += 2;
        }
        if let Some(start_index) = start {
            push_utf16_string(start_index, &mut units, &mut result);
        }
    }
    result
}

fn push_utf16_string(start: usize, units: &mut Vec<u16>, result: &mut Vec<StringEvidence>) {
    if units.len() >= MIN_STRING_LENGTH {
        result.push(StringEvidence {
            offset: start as u64,
            encoding: StringEncoding::Utf16Le,
            value: String::from_utf16_lossy(units),
        });
    }
    units.clear();
}

fn is_research_text_byte(byte: u8) -> bool {
    byte.is_ascii_graphic() || byte == b' ' || byte == b'\t'
}

fn is_research_text_char(character: char) -> bool {
    !character.is_control() && !character.is_whitespace() || character == ' '
}

fn block_stats(bytes: &[u8], block_size: usize) -> Vec<BlockStats> {
    bytes
        .chunks(block_size)
        .enumerate()
        .map(|(index, block)| {
            let mut histogram = [0usize; 256];
            for byte in block {
                histogram[*byte as usize] += 1;
            }
            let entropy = histogram
                .iter()
                .copied()
                .filter(|count| *count > 0)
                .map(|count| {
                    let probability = count as f64 / block.len() as f64;
                    -probability * probability.log2()
                })
                .sum();
            let zeroes = histogram[0];
            let digest = hex::encode(Sha256::digest(block));

            BlockStats {
                offset: (index * block_size) as u64,
                length: block.len() as u32,
                entropy_bits_per_byte: entropy,
                distinct_byte_count: histogram.iter().filter(|count| **count > 0).count() as u16,
                zero_ratio_per_mille: if block.is_empty() {
                    0
                } else {
                    ((zeroes * 1000) / block.len()) as u16
                },
                sha256_prefix: digest[..16].into(),
            }
        })
        .collect()
}

fn changed_ranges(left: &[u8], right: &[u8], limit: usize) -> (Vec<ChangedRange>, bool) {
    let max_length = left.len().max(right.len());
    let mut result = Vec::new();
    let mut start = None;

    for index in 0..max_length {
        let differs = left.get(index) != right.get(index);
        match (differs, start) {
            (true, None) => start = Some(index),
            (false, Some(start_index)) => {
                result.push(ChangedRange {
                    offset: start_index as u64,
                    length: (index - start_index) as u64,
                });
                start = None;
                if result.len() == limit {
                    return (result, index < max_length);
                }
            }
            _ => {}
        }
    }
    if let Some(start_index) = start {
        result.push(ChangedRange {
            offset: start_index as u64,
            length: (max_length - start_index) as u64,
        });
    }
    let truncated = result.len() > limit;
    result.truncate(limit);
    (result, truncated)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn probe_for(size: usize) -> FormatProbe {
        FormatProbe {
            path: PathBuf::from("fixture.rp"),
            file_size: size as u64,
            container: crate::ContainerKind::UnknownBinary,
            magic_hex: String::new(),
            version_hints: Vec::new(),
            printable_ratio_per_mille: 0,
            warnings: Vec::new(),
        }
    }

    #[test]
    fn finds_embedded_resource_signatures() {
        let mut bytes = vec![0xAA; 13];
        bytes.extend_from_slice(&[0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        let signatures = scan_signatures(&bytes);
        assert_eq!(signatures.len(), 1);
        assert_eq!(signatures[0].offset, 13);
        assert_eq!(signatures[0].kind, "png");
    }

    #[test]
    fn extracts_ascii_and_utf16_strings_with_offsets() {
        let mut bytes = b"\0Axure RP 11\0".to_vec();
        let utf16_offset = bytes.len();
        for unit in "页面名称测试".encode_utf16() {
            bytes.extend_from_slice(&unit.to_le_bytes());
        }
        bytes.extend_from_slice(&[0, 0]);

        let strings = extract_strings(&bytes);
        assert!(strings.iter().any(|value| {
            value.offset == 1
                && value.encoding == StringEncoding::Ascii
                && value.value == "Axure RP 11"
        }));
        assert!(
            strings.iter().any(|value| {
                value.offset == utf16_offset as u64
                    && value.encoding == StringEncoding::Utf16Le
                    && value.value == "页面名称测试"
            }),
            "{strings:#?}"
        );
    }

    #[test]
    fn searches_ascii_and_utf16_strings_before_applying_limit() {
        let mut ascii_bytes = Vec::new();
        for index in 0..600 {
            ascii_bytes.extend_from_slice(format!("filler-{index:04}\0").as_bytes());
        }
        ascii_bytes.extend_from_slice(b"ObjectPersistanceContext\0");

        let mut utf16_bytes = Vec::new();
        for unit in "RPFormatConverter".encode_utf16() {
            utf16_bytes.extend_from_slice(&unit.to_le_bytes());
        }
        utf16_bytes.extend_from_slice(&[0, 0]);

        let ascii = search_strings(&ascii_bytes, "persistance", 10);
        assert_eq!(ascii.len(), 1);
        assert_eq!(ascii[0].value, "ObjectPersistanceContext");

        let utf16 = search_strings(&utf16_bytes, "formatconverter", 10);
        assert_eq!(utf16.len(), 1);
        assert_eq!(utf16[0].encoding, StringEncoding::Utf16Le);
        assert_eq!(utf16[0].value, "RPFormatConverter");

        assert!(search_strings(&ascii_bytes, "context", 0).is_empty());
        assert!(search_strings(&ascii_bytes, "", 10).is_empty());
    }

    #[test]
    fn reports_entropy_extremes() {
        let zero_block = vec![0; 4096];
        let stats = block_stats(&zero_block, 4096);
        assert_eq!(stats[0].entropy_bits_per_byte, 0.0);
        assert_eq!(stats[0].zero_ratio_per_mille, 1000);

        let evenly_distributed: Vec<u8> = (0u8..=255).cycle().take(4096).collect();
        let stats = block_stats(&evenly_distributed, 4096);
        assert!((stats[0].entropy_bits_per_byte - 8.0).abs() < 0.000_001);
    }

    #[test]
    fn compares_prefix_suffix_and_changed_ranges() {
        let left_bytes = b"same-left-MIDDLE-same-right";
        let right_bytes = b"same-left-CHANGED-same-right";
        let left = inspect_bytes(probe_for(left_bytes.len()), left_bytes);
        let right = inspect_bytes(probe_for(right_bytes.len()), right_bytes);
        let report = compare_bytes(left, right, left_bytes, right_bytes);

        assert_eq!(report.common_prefix_bytes, 10);
        assert_eq!(report.common_suffix_bytes, 11);
        assert!(!report.changed_ranges.is_empty());
        assert!(report.aligned_similarity_per_mille < 1000);
    }

    #[test]
    fn marks_identical_empty_files_as_fully_similar() {
        let left = inspect_bytes(probe_for(0), b"");
        let right = inspect_bytes(probe_for(0), b"");
        let report = compare_bytes(left, right, b"", b"");
        assert_eq!(report.aligned_similarity_per_mille, 1000);
    }
}
