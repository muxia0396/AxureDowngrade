# Axure RP 9/11 格式取证记录

本文只记录样本或安装文件中可重复观察到的事实。尚未验证的推断会明确标注。

## 对照样本

样本位于：

- `fixtures/axure9/00-empty.rp`
- `fixtures/axure11/00-empty.rp`
- `fixtures/axure9/01-rectangle.rp`
- `fixtures/axure11/01-rectangle.rp`

矩形样本中的根对象 GUID 在 RP9 与 RP11 文件头中相同，因此这组文件适合做同源版本对照。

## 容器头

四个样本都以 `AC EF` 开头，随后是小端主版本号：

- RP9：`AC EF 09 00`
- RP11：`AC EF 0B 00`

偏移 `4..8` 是一个小端长度提示。矩形样本分别为：

- RP9：356
- RP11：357

文件不是标准 ZIP。头部可看到包索引、对象 GUID、版本字符串和各包偏移等结构化内容，但其中混有二进制标记，不能直接当作普通 JSON 解析。

## GZip 包

矩形样本均包含 8 个可独立解压的 GZip 包：

| 序号 | 类型 | RP9 解压长度 | RP11 解压长度 | 结论 |
| ---: | --- | ---: | ---: | --- |
| 0 | HTML 原型生成配置 | 1,187 | 1,187 | 解压内容 SHA-256 完全相同 |
| 1 | Word 规格说明配置 | 41,880 | 41,880 | 解压内容 SHA-256 完全相同 |
| 2 | CSV 注释报告配置 | 1,160 | 1,160 | 解压内容 SHA-256 完全相同 |
| 3 | 打印配置 | 414 | 414 | 解压内容 SHA-256 完全相同 |
| 4 | Page | 11,283 | 11,903 | 需要转换 |
| 5 | DesignDocument | 30,350 | 35,228 | 需要转换 |
| 6 | BreakingChanges | 757 | 761 | 需要转换或重建 |
| 7 | DocumentSettings | 5,671 | 6,516 | 需要转换 |

这说明降级器无需重写所有内容。第一阶段可原样复用前四包，把研究和转换范围集中到 Page、DesignDocument、BreakingChanges、DocumentSettings。

## BreakingChanges

RP11 的 BreakingChanges 包解压后是 XML，其中明确包含：

```text
This file was created with Axure RP 11.0, and cannot be opened in earlier versions.
```

这证明“不兼容提示”不只来自文件头的主版本字节；包内也保存了版本门槛信息。仅把 `0B` 改成 `09` 不能视为完整降级。

## 安装程序中的持久化线索

在本机 Axure 安装文件的可打印字符串中观察到：

- `Axure.Platform.ObjectPersistanceContext`
- `PackagePersistenceContext`
- `LegacyPackageContextBegin`
- `LegacyPackageContextEnd`
- `RootObjectId`
- `PackageReference`
- `FileStart`
- `FileEnd`
- `LZ4Stream`
- `K4os.Compression.LZ4.Legacy`
- `GZipInputStream`
- `GZipOutputStream`
- `RPFormatConverter`

结合样本中的实际 GZip 包，可以确认 GZip 是正文包的一层封装。LZ4 很可能用于文件头/包索引或其他内部块，但尚未完成解码验证。

## RP9 对象持久化桥接

Axure RP 9 的 `AxureRP9.exe` 中存在内部对象流入口：

```text
Pacj.jac4.Load(Object data, Double legacyDpi, Boolean lazy)
Pacj.jac4.Save(Object packageContext, Object data)
```

在 32 位 .NET Framework 桥接进程中：

- `Load` 可以直接读取 RP9 和 RP11 的 Page、DesignDocument、DocumentSettings 解压包。
- RP11 矩形 Page 可提取为 `Axure:DiagramObject:VectorShape`。
- 位置与尺寸位于关联的 `Axure:Style/PropList` 中。
- 对照矩形的实际值被读取为 `X=13`、`Y=17`、`Width=101`、`Height=37`。
- 把包上下文版本设为 `9.0.1.0` 后，RP9 自带 `Save` 可以重新写出对象流。
- 重写后的 Page 可再次由 RP9 `Load` 读取，四个几何值不变。

