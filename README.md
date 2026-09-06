# NavisFavorites · Navisworks 模型收藏夹

NavisFavorites 是面向 Autodesk Navisworks Manage 2024 的本机收藏夹插件。V0.2 修复了用户级 Bundle 的自动发现链路，增加顶部独立“收藏夹”页签和“工具附加模块”备用入口，并可把旧版模型收藏严格迁移到修改版模型。

插件只保存模型节点引用，只改变当前 Navisworks 会话的选择和临时显隐；不会保存或修改 NWC/NWF/NWD。

## 支持环境

- Navisworks Manage 2024 `21.0.1397.16`
- Navisworks API `21.0.0.0`
- Windows x64、.NET Framework 4.8
- 本机安装目录：`E:\auto\Navisworks Manage 2024\`

V0.2 只针对本机 Navisworks 2024 编译，不承诺兼容 2025/2026。

## 日常入口

正常重启 Navisworks 后，顶部会出现独立的“收藏夹”页签，其中有：

- `★ 收藏夹`：打开可停靠收藏夹窗格。
- `★ 收藏当前选择…`：把当前同一源模型中的选择加入指定收藏夹。
- `版本迁移…`：手动选择旧版收藏来源并预览严格匹配结果。

如果 Ribbon 临时不可见，可从 Navisworks 的“工具附加模块”使用 `★ 打开收藏夹`、`★ 收藏当前选择…` 和 `版本迁移…`。原 Selection Tree 右键菜单中的 `★ 添加到收藏夹…` 也继续保留。

## 修改版自动继承

1. V0.2 首次打开旧版模型的收藏夹窗格时，会为旧版记录结构指纹。
2. 以后打开文件名带新日期、`V/VER/VERSION`、`R/REV`、`修改版/修订版/新版` 标记的新文件，再打开收藏夹窗格。
3. 只有“系列名唯一”且根节点无关的结构相似度不低于 75% 时，才自动从最近的确定旧版继承。
4. 自动完成后，窗格顶部显示来源版本、成功数、跳过数和歧义数；点击“查看”可看逐项明细。

迁移采用复制语义：旧版收藏不变，新版得到自己独立的 PathId、IndexPath 和层级快照。同名收藏夹合并并去重；未匹配或有歧义的项目只进入报告，不复制到新版收藏树。

如果自动条件不足，点击 `版本迁移…` 手动选择来源。人工迁移仍使用相同的严格对象匹配，不做名称相似度或模糊匹配。

## 其他功能

- 按源模型版本隔离收藏，顶部源模型选择器自动跟随单模型当前选择。
- 新建、重命名、删除收藏夹；添加当前选择；删除收藏项目。
- 单击、Ctrl 多选、Shift 连选、Ctrl+A 和“全选”。
- 选择、隐藏、显示、隐藏未选定、全部显示。
- 失效项明确标记，不阻断其余有效收藏的操作。
- JSON 原子写入、上一版备份、损坏文件保留和 schema v1→v2 升级备份。

收藏数据位于：

```text
%APPDATA%\NavisFavorites\favorites.json
%APPDATA%\NavisFavorites\settings.json
```

首次由 V0.1 升级到 V0.2 时，会先在同一目录生成 `favorites.json.schema1-backup-时间戳`，再写入 `schemaVersion: 2`。现有收藏夹、项目 ID 和旧模型范围都会保留。

## 编译与部署

在项目根目录执行：

```powershell
.\scripts\build.ps1
.\scripts\deploy.ps1 -Mode Bundle
```

正式部署位置为：

```text
%APPDATA%\Autodesk\ApplicationPlugins\NavisFavorites.bundle
└─ Contents\v21\0.2.0.0\NavisFavorites.dll
```

XAML 同时部署到程序集目录和 `zh-CN`、`en-US`、`ja-JP` 子目录。本机隔离宿主已确认 Bundle 可自动发现，因此没有启用安装目录备用部署。仅当 Bundle 再次无法识别时才使用：

```powershell
.\scripts\deploy.ps1 -Mode InstallPlugins
```

脚本确保两种加载方式互斥，避免重复插件 ID。部署时若 Navisworks 已运行，脚本不会关闭现有会话；需要正常重启 Navisworks 才会加载 V0.2。

实现细节、迁移规则和验证证据见 [DEVELOPMENT.md](DEVELOPMENT.md)。

## 在其他电脑构建

需要 Windows、.NET SDK 和已安装的 Navisworks Manage 2024。Autodesk API 程序集由本机产品安装提供，不包含在仓库中。

```powershell
.\scripts\build.ps1 -NavisworksInstallDir 'C:\Program Files\Autodesk\Navisworks Manage 2024'
```

随后按“编译与部署”章节部署并重启 Navisworks。测试工程位于 `tests/`；宿主测试需要本机 Navisworks 和自行提供的模型。

仓库不包含实际工程模型、个人收藏数据或编译产物。`DEVELOPMENT.md` 中的验证记录描述原开发环境，不能视为对所有电脑的兼容性保证。
