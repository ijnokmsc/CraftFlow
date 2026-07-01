# CraftFlow 架构评审报告

> 评审时间：2026-07-02
> 评审范围：`D:\deepseek\CraftFlow\CraftFlow\` 全部 41 个 C# 源文件，约 7,800 行代码
> 评审视角：分层合理性、耦合度、可维护性、可演进性

---

## 1. 总体评价

**结论：架构基础正确，但已逼近"重构临界点"。**

CraftFlow 采用了 Dalamud 插件标准的 UI/Services/Data 三层架构，依赖方向整体正确，关键决策（如放弃字符串匹配改用 ClassJobCategory 布尔属性、BOM 不缓存、IPC 延迟订阅）都体现了对 FF14 生态的深刻理解。但随着功能增长，三个文件已经出现明显的"上帝对象"倾向，需要及时拆分以避免债务累积。

| 维度 | 评分 | 说明 |
|------|------|------|
| 分层清晰度 | ★★★★☆ | UI/Service/Data/IPC 四层划分明确，依赖方向正确 |
| 职责单一性 | ★★★☆☆ | EquipmentRepository、MaterialListWidget 职责膨胀 |
| 耦合度 | ★★★☆☆ | UI 层存在循环依赖，Plugin.cs 是耦合中心 |
| 可测试性 | ★★☆☆☆ | 测试项目空置，静态属性 + 隐藏依赖阻碍单测 |
| 可演进性 | ★★★☆☆ | 新功能需改 Plugin.cs + MainWindow 两处，开闭原则受损 |
| 文档一致性 | ★★☆☆☆ | ARCHITECTURE.md 与实际代码已脱节 |

---

## 2. 架构亮点（值得保持的设计）

### 2.1 国服适配的工程决策

`EquipmentRepository.CanJobEquip` 放弃了 `ClassJobCategory.Name` 字符串匹配（国服返回中文名如"防护"，与英文 affix "fending" 永远不匹配），改用结构体的布尔属性（`cjc.PLD`、`cjc.WAR` 等）。这是经过验证的正确决策，注释清晰记录了根因。

### 2.2 BOM 展开的循环检测

`BomExpander` 使用 `HashSet<uint>` 回溯实现循环检测，配合 10 层深度限制。**不缓存的设计是有意为之**——注释明确说明缓存会导致子节点 Children 共享引用，使不同分支的数量缩放失效。这种"知道为什么不做"的决策比"做了但留下隐患"要好得多。

### 2.3 IPC 延迟订阅

`GbrIpcClient.DelayedSubscribe` 使用 `Framework.Update` 帧计数退避（1/2/4/8...帧，最多 10 次），解决了 Dalamud 插件加载时序问题。`Plugin.cs` 中 Artisan 的延迟订阅采用了同样的策略，模式一致。

### 2.4 崩溃恢复设计

`CraftProgressManager` 将制作进度持久化到 `PluginConfig.CraftProgress`，每次 `AdvanceOneItem()` 后立即 `Save()`。`CraftProgressWindow.ResumeCrafting()` 可从持久化状态重建轮询。这是面向长时制作场景的必要设计。

### 2.5 IPC 接口抽象

`IIpcClient` 接口定义了 `IsAvailable`/`Subscribe`/`Dispose` 契约，`GbrIpcClient` 和 `ArtisanIpcClient` 各自封装外部插件细节。所有 IPC 调用统一 try-catch，失败时设 `IsAvailable=false` 但不抛异常，符合"降级而非崩溃"的原则。

### 2.6 LuminaCache 索引化

`LuminaCache.Init()` 一次性构建 `RecipeByResultItem`、`ClassJobCategoryByName`、`GatherableItemIds` 等索引，将 O(N) 查询降为 O(1)。对于查询频繁的场景，物化优于按需查询。

---

## 3. 架构问题（按严重程度排序）

### P1. Plugin.cs 上帝对象（严重）

**现象**：`Plugin` 类构造函数手工 `new` 了 20+ 个对象，构成一个庞大的依赖图。`MainWindow` 构造函数接收 17 个参数。

**根因**：没有依赖注入容器，所有依赖关系在 `Plugin.cs` 中手工编织。`Plugin` 类同时承担：
- DI 容器（持有所有服务实例）
- 入口点（实现 `IDalamudPlugin`）
- 静态服务定位器（`Plugin.PluginInterface`、`Plugin.DataManager` 等静态属性）
- 公开 API（5 个 public 属性暴露内部状态）

**后果**：
- 增加任何新功能都要改 `Plugin.cs` 和 `MainWindow` 两个地方，违反开闭原则
- 静态属性 + 实例字段混合，依赖关系不清晰
- `MainWindow` 17 参数构造函数是明显的"上帝对象"信号

**建议**：引入轻量级 DI 容器（`Microsoft.Extensions.DependencyInjection`）。Dalamud API 15 的 `[PluginService]` 静态注入模式与 DI 容器不冲突，可以并存。短期方案至少引入 `ServiceLocator` 模式，将服务注册与入口点分离。

### P2. UI 层循环依赖（严重）

**现象**：`MaterialListWidget` 构造函数接收 `MainWindow? mainWindow` 参数，而 `MainWindow` 又持有 `MaterialListWidget` 实例。

```csharp
// MaterialListWidget.cs
public MaterialListWidget(..., MainWindow? mainWindow = null)

