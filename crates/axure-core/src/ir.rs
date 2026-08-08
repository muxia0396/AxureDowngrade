use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

/// Version-neutral representation used between the Axure 11 reader and the
/// Axure 9 writer. Only visual state belongs here; interaction state is
/// intentionally excluded from the first conversion milestone.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Document {
    pub source_version: Option<String>,
    pub pages: Vec<Page>,
    pub components: Vec<Component>,
    pub resources: Vec<Resource>,
    pub metadata: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Page {
    pub id: String,
    pub name: String,
    pub width: Option<f64>,
    pub height: Option<f64>,
    /// Top-level widgets use page-relative coordinates. Nested widget bounds
    /// are relative to their immediate parent. The staticization pass converts
    /// all retained widgets to page-relative coordinates before RP 9 writing.
    pub widgets: Vec<Widget>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Component {
    pub id: String,
    pub name: String,
    pub widgets: Vec<Widget>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Widget {
    pub id: String,
    pub name: Option<String>,
    pub kind: WidgetKind,
    /// Bounds relative to the immediate parent, or to the page for a top-level
    /// widget.
    pub bounds: Bounds,
    pub rotation_degrees: f64,
    pub visible: bool,
    pub locked: bool,
    pub text: Option<RichText>,
    pub style: Style,
    pub children: Vec<Widget>,
    pub source_properties: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum WidgetKind {
    Rectangle,
    Ellipse,
    Image,
    Text,
    Line,
    Hotspot,
    DynamicPanel,
    Repeater,
    InlineFrame,
    Group,
    ComponentInstance,
    /// Axure 11-only layout container. The downgrade pass must bake its
    /// computed child positions into absolute bounds before RP 9 writing.
    FlexContainer,
    Unknown(String),
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Bounds {
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RichText {
    pub plain_text: String,
    pub runs: Vec<TextRun>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TextRun {
    pub text: String,
    pub font_family: Option<String>,
    pub font_size: Option<f64>,
    pub font_weight: Option<u16>,
    pub italic: bool,
    pub underline: bool,
    pub color: Option<Color>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Style {
    pub fill: Option<Paint>,
    pub border: Option<Border>,
    pub opacity: Option<f64>,
    pub corner_radius: Option<[f64; 4]>,
    pub shadow: Option<Shadow>,
    pub horizontal_alignment: Option<HorizontalAlignment>,
    pub vertical_alignment: Option<VerticalAlignment>,
    pub image_resource_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", tag = "type", content = "value")]
pub enum Paint {
    Solid(Color),
    LinearGradient(Vec<GradientStop>),
    RadialGradient(Vec<GradientStop>),
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GradientStop {
    pub offset: f64,
    pub color: Color,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Color {
    pub red: u8,
    pub green: u8,
    pub blue: u8,
    pub alpha: u8,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Border {
    pub color: Color,
    pub width: f64,
    pub style: BorderStyle,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum BorderStyle {
    Solid,
    Dashed,
    Dotted,
    None,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Shadow {
    pub offset_x: f64,
    pub offset_y: f64,
    pub blur: f64,
    pub spread: f64,
    pub color: Color,
    pub inset: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum HorizontalAlignment {
    Left,
    Center,
    Right,
    Justify,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum VerticalAlignment {
    Top,
    Middle,
    Bottom,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Resource {
    pub id: String,
    pub media_type: Option<String>,
    pub original_name: Option<String>,
    pub bytes: Vec<u8>,
}
