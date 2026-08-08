//! Conversion core for Axure RP downgrade research.
//!
//! The public API is intentionally split into probing, a version-neutral
//! intermediate representation, and writing. This prevents format-specific
//! assumptions from leaking into the desktop UI.

pub mod ir;
pub mod package;
pub mod probe;
pub mod research;
pub mod staticize;

pub use package::{
    RpPackage, RpPackageError, RpPackageKind, RpPackageReport, extract_rp_package,
    inspect_rp_packages,
};
pub use probe::{ContainerKind, FormatProbe, ProbeError, probe_file};
pub use research::{
    BlockStats, ChangedRange, EmbeddedSignature, PairReport, ResearchError, ResearchReport,
    StringEncoding, StringEvidence, compare_files, inspect_file, search_string_evidence,
};
pub use staticize::{
    StaticizationError, StaticizationIssue, StaticizationIssueKind, StaticizationOptions,
    StaticizationReport, StaticizationResult, staticize_document,
};