写入器默认依赖 Axure GUI 进程的临时流服务。将其内部
`UseMemoryStream` 分支置为 `true` 后，可以在隔离的桥接进程中使用。

## 外层索引

偏移 `8` 开始、长度由文件头 `4..8` 指定的区块是 LZ4 Legacy 流。
解压结果是 UTF-8 JSON，记录每个部件相对于数据区基址的偏移。

矩形 RP11 样本的索引包括：

```json
{
  "parts": {
    "DesignDocument": 35754,
    "11.0.0.4137.version": 39963,
    "DocumentSettings": 40303
  }
}
```

页面节点还分别记录 `thumbnail` 与 `package`。部件记录采用：

```text
uint32 gzipLength
byte[gzipLength] payload
uint32 zeroPadding  // 最后一个部件除外
```

当前桥接器会重新压缩被修改的包、重算所有相对偏移、重新序列化 JSON，
再用 Axure 9 随附的 K4os LZ4 Legacy 实现写回索引。

## 当前转换验证

`candidate-portable-rp9.rp` 已通过以下机器检查：

- 外层主版本为 9。
- LZ4 索引可重新解码。
- 8 个 GZip 包均可解压和分类。
- Page、DesignDocument、DocumentSettings 内部版本均由 RP9 写入器生成。
- Page 中不再包含 `interactionmap` 或 `Axure:InteractionTreeMap`。
- 矩形仍为 `X=13`、`Y=17`、`Width=101`、`Height=37`。
- 便携包解压后的桥接器实际生成了该文件，证明发布目录中的 EXE 和两
  个 LZ4 DLL 可被独立加载。
- 桥接器结构化报告为：1 个页面、1 个设计文档、1 个设置包被重写，
  3 项交互数据被移除；写出后由 RP9 解析器回读并核对了 49 个静态
  对象记录和 1937 个静态属性叶值。

用户在 Axure RP 9 中重复打开已打开的候选文件时，Axure 显示“文件正
由另一进程使用”。进程检查确认占用者就是 AxureRP9.exe，而非桥接器；
进一步读取 Axure 错误日志后确认，这条提示其实是二次异常：RP9 首次
加载 DesignDocument 失败后进入 `Axure.Legacy.Version3_1.FileIO.IsLegacyFile`
回退路径，此时再次打开已被自身持有的文件，才抛出误导性的占用错误。

部件二分测试结果：

- 仅替换转换后的 Page：GUI 正常打开。
- 仅替换转换后的 DocumentSettings：GUI 正常打开。
- 任何包含转换后 DesignDocument 的组合：GUI 报错。

RP9 与 RP11 DesignDocument 对照显示，每个 `Axure:PackageInfo` 在 RP9
中必须包含空集合字段 `root-panel-infos`，而 RP11 已删除它。桥接器
现使用 Axure 自身属性对象的 `Add(string, Aa3I.Fa3P)` 方法补齐该字段；
当前矩形文档共补齐 5 处。同时移除 RP9 不认识的样式属性
`Radius`、`Duration`、`Easing`、`ScaleX/Y`、`TranslateX/Y`、`Rotate`。

修复后的完整候选文件通过真实 Axure RP 9 GUI 加载：

- 窗口标题为 `candidate-required-field-rp9 - Axure RP 9 ...`。
- 没有“报告错误”窗口。
- 空白和矩形两个批量输出均通过 `tools/verify-axure9-gui.ps1`。
- 原生 RP9 与降级输出的 1534×814 窗口截图，在画布区域
  `(328,216,1206,598)` 的 721188 个像素中差异像素为 0。
- 因此当前矩形的页面、位置、尺寸、边框和画布渲染获得了像素级一致
  性证据。

截图保存在：

```text
artifacts/verification/official-rp9-rectangle.png
artifacts/verification/downgraded-rp9-rectangle.png
```

## 官方复杂样本验证

`tools/verify-complex-samples.ps1` 会转换 Axure 11 安装目录中的四个官方
训练工程，并分别对输入、输出建立对象记录类型清单：

