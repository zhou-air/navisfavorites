# NavisFavorites V0.2 开发记录

更新日期：2026-08-26

## 本机目标与安全边界

| 项目 | 实测结果 |
| --- | --- |
| 产品 | Autodesk Navisworks Manage 2024 |
| 产品版本 | `21.0.1397.16` |
| 主程序 | `E:\auto\Navisworks Manage 2024\Roamer.exe` |
| API 程序集版本 | `21.0.0.0` |
| 目标框架 | `.NET Framework 4.8`、x64 |
| 插件程序集版本 | `0.2.0.0` |
| 固定开发者 ID | `2E3F445B-6C6E-4A98-9A9C-203680A746B8` |

未安装 Visual Studio、Navisworks SDK、第三方 UI 框架或大型组件。工程继续使用本机 .NET SDK、MSBuild、已缓存 net48 引用程序集和 Navisworks 随产品提供的 API。

插件没有模型保存调用。测试只打开模型、读取节点、修改隔离会话的选择/临时显隐，并在结束前恢复显隐。

## V0.2 入口与自动发现修复

实施前，全新 Navisworks 进程没有加载 NavisFavorites 程序集，Ribbon 状态也没有自定义页签。V0.2 将 Bundle 清单调整为官方 `ApplicationPackage → Components → RuntimeRequirements + ComponentEntry` 结构：

- `Platform="NAVMAN|NAVSIM"`
- `SeriesMin="Nw21"`、`SeriesMax="Nw21"`
- `AppType="ManagedPlugin"`
- `ModuleName="./Contents/v21/0.2.0.0/NavisFavorites.dll"`

顶部入口由 `CommandHandlerPlugin`、`RibbonLayout` 和 XAML 提供，页签标题为“收藏夹”，包含三个命令：

- `NavisFavorites.OpenPane`
- `NavisFavorites.AddSelection`
- `NavisFavorites.MatchRevision`

另外注册三个 `AddInLocation.AddIn` 静态备用入口；原 `AddInLocation.CurrentSelectionContextMenu` 收藏命令继续保留。没有修改 Autodesk 内置“常用”页签，也没有使用 Hook、反射或私有 UI 注入。

XAML 位于程序集同目录，并复制到 `zh-CN`、`en-US`、`ja-JP`。部署脚本支持 `Bundle` 与 `InstallPlugins` 两种互斥模式。隔离进程已验证用户级 Bundle 能自动发现正式程序集，所以当前只启用 Bundle，没有在产品安装目录部署第二份插件。

## schemaVersion 2

`favorites.json` 中每个 `ModelFavoriteScope` 新增：

- `familyId`、`familyKey`
- `firstSeenUtc`、`modelFileLastWriteUtc`、`revisionToken`
- `structureFingerprint`
- `inheritedFromScopeId`、`migrationHistory`

每个迁移后的项目另存 `migratedFromItemId` 和 `migrationMethod`。旧字段、旧模型范围、收藏夹 ID、项目 ID 和顺序保持不变。

首次读取 schema v1 时，程序先复制原文件为 `favorites.json.schema1-backup-yyyyMMdd-HHmmss-fff`，再原子写入 schema v2。损坏文件仍保留为带时间戳的副本并加载空数据，不覆盖损坏原件。

本次还在正式数据升级前建立了只读保护副本：

```text
C:\Users\34084\AppData\Roaming\NavisFavorites\favorites.json.pre-v2-20260826-194736
```

正式 `favorites.json` 目前仍为 schema v1；当前正在运行的 Navisworks 会话没有被关闭，也没有触发正式数据升级。重启后首次使用 V0.2 时才会自动升级。

## 模型系列规则

`ModelFamilyName` 从文件主名末尾剥离明确版本标记，支持：

- `yyyy-MM-dd`、`yyyyMMdd` 等日期后缀
- `V`、`VER`、`VERSION`
- `R`、`REV`
- `修改版`、`修订版`、`新版`

结构指纹不包含文件根节点，使用根节点下前两层的 `DisplayName + ClassName` 稳定标记集合。自动继承要求：

1. 系列名相同；
2. 候选只属于一个 `familyId`；
3. 有可比较版本标记时，来源必须早于目标且最近；同样接近的候选拒绝自动选择；
4. 无可比较标记时使用唯一的最近创建范围；时间并列则拒绝；
5. Jaccard 结构相似度至少为 `0.75`。

