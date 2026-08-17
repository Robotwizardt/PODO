<p align="center">
  <img src="src/WitchDrawer.App/Assets/app.png" alt="PODO Logo" width="128" height="128" />
</p>

<h1 align="center">PODO</h1>

<p align="center">
  <img src="https://img.shields.io/badge/version-1.3.0-blue" alt="Version" />
  <img src="https://img.shields.io/badge/license-CC%20BY--NC--SA%204.0-green" alt="License" />
  <img src="https://img.shields.io/badge/.NET-10.0-purple" alt=".NET" />
  <img src="https://img.shields.io/badge/platform-Windows-blue" alt="Platform" />
</p>

PODO 是一款基于原生 WPF 构建的 Windows 桌面工作台：文件收纳盒、项目收纳盒和独立桌面纸片在同一个应用中运行。常用文件可以直接拖入桌面收纳盒，待办和笔记则使用集成的 PaperTodo 原生纸片窗口。

English: PODO is a native WPF Windows desktop workspace combining file drawers, project notes, and PaperTodo desktop papers in one application.

项目仓库：[Robotwizardt/PODO](https://github.com/Robotwizardt/PODO)

## 效果展示

[![WitchDrawer 桌面收纳效果展示](docs/images/witchdrawer-desktop-showcase.png)](https://www.bilibili.com/video/BV1zx3c6eEX8/)

▶ [在哔哩哔哩观看 WitchDrawer 视频演示](https://www.bilibili.com/video/BV1zx3c6eEX8/)

## 功能特性

- **普通收纳盒** — 将拖入的文件或文件夹移入 PODO 的应用数据存储目录
- **项目收纳盒** — 大号彩色阶段标记可直接切换状态；将收纳盒或 PaperTodo 纸片拖到项目盒四周即可多项关联，有内容的方向会显示带数量的展开按钮
- **项目文件夹** — 像手机应用文件夹一样，把两个项目盒拖到一起即可归组；文件夹动态显示所有成员的名称与阶段，点击卡片打开完整项目并可随时收回
- **映射收纳盒** — 仅存储绝对路径引用，源文件保留在原位
- **像素收纳盒** — 像素风格的收纳盒，为桌面增添趣味
- **桌面浮动窗口** — 每个收纳盒显示为精美的浮动桌面窗口，支持自由拖放定位
- **窗口位置记忆** — 自动记住每个收纳盒在桌面上的位置
- **系统图标** — 拖入的文件显示系统原生图标
- **文件名显示** — 网格视图可按收纳盒显示或隐藏文件名
- **拖出支持** — 可以将项目从收纳盒中拖出作为文件放置
- **跨盒拖放** — 支持在收纳盒之间拖放移动图标
- **快捷面板** — 按 `Ctrl+Alt+W` 跨所有收纳盒搜索并打开项目
- **三套主题** — 清透雅致 / 玻璃光泽 / 水晶棱镜
- **图标大小** — 超大 / 大 / 中 / 小 四档可调
- **开机自启动** — 可在设置中开启/关闭
- **检查更新** — 自动检测 GitHub Releases 新版本
- **原位还原删除** — 删除收纳项或收纳盒时，普通/像素盒文件恢复到原来的位置；原位置不可用则回退到桌面，重名自动加后缀；映射盒只删除引用
- **窗口恢复** — 可从主页收纳盒菜单恢复单个窗口，或从系统托盘显示全部收纳盒
- **桌面图标隐藏** — 可在设置中隐藏 Windows 桌面文件、文件夹和快捷方式，不移动或删除文件
- **系统托盘** — 最小化到系统托盘，不占用任务栏
- **单实例运行** — 防止重复启动
- **桌面待办纸片** — 集成 PaperTodo 原始待办纸片，支持直接添加、勾选、拖动排序、改名、置顶和胶囊折叠
- **Markdown 笔记纸片** — 集成 PaperTodo 原始笔记纸，自动保存，支持 Markdown 编辑、预览、图片和桌面胶囊折叠
- **统一入口** — PODO 主界面和系统托盘都能新建或显示全部桌面纸片；不会再单独启动第二个应用或托盘图标
- **旧便签迁移** — 首次启动会先将旧版 PODO 待办/笔记转入 PaperTodo 数据，再移除旧的收纳盒式便签记录

## 技术栈

| 技术 | 说明 |
|------|------|
| .NET 10 | 运行时 |
| WPF | 原生 Windows UI |
| Win32 API | Shell 打开、全局快捷键、窗口层级 |
| SQLite | 本地持久化（WAL 模式） |
| CommunityToolkit.Mvvm | MVVM 框架 |
| xUnit | 单元测试 |

本项目有意避免使用 Electron、WebView 外壳和沉重的第三方 UI 框架。

## 仓库结构

```text
WitchDrawer.sln
src/
  WitchDrawer.App/       PODO WPF UI、PaperTodo 纸片源码、视图模型、拖放、快捷键绑定
  WitchDrawer.Core/      模型、SQLite 持久化、文件导入/删除规则、更新检查
  WitchDrawer.Native/    Shell 打开、全局快捷键、系统托盘
  PaperTodo.NotifyIconWpf/ PaperTodo 原始依赖的通知图标库
tests/
  WitchDrawer.Core.Tests/
THIRD-PARTY/             第三方源码许可说明
```

## 环境要求

- Windows 10/11
- .NET SDK `10.0.300` 或兼容的 .NET 10 SDK

## 构建

```powershell
dotnet build WitchDrawer.sln
```

日常启动直接双击仓库根目录的 [启动PODO.cmd](启动PODO.cmd)。程序未运行时，它会构建当前源码后再启动；若已有 PODO 正在运行，会提示先退出后重启，避免旧版本继续留在内存中。

也可以在仓库根目录执行构建脚本：

```powershell
.\build.ps1
```

该脚本使用 `Release` 配置构建完整解决方案。

## 本地开发

```powershell
.\dev.ps1
```

该脚本使用 `Debug` 配置构建并启动 WPF 应用。

Debug 可执行文件位于：

```text
src/WitchDrawer.App/bin/Debug/net10.0-windows/PODO.exe
```

## 测试

```powershell
dotnet test WitchDrawer.sln
```

测试覆盖：默认收纳盒创建、普通/映射/像素盒导入、重复文件名后缀、跨盒移动、原位还原删除、更新 URL 校验等。

## 运行时数据

```text
%LocalAppData%\PODO\
  podo.db                 SQLite 数据库（收纳盒、项目和旧版迁移资料）
  Boxes\{BoxId}\          普通收纳盒的文件存储
  PaperTodo\data.json     PaperTodo 待办与笔记纸片数据
  PaperTodo\note-assets.lmdb  PaperTodo 笔记图片数据
  logs\                   运行日志
```

## 开源协议

本项目采用 **CC BY-NC-SA 4.0** 协议开源。

- **BY（署名）**：二次修改必须注明原作者 Thewitchcat
- **NC（非商用）**：禁止商业使用
- **SA（相同方式共享）**：衍生作品必须以相同协议开源

第三方组件仍分别适用其原始许可，详情见下方“参考与致谢”及 `THIRD-PARTY` 目录。

## 参考与致谢

PODO 是在 WitchDrawer 基础上继续开发的衍生项目，并集成了 PaperTodo。以下两个项目是本项目的主要来源与参考，感谢原作者的工作：

1. **[WitchDrawer](https://github.com/witchscottishfoldcat/WitchDrawer)**
   - PODO 的桌面收纳盒、项目结构及部分基础实现源自 WitchDrawer，并在其基础上继续开发。
   - 原项目作者及许可要求以 WitchDrawer 原仓库为准；使用和再分发本项目时应保留原作者署名。
2. **[PaperTodo](https://github.com/snownico0722/PaperTodo)**
   - PODO 的桌面待办纸片、Markdown 笔记纸片及相关交互基于 PaperTodo 源码集成和适配。
   - PaperTodo 采用 PolyForm Noncommercial License 1.0.0 与 PaperTodo Individual Professional Use Additional Permission 1.0，完整许可见 [THIRD-PARTY/PaperTodo-LICENSE.md](THIRD-PARTY/PaperTodo-LICENSE.md)。

### 第三方依赖

- **[Hardcodet WPF NotifyIcon](https://github.com/hardcodet/wpf-notifyicon)**
   - PODO 的 WPF 系统托盘能力使用并适配了 Hardcodet WPF NotifyIcon 源码。
   - 该项目采用 MIT License，许可与版权声明见 [THIRD-PARTY/Hardcodet.Wpf.NotifyIcon-LICENSE.md](THIRD-PARTY/Hardcodet.Wpf.NotifyIcon-LICENSE.md)。

PODO 并非上述项目的官方版本，也不代表上述项目作者对 PODO 提供背书。