// MainWindow.cs
var materialListWidget = new MaterialListWidget(..., this);
```

**根因**：日志通知机制没有解耦。`MaterialListWidget` 通过 `_mainWindow?.AddLog(...)` 反向调用主窗口的日志面板。

**后果**：
- 循环引用使两个类无法独立测试
- 增加新窗口（如未来的设置窗口）需要再次注入 MainWindow
- 违反依赖倒置原则

**建议**：引入事件/回调机制解耦。

```csharp
// MaterialListWidget 暴露事件
public event Action<string, LogLevel>? OnLog;

// MainWindow 订阅
materialListWidget.OnLog += (msg, level) => AddLog(msg, level);
```

更进一步，可以引入 `INotificationService` 抽象，任何组件都可以发布日志，由订阅者决定如何展示。

### P3. EquipmentRepository 职责膨胀（严重）

**现象**：717 行（项目最大文件），承担 4 个不相关职责：

| 职责 | 行数占比 | 是否应在此类 |
|------|---------|-------------|
| 装备查询（GetEquipmentGroupedBySlot/GetBestInSlot） | ~40% | ✓ 是 |
| 版本映射（PatchVersionILvlRanges + GetPatchVersionByILvl） | ~15% | ✗ 应独立 |
| 槽位判断（GetSlotTypeForItem） | ~10% | △ 可接受 |
| **职业图标加载与缓存（GetClassJobIcon/GetClassJobIconTexture）** | **~30%（200+ 行）** | **✗ 完全无关** |

**关键问题**：项目里**已有** `JobIconService` 专门负责职业图标，但 `EquipmentRepository` 还在重复实现一份（含反射、回退映射表、纹理缓存）。这是典型的"重构未完成"痕迹。

**其他问题**：
- `PatchVersionILvlRanges` 硬编码 7.x 版本表，FF14 每个新版本都要改代码
- 反射读取 `ClassJob.Icon` 属性的代码（30+ 行）带有大量 Debug 日志，像是在调试中留下的

**建议**：
1. 将职业图标相关代码全部迁移到 `JobIconService`（删除 `EquipmentRepository` 中的 200 行）
2. 将 `PatchVersionILvlRanges` 提取为独立的 `PatchVersionMapper` 静态类，或移到配置文件
3. 反射代码应固化或移除（带 Debug 日志的反射是开发期产物）

### P4. MaterialListWidget UI 与业务混杂（严重）

**现象**：705 行（项目第二大文件），名为"Widget"实际承担了 5 个职责：

| 职责 | 行数 | 性质 |
|------|------|------|
| 材料表格渲染（DrawSummaryTable） | ~120 | UI |
| 库存缓存（RefreshCaches） | ~30 | UI 辅助 |
| GBR 推送逻辑（PushToGbr） | ~50 | **业务** |
| **Artisan 制作编排（CraftWithArtisan）** | **~80** | **业务（含材料缺口计算、步骤过滤、数量调整）** |
| BOM 树视图渲染（DrawTreeNode） | ~100 | UI |

`CraftWithArtisan` 方法内含大量业务逻辑：
- 材料充足性检查（区分 OnlyMissingMaterials 模式）
- 半成品库存扣减与跳过决策
- 制作数量按已有库存调整（`Math.Ceiling((double)stillNeeded / yield)`）

这些逻辑完全不属于 UI Widget，应该提取到 `CraftOrchestrator` 服务。

**建议**：
1. 新建 `CraftOrchestrator` 服务，承载 Artisan 制作编排逻辑
2. `MaterialListWidget` 只负责渲染和触发回调（`OnPushToGbr`、`OnStartCraft`）
3. 库存缓存逻辑可考虑提取到 `InventoryService`

### P5. 隐藏依赖（中等）

**现象**：`MaterialListWidget.ShowNpcShopInfo` 方法内直接调用 `Plugin.PluginInterface.GetIpcSubscriber(...)` 访问 DailyRoutines 插件的 IPC，但这个依赖没在构造函数声明。

```csharp
var drIpc = Plugin.PluginInterface.GetIpcSubscriber<uint, bool>(
    "DailyRoutines.Modules.AutoShowItemNPCShopInfo.OpenShopInfoByItemID");