| 样本 | 真页面 | 面板/附属包 | RP11 记录 | RP9 记录 | 静态类型差异 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Prototype Starter | 8 | 20 | 973 | 819 | 0 |
| Prototyping Basics | 1 | 5 | 698 | 663 | 0 |
| Quick Win | 2 | 2 | 262 | 240 | 0 |
| UX Prototyping | 10 | 7 | 807 | 722 | 0 |

四个样本合计 21 个真实页面和 34 个动态面板状态/附属对象包。此前仅按
字节前缀匹配 `Axure:Page` 会误把 `Axure:PageStyle` 算成页面；当前
桥接器通过 RP9 对象模型中的精确记录类型区分两者，但两类包都会被
重写。所有数量变化都属于明确删除的 Interaction/Interation（Axure
内部存在这一拼写）记录；未发现静态记录类型数量变化。覆盖的可视
对象包括：

- 514 个 `VectorShape`
- 62 个 `ImageBox`
- 24 个 `DynamicPanel`
- 7 个 `TextBox`
- 6 个 `RadioButton`
- `Checkbox`、`Connector`、`ImageMapRegion`、`Layer`
- 34 个动态面板状态和对应 PanelDiagram

四个降级输出均通过真实 Axure RP 9 GUI 加载。`Quick Win` 输出还恢复
了两个页面标签，并在 RP9 画布中实际显示标题、正文、分栏、图片占位
和阴影卡片。截图保存为：

```text
artifacts/verification/downgraded-rp9-quick-win.png
artifacts/verification/official-rp11-quick-win-thumbnail.jpg
```

RP11 的 `DocumentSettings` 还包含 RP9 不认识的概览标签（全零 package
GUID）以及 `FloatingEditorLayoutInfos`、`PrototypeDeleted`、
`ShowLastPublishedCurrentLinks`、`UploadedSitmapIds`。桥接器会在写出
前移除这些工作区状态，不影响页面和元件数据。

## 官方元件库验证

Axure 11 自带的五套 `.rplib` 使用相同的 AC EF/LZ4/GZip 容器。
`tools/verify-library-samples.ps1` 将它们写成 RP9 对象流，并对输入输出
进行精确记录类型比对：

| 元件库 | 真页面 | 面板/附属包 | RP11 记录 | RP9 记录 | 静态类型差异 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Default | 40 | 0 | 855 | 801 | 0 |
| Flow | 37 | 0 | 614 | 576 | 0 |
| Icons | 920 | 0 | 12030 | 11110 | 0 |
| Sample form patterns | 48 | 56 | 21373 | 18430 | 0 |
| Sample UI patterns | 15 | 28 | 3345 | 2777 | 0 |

五套库合计 1060 个真实页面、84 个面板/附属包、38217 个输入记录；
写出后回读核对 33694 个静态记录和 1105202 个静态属性叶值。覆盖包括
12 个中继器、3 个表格、15 个表格单元格、菜单、树节点、列表框、
27 个组合框、5 个文本域、内联框架、截图、43 个动态面板、261 个分组
层和 2282 个矢量图形。五个降级结果均已由真实 Axure RP 9 GUI 加载；
元件库树可以显示 `Common`、`Box`、`Ellipse`、`Image`、`Button` 等条目。

## 关于“Flex 容器”的核查

对当前安装的 Axure RP 11、四个官方训练工程和五套官方元件库进行完整
记录类型与属性键扫描，没有发现 `Flex`/`Flexbox` 记录或属性。Axure
官方 2024 年更新说明描述的是在页面或动态面板内进行父容器对齐，而
不是自动 Flexbox 布局：

```text
https://www.axure.com/blog/whats-new-april-2024
```

因此当前桥接器没有凭空实现一个样本中不存在的 Flex 映射。若后续提供
确实包含此类专属结构的 RP11 文件，应以其真实记录类型新增坐标烘焙
规则，而不是根据未经验证的功能名称猜测格式。

## 下一验证目标

1. 为第三方组件库和自定义元件建立更多受控样本。
2. 建立字体缺失和外部资源不可用时的替代策略。
3. 为图片、SVG 和组件覆盖属性建立更多像素级基准。
4. 若实际文件出现官方样本未覆盖的新记录类型，先阻止静默丢失并输出
   明确诊断，再添加样本驱动的降级规则。