最近版本若未通过结构阈值，不会退而选择更老版本。任何不唯一条件都转为非阻塞提示和人工选择入口。

## 修改版对象匹配

`RevisionItemMatcher` 对每个旧收藏依次尝试：

1. 在目标模型索引下重建 PathId，校验时忽略已变化的文件根节点；
2. 校正模型索引后的 IndexPath，再尝试原 IndexPath，同样做根节点无关校验；
3. 去除根节点后的完整名称与 ClassName 层级逐级精确匹配；
4. 全模型唯一的 `DisplayName + ClassName`；
5. 同名同类型节点之间唯一最长祖先后缀。

仍然重名或缺失即跳过，不使用编辑距离、名称相似度或模糊匹配。成功后调用目标模型自身的 `CreatePathId()` 和 `CreateIndexPath()` 重新捕获引用，绝不把旧 PathId 直接写到新版。

迁移为复制语义。目标已有同名收藏夹时合并，并按新版稳定引用去重；迁移历史保存来源 scope 和内容哈希，防止每次打开重复写入。旧版数据不移动、不删除、不覆盖。

## UI 与服务行为

- DockPane 顶部保留源模型下拉框，并新增“版本迁移…”按钮。
- 自动迁移或安全条件不足时显示非阻塞横幅；横幅包含摘要、“查看”和“关闭”。
- 手动迁移对话框可选择当前文档中的目标模型、已有旧版范围，先预览再确认。
- 明细报告保留每个项目的收藏夹、名称、匹配方法或失败原因。
- 未匹配项目不复制到目标收藏树。
- V0.1 的多选、选择、隐藏、显示、隐藏未选定和全部显示语义保持不变；失效项不会阻断有效项。

## 构建与验证

正式 Release 和测试项目均要求 0 错误、0 警告。当前主要证据：

| 报告 | 验证内容 |
| --- | --- |
| `artifacts/core-tests-v2.json` | JSON 安全写入、多选、系列名与版本顺序 |
| `artifacts/bundle-probe-v2.json` | 无正式 DLL 手动注入时 Bundle 自动发现、三个 Ribbon 命令、独立页签、三个 Add-In 入口、程序集 `0.2.0.0` |
| `artifacts/user-data-upgrade-v2.json` | 当前真实 schema v1 数据的临时副本升级；1 个模型、6 个收藏夹、51 项、ID 全部保留、生成 1 个升级备份 |
| `artifacts/host-services-v2.json` | schema、结构阈值、唯一/歧义/缺失匹配、选择与临时显隐 |
| `artifacts/migration-source-capture-v2.json` | 从旧版样例捕获独立收藏引用 |
| `artifacts/revision-migration-v2.json` | 日期修改版副本自动继承、目标引用重建、旧版不变、同名文件夹合并去重、重复迁移不写入、选择与显隐 |

隔离宿主测试用 `QICHUANG-SITE-2026-08-27.nwc` 临时重命名副本模拟修改版。副本哈希与原模型一致，测试结束后已删除。

## 模型只读校验

原始样例：`QICHUANG-SITE-2026-08-21.nwc`

```text
计划基线 SHA-256: B6155A52713CE56F402DC9A2677BF9BB3F5AD92303929156CBA0C1CAE858A5B6
V0.2 验证后 SHA-256: B6155A52713CE56F402DC9A2677BF9BB3F5AD92303929156CBA0C1CAE858A5B6
```

没有 NWC/NWF/NWD 保存调用，也没有创建或覆盖正式模型文件。

## 当前会话与启用方式

实施期间保留了用户当前 Navisworks 会话。该进程启动早于 V0.2 部署，因此不会热更新顶部页签。正常关闭并重新启动 Navisworks 后，用户级 Bundle 会自动加载 `0.2.0.0`；无需使用 `AddPluginAssembly`，也无需安装目录备用副本。

按照个人自用约定，本轮以插件记录、Ribbon Tab/Button ID 和隔离宿主调用做功能验收，没有执行鼠标级视觉验收。

## 参考

- [Autodesk Navisworks 插件发布指南](https://aps.autodesk.com/marketplace/publisher-center/navisworks-publisher-guidelines)
- [Autodesk 自定义 Ribbon 说明](https://blog.autodesk.io/custom-ribbon-of-navisworks-part-1/)
- [Autodesk Navisworks API 概览](https://aps.autodesk.com/developer/overview/navisworks-api)