```

**问题**：
- 绕过 IPC 层，违反分层
- 隐藏依赖，难以测试
- 违反显式依赖原则
- DailyRoutines 未安装时静默失败，但用户无感知

**建议**：若 DailyRoutines 是必要依赖，应在 `ArtisanIpcClient` 旁边新建 `DailyRoutinesIpcClient`；若是可选增强，应通过配置开关控制并给出明确提示。

### M1. ARCHITECTURE.md 与实际代码脱节（中等）

**现象**：`docs/ARCHITECTURE.md` 描述的"词缀匹配"方案已被废弃，实际代码改用 ClassJobCategory 布尔属性。文档中未提到 `CraftProgressManager`、`CraftProgressWindow`、`JobIconService`、`ItemIconService`、`CollectibleCalculator` 等后续新增的类。

**建议**：采用 ADR（架构决策记录）记录演进，而非维护一份可能过时的大文档。每次重要决策写一份 ADR（如 `ADR-001-放弃字符串词缀匹配.md`），保留决策上下文。

### M2. 测试项目空置（中等）

**现象**：`CraftFlow.Tests` 目录存在，但只有 csproj 和空的 Config 目录，没有任何实际测试。

**可测试的纯逻辑**：
- `BomExpander.Expand`（递归 + 循环检测）
- `EquipmentRepository.GetPatchVersionByILvl` / `GetILvlRangeForPatchVersion`（纯映射）
- `EquipmentRepository.GetSlotTypeForItem`（槽位判断）
- `EquipmentRepository.CanJobEquip`（职业匹配）
- `MaterialAggregator.Aggregate`（水晶过滤）
- `CraftOrderCalculator.CalculateOrder`（制作顺序）

**建议**：先对上述纯逻辑补单元测试，覆盖率不必追求 100%，重点覆盖边界条件（循环引用、深度超限、空输入）。

### S1. IpcAvailabilityChecker 冗余封装（轻微）

**现象**：`IpcAvailabilityChecker` 只有 2 个方法，各自一行 `return _xxxIpc.IsAvailable;`，没有增加任何价值。

**建议**：直接使用 `GbrIpcClient.IsAvailable` / `ArtisanIpcClient.IsAvailable`，删除此类。除非未来要加入"联合检测"逻辑（如两个都必须可用才能制作），否则没必要保留。

### S2. 重复的颜色常量（轻微）

**现象**：`MaterialListWidget` 和 `CraftProgressWindow` 都定义了相同的 `ColorGreen`/`ColorOrange`/`ColorRed` 常量。

**建议**：提取到 `UiColors` 静态类。

### S3. LuminaCache 一次性全量加载（轻微，需权衡）

**现象**：`Init()` 一次性加载 10+ 个 Sheet 到内存，全部 `ToDictionary` 物化。`ItemSheet` 数万行可能用不到全量。

**权衡**：物化 vs 按需查询。对于查询频繁的场景物化是对的，但可以改为**懒加载**——首次访问某个 Sheet 时才加载，而非启动时全部加载。这样能加快启动速度，减少峰值内存。

---

## 4. 重构优先级建议

按"投入产出比"排序，建议按以下顺序处理：

| 优先级 | 任务 | 预期收益 | 风险 |
|--------|------|---------|------|
| 1 | **迁移职业图标代码到 JobIconService**（P3） | 删除 200 行重复代码，EquipmentRepository 回归单一职责 | 低，纯代码迁移 |
| 2 | **解耦 MaterialListWidget ↔ MainWindow 循环依赖**（P2） | 引入事件机制，UI 层可独立测试 | 低，机械重构 |
| 3 | **提取 CraftOrchestrator 服务**（P4） | MaterialListWidget 瘦身至 ~400 行，业务逻辑可测试 | 中，需理清回调边界 |
| 4 | **补纯逻辑单元测试**（M2） | 为后续重构提供安全网 | 无 |
| 5 | **拆分 PatchVersionMapper**（P3 子项） | 版本映射独立，便于数据驱动化 | 低 |
| 6 | **引入轻量 DI 容器**（P1） | Plugin.cs 瘦身，新功能无需改入口 | 中，需调整所有构造函数 |
| 7 | **DailyRoutines IPC 显式化**（P5） | 消除隐藏依赖 | 低 |
| 8 | **更新架构文档为 ADR 集**（M1） | 决策可追溯 | 无 |

---

## 5. 不建议改动的部分

以下设计虽然不"完美"，但在当前规模下是合理的，**不建议为了"最佳实践"而改动**：

- **BomExpander 不缓存**：注释说明的根因正确，缓存会引入更深的 bug
- **CraftProgressWindow 轮询状态机**：单物品推送 + 轮询的模式虽然不如事件驱动优雅，但对 Artisan IPC 的兼容性最好
- **LuminaCache 物化全量 Sheet**：在当前规模下性能可接受，懒加载收益有限
- **EquipmentTab 持有业务状态**：ImGui 即时模式天然混合 View/ViewModel，强分 MVVM 反而增加复杂度

---

## 6. 一句话总结

> CraftFlow 是一个**功能完整、设计意图清晰**的插件，分层基础正确，但三个"上帝对象"（Plugin.cs、EquipmentRepository、MaterialListWidget）已到重构临界点。优先做 P2（解耦循环依赖）和 P3（迁移职业图标）两项，即可显著改善可维护性，且风险可控。
