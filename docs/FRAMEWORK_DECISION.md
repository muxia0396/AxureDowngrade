# 桌面框架选型：Tauri

## 结论

本项目采用 **Tauri 2 + React/TypeScript**，而不是 Electron。

## 评估

| 维度 | Tauri | Electron |
| --- | --- | --- |
| 安装/便携包体积 | 复用 WebView2，主体较小 | 必须捆绑 Chromium 与 Node.js |
| 本地二进制处理 | Rust 适合解析容器、校验偏移和哈希 | Node Buffer 开发快，但大型二进制更容易造成内存复制 |
| 权限边界 | 命令显式暴露给前端，默认边界较窄 | Node 集成需要额外维护隔离与 IPC 安全 |
| Windows 集成 | 可直接调用 Rust/Win32，并打包 sidecar | Node 原生模块和不同位数运行时的维护成本较高 |
| 前端开发 | React/Vite，与 Electron 基本相同 | React/Vite 生态成熟 |
| 调试速度 | Rust 首次编译较慢 | 纯 JS/TS 迭代通常更快 |

## 与 Axure 桥接的关系

Axure RP 9 的对象序列化器是 32 位 .NET Framework 程序集。无论选择
Tauri 还是 Electron，都不能直接在主进程中可靠加载它，因此项目采用
独立的 32 位 C# sidecar：

```text
Tauri UI
  -> Rust 命令层（文件检查、结果核验）
  -> 32 位 C# AxureDowngradeBridge
  -> Axure RP 9 自带序列化器
```

Electron 并不能消除这个 sidecar，反而会额外带来 Chromium/Node 的
发布体积。因此在相同转换能力下，Tauri 的分发和权限模型更适合本项目。

## 已验证的发布形态

当前 Windows x64 便携包包含：

```text
axure-downgrade-desktop.exe
bin/AxureDowngradeBridge.exe
bin/K4os.Compression.LZ4.dll
bin/K4os.Compression.LZ4.Legacy.dll
```

便携包解压后已完成桌面程序启动冒烟测试；包内 bridge 生成的 RP9 文件
也已由真实 Axure RP 9 GUI 打开。
