use crate::ir::{Bounds, Document, RichText, TextRun, Widget, WidgetKind};
use serde::{Deserialize, Serialize};
use std::fmt;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StaticizationOptions {
    /// Retain a visual rectangle for containers that have their own fill,
    /// border, image, shadow, opacity, or text.
    pub preserve_container_shells: bool,
    /// Hidden widgets are excluded from the static snapshot by default.
    pub include_hidden_widgets: bool,
    /// An inline frame cannot be embedded into a static RP 9 document without
    /// resolving its external content. Preserve a labeled rectangle instead.
    pub include_inline_frame_placeholders: bool,
}

impl Default for StaticizationOptions {
    fn default() -> Self {
        Self {
            preserve_container_shells: true,
            include_hidden_widgets: false,
            include_inline_frame_placeholders: true,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StaticizationResult {
    pub document: Document,
    pub report: StaticizationReport,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StaticizationReport {
    pub input_widget_count: u64,
    pub output_widget_count: u64,
    pub dropped_widget_count: u64,
    pub flattened_container_count: u64,
    pub substituted_widget_count: u64,
    pub issues: Vec<StaticizationIssue>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StaticizationIssue {
    pub page_id: String,
    pub widget_id: String,
    pub widget_name: Option<String>,
    pub kind: StaticizationIssueKind,
    pub message: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum StaticizationIssueKind {
    HiddenWidgetDropped,
    HotspotDropped,
    ContainerFlattened,
    ContainerShellPreserved,
    InlineFrameSubstituted,
    UnknownWidgetSubstituted,
    EmptyContainer,
    RotatedContainerApproximation,
}

#[derive(Debug, Clone, PartialEq)]
pub enum StaticizationError {
    NonFiniteBounds {
        page_id: String,
        widget_id: String,
    },
    NegativeSize {
        page_id: String,
        widget_id: String,
        width: f64,
        height: f64,
    },
    CoordinateOverflow {
        page_id: String,
        widget_id: String,
    },
}

impl fmt::Display for StaticizationError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::NonFiniteBounds { page_id, widget_id } => write!(
                formatter,
                "页面 {page_id} 的元件 {widget_id} 包含非有限坐标或尺寸"
            ),
            Self::NegativeSize {
                page_id,
                widget_id,
                width,
                height,
            } => write!(
                formatter,
                "页面 {page_id} 的元件 {widget_id} 尺寸为 {width} × {height}，不能为负数"
            ),
            Self::CoordinateOverflow { page_id, widget_id } => write!(
                formatter,
                "页面 {page_id} 的元件 {widget_id} 在转换为页面绝对坐标时溢出"
            ),
        }
    }
}

impl std::error::Error for StaticizationError {}

pub fn staticize_document(
    mut document: Document,
    options: StaticizationOptions,
) -> Result<StaticizationResult, StaticizationError> {
    let mut report = StaticizationReport {
        input_widget_count: document
            .pages
            .iter()
            .map(|page| count_widgets(&page.widgets))
            .sum(),
        output_widget_count: 0,
        dropped_widget_count: 0,
        flattened_container_count: 0,
        substituted_widget_count: 0,
        issues: Vec::new(),
    };

    for page in &mut document.pages {
        let page_id = page.id.clone();
        let input = std::mem::take(&mut page.widgets);
        let mut output = Vec::new();
        for widget in input {
            flatten_widget(
                &page_id,
                widget,
                (0.0, 0.0),
                options,
                &mut output,
                &mut report,
            )?;
        }
        page.widgets = output;
    }

    // Component instances are expected to have been expanded into their page
    // occurrence by the reader. Definitions are not needed by the static RP 9
    // writer after all page widgets have been flattened.
    document.components.clear();
    report.output_widget_count = document
        .pages
        .iter()
        .map(|page| page.widgets.len() as u64)
        .sum();

    Ok(StaticizationResult { document, report })
}

fn flatten_widget(
    page_id: &str,
    mut widget: Widget,
    parent_offset: (f64, f64),
    options: StaticizationOptions,
    output: &mut Vec<Widget>,
    report: &mut StaticizationReport,
) -> Result<(), StaticizationError> {
    validate_bounds(page_id, &widget)?;
    let absolute_x = parent_offset.0 + widget.bounds.x;
    let absolute_y = parent_offset.1 + widget.bounds.y;
    if !absolute_x.is_finite() || !absolute_y.is_finite() {
        return Err(StaticizationError::CoordinateOverflow {
            page_id: page_id.into(),
            widget_id: widget.id,
        });
    }
    widget.bounds.x = absolute_x;
    widget.bounds.y = absolute_y;

    if !widget.visible && !options.include_hidden_widgets {
        report.dropped_widget_count += count_widgets(std::slice::from_ref(&widget));
        issue(
            page_id,
            &widget,
            StaticizationIssueKind::HiddenWidgetDropped,
            "隐藏元件及其子树未进入静态快照。",
            report,
        );
        return Ok(());
    }

    let child_offset = (absolute_x, absolute_y);
    let children = std::mem::take(&mut widget.children);
    let is_container = is_container_kind(&widget.kind);
    if is_container {
        report.flattened_container_count += 1;
        issue(
            page_id,
            &widget,
            StaticizationIssueKind::ContainerFlattened,
            "容器层级已展开，子元件转换为页面绝对坐标。",
            report,
        );
        if widget.rotation_degrees != 0.0 && !children.is_empty() {
            issue(
                page_id,
                &widget,
                StaticizationIssueKind::RotatedContainerApproximation,
                "旋转容器的子元件目前只合成平移坐标；需要源格式提供变换原点后才能完全复原。",
                report,
            );
        }
    }

    match &widget.kind {
        WidgetKind::Hotspot => {
            report.dropped_widget_count += 1;
            issue(
                page_id,
                &widget,
                StaticizationIssueKind::HotspotDropped,
                "热点没有静态外观，已移除。",
                report,
            );
        }
        WidgetKind::InlineFrame => {
            if options.include_inline_frame_placeholders {
                substitute_inline_frame(&mut widget);
                report.substituted_widget_count += 1;
                issue(
                    page_id,
                    &widget,
                    StaticizationIssueKind::InlineFrameSubstituted,
                    "内嵌框架依赖外部内容，已替换为静态占位矩形。",
                    report,
                );
                output.push(widget);
            } else {
                report.dropped_widget_count += 1;
            }
        }
        WidgetKind::Unknown(source_kind) => {
            let source_kind = source_kind.clone();
            widget.kind = WidgetKind::Rectangle;
            report.substituted_widget_count += 1;
            issue(
                page_id,
                &widget,
                StaticizationIssueKind::UnknownWidgetSubstituted,
                &format!("未知元件类型 {source_kind} 已替换为矩形，并保留边界、样式和文本。"),
                report,
            );
            output.push(widget);
        }
        kind if is_container_kind(kind) => {
            if options.preserve_container_shells && has_visual_shell(&widget) {
                widget.kind = WidgetKind::Rectangle;
                report.substituted_widget_count += 1;
                issue(
                    page_id,
                    &widget,
                    StaticizationIssueKind::ContainerShellPreserved,
                    "容器自身的可见样式已保留为矩形。",
                    report,
                );
                output.push(widget);
            } else if children.is_empty() {
                report.dropped_widget_count += 1;
                issue(
                    page_id,
                    &widget,
                    StaticizationIssueKind::EmptyContainer,
                    "空容器没有可见外观，已移除。",
                    report,
                );
            }
        }
        _ => output.push(widget),
    }

    for child in children {
        flatten_widget(page_id, child, child_offset, options, output, report)?;
    }
    Ok(())
}

fn validate_bounds(page_id: &str, widget: &Widget) -> Result<(), StaticizationError> {
    let Bounds {
        x,
        y,
        width,
        height,
    } = widget.bounds;
    if !x.is_finite() || !y.is_finite() || !width.is_finite() || !height.is_finite() {
        return Err(StaticizationError::NonFiniteBounds {
            page_id: page_id.into(),
            widget_id: widget.id.clone(),
        });
    }
    if width < 0.0 || height < 0.0 {
        return Err(StaticizationError::NegativeSize {
            page_id: page_id.into(),
            widget_id: widget.id.clone(),
            width,
            height,
        });
    }
    Ok(())
}

fn is_container_kind(kind: &WidgetKind) -> bool {
    matches!(
        kind,
        WidgetKind::DynamicPanel
            | WidgetKind::Repeater
            | WidgetKind::Group
            | WidgetKind::ComponentInstance
            | WidgetKind::FlexContainer
    )
}

fn has_visual_shell(widget: &Widget) -> bool {
    widget.text.as_ref().is_some_and(|text| {
        !text.plain_text.is_empty() || text.runs.iter().any(|run| !run.text.is_empty())
    }) || widget.style.fill.is_some()
        || widget.style.border.is_some()
        || widget.style.opacity.is_some()
        || widget.style.corner_radius.is_some()
        || widget.style.shadow.is_some()
        || widget.style.image_resource_id.is_some()
}

fn substitute_inline_frame(widget: &mut Widget) {
    widget.kind = WidgetKind::Rectangle;
    if widget.text.is_none() {
        let label = widget
            .name
            .as_deref()
            .map(|name| format!("内嵌框架：{name}"))
            .unwrap_or_else(|| "内嵌框架".into());
        widget.text = Some(RichText {
            plain_text: label.clone(),
            runs: vec![TextRun {
                text: label,
                font_family: None,
                font_size: None,
                font_weight: None,
                italic: false,
                underline: false,
                color: None,
            }],
        });
    }
}

fn count_widgets(widgets: &[Widget]) -> u64 {
    widgets
        .iter()
        .map(|widget| 1 + count_widgets(&widget.children))
        .sum()
}

fn issue(
    page_id: &str,
    widget: &Widget,
    kind: StaticizationIssueKind,
    message: &str,
    report: &mut StaticizationReport,
) {
    report.issues.push(StaticizationIssue {
        page_id: page_id.into(),
        widget_id: widget.id.clone(),
        widget_name: widget.name.clone(),
        kind,
        message: message.into(),
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::ir::{Component, Page, Resource, Style};
    use std::collections::BTreeMap;

    fn widget(id: &str, kind: WidgetKind, bounds: Bounds, children: Vec<Widget>) -> Widget {
        Widget {
            id: id.into(),
            name: None,
            kind,
            bounds,
            rotation_degrees: 0.0,
            visible: true,
            locked: false,
            text: None,
            style: Style::default(),
            children,
            source_properties: BTreeMap::new(),
        }
    }

    fn document(widgets: Vec<Widget>) -> Document {
        Document {
            source_version: Some("11".into()),
            pages: vec![Page {
                id: "page-1".into(),
                name: "Page 1".into(),
                width: Some(1024.0),
                height: Some(768.0),
                widgets,
            }],
            components: vec![Component {
                id: "component-1".into(),
                name: "Unused after expansion".into(),
                widgets: Vec::new(),
            }],
            resources: Vec::<Resource>::new(),
            metadata: BTreeMap::new(),
        }
    }

    #[test]
    fn flattens_nested_coordinates_and_preserves_z_order() {
        let nested = widget(
            "group",
            WidgetKind::Group,
            Bounds {
                x: 100.0,
                y: 40.0,
                width: 300.0,
                height: 200.0,
            },
            vec![
                widget(
                    "first",
                    WidgetKind::Rectangle,
                    Bounds {
                        x: 10.0,
                        y: 20.0,
                        width: 30.0,
                        height: 40.0,
                    },
                    Vec::new(),
                ),
                widget(
                    "nested-group",
                    WidgetKind::Group,
                    Bounds {
                        x: 7.0,
                        y: 9.0,
                        width: 20.0,
                        height: 20.0,
                    },
                    vec![widget(
                        "second",
                        WidgetKind::Text,
                        Bounds {
                            x: 2.0,
                            y: 3.0,
                            width: 10.0,
                            height: 10.0,
                        },
                        Vec::new(),
                    )],
                ),
            ],
        );

        let result =
            staticize_document(document(vec![nested]), StaticizationOptions::default()).unwrap();
        let widgets = &result.document.pages[0].widgets;
        assert_eq!(
            widgets
                .iter()
                .map(|widget| widget.id.as_str())
                .collect::<Vec<_>>(),
            vec!["first", "second"]
        );
        assert_eq!(widgets[0].bounds.x, 110.0);
        assert_eq!(widgets[0].bounds.y, 60.0);
        assert_eq!(widgets[1].bounds.x, 109.0);
        assert_eq!(widgets[1].bounds.y, 52.0);
        assert!(result.document.components.is_empty());
    }

    #[test]
    fn removes_hotspots_and_hidden_subtrees() {
        let hidden_child = widget(
            "hidden-child",
            WidgetKind::Rectangle,
            Bounds {
                x: 1.0,
                y: 1.0,
                width: 10.0,
                height: 10.0,
            },
            Vec::new(),
        );
        let mut hidden_group = widget(
            "hidden-group",
            WidgetKind::Group,
            Bounds {
                x: 0.0,
                y: 0.0,
                width: 10.0,
                height: 10.0,
            },
            vec![hidden_child],
        );
        hidden_group.visible = false;
        let hotspot = widget(
            "hotspot",
            WidgetKind::Hotspot,
            Bounds {
                x: 0.0,
                y: 0.0,
                width: 20.0,
                height: 20.0,
            },
            Vec::new(),
        );

        let result = staticize_document(
            document(vec![hidden_group, hotspot]),
            StaticizationOptions::default(),
        )
        .unwrap();
        assert!(result.document.pages[0].widgets.is_empty());
        assert_eq!(result.report.input_widget_count, 3);
        assert_eq!(result.report.dropped_widget_count, 3);
    }

    #[test]
    fn preserves_visual_flex_shell_before_children() {
        let mut flex = widget(
            "flex",
            WidgetKind::FlexContainer,
            Bounds {
                x: 20.0,
                y: 30.0,
                width: 200.0,
                height: 100.0,
            },
            vec![widget(
                "child",
                WidgetKind::Ellipse,
                Bounds {
                    x: 5.0,
                    y: 6.0,
                    width: 20.0,
                    height: 20.0,
                },
                Vec::new(),
            )],
        );
        flex.style.opacity = Some(0.8);

        let result =
            staticize_document(document(vec![flex]), StaticizationOptions::default()).unwrap();
        let widgets = &result.document.pages[0].widgets;
        assert_eq!(widgets.len(), 2);
        assert_eq!(widgets[0].id, "flex");
        assert_eq!(widgets[0].kind, WidgetKind::Rectangle);
        assert_eq!(widgets[1].id, "child");
        assert_eq!(widgets[1].bounds.x, 25.0);
        assert_eq!(widgets[1].bounds.y, 36.0);
    }

    #[test]
    fn substitutes_unknown_widgets_without_losing_visual_fields() {
        let mut unknown = widget(
            "new-widget",
            WidgetKind::Unknown("Axure11Thing".into()),
            Bounds {
                x: 4.0,
                y: 8.0,
                width: 16.0,
                height: 32.0,
            },
            Vec::new(),
        );
        unknown.style.opacity = Some(0.5);

        let result =
            staticize_document(document(vec![unknown]), StaticizationOptions::default()).unwrap();
        let output = &result.document.pages[0].widgets[0];
        assert_eq!(output.kind, WidgetKind::Rectangle);
        assert_eq!(output.style.opacity, Some(0.5));
        assert_eq!(result.report.substituted_widget_count, 1);
    }

    #[test]
    fn rejects_invalid_geometry() {
        let invalid = widget(
            "invalid",
            WidgetKind::Rectangle,
            Bounds {
                x: f64::NAN,
                y: 0.0,
                width: 10.0,
                height: 10.0,
            },
            Vec::new(),
        );
        let error = staticize_document(document(vec![invalid]), StaticizationOptions::default())
            .unwrap_err();
        assert!(matches!(error, StaticizationError::NonFiniteBounds { .. }));
    }
}
