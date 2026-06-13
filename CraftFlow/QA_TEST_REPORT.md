# CraftFlow QA 测试报告

> **测试类型**：静态代码审查 + 逻辑验证  
> **测试日期**：2026-06-12  
> **测试范围**：26 个源文件（全部）  
> **测试员**：严过关（Yan）· QA 工程师  

---

## 测试结果摘要

| 类别 | 通过 | 失败 | 警告 |
|------|------|------|------|
| 编译一致性 | 10 | 0 | 1 |
| BOM 展开算法 | 4 | 1 | 0 |
| IPC 集成 | 7 | 0 | 1 |
| 数据层 | 4 | 2 | 1 |
| UI 集成 | 5 | 1 | 1 |
| Dalamud v14 合规 | 4 | 1 | 0 |
| 边界/错误处理 | 5 | 0 | 0 |

**总计**：39 通过 / 6 失败 / 4 警告  
**IS_PASS: NO**

---

## 问题清单

### Critical（严重 — 影响核心功能，必须修复）

#### C-01: MainWindow 未使用 WindowSystem，ImGui 窗口无法正确渲染

- **文件**: `Plugin.cs` 第 90 行，`UI/MainWindow.cs` 全文件
- **问题描述**: MainWindow 继承自 `Dalamud.Interface.Windowing.Window`，但 Plugin.cs 直接将 `_mainWindow.Draw` 注册到 `UiBuilder.Draw`，而非通过 WindowSystem 管理。Dalamud 的 Window 类设计为通过 WindowSystem.Render() → Window.Render() → ImGui.Begin/End + Draw() 的调用链工作。直接调用 Draw() 跳过了 ImGui 窗口创建（Begin/End），导致：
  1. 窗口内容会在 Dalamud 覆盖层上下文中"泄漏"渲染，而非独立窗口
  2. `/craftflow` 命令切换 `IsOpen` 属性无法实际控制窗口显示/隐藏
  3. `OpenMainUi` 事件设置 `IsOpen = true` 同样无效
- **修复建议**:
  ```csharp
  // Plugin.cs 中添加
  private readonly WindowSystem _windowSystem = new("CraftFlow");
  
  // 构造函数中：
  _windowSystem.AddWindow(_mainWindow);
  pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
  
  // Dispose 中：
  pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
  _windowSystem.RemoveWindow(_mainWindow);
  ```

#### C-02: BOM 缓存导致子节点数量计算错误

- **文件**: `Services/BomExpander.cs` 第 84-97 行（缓存命中路径）
- **问题描述**: 缓存存储 Quantity=1 的节点模板，缓存命中时创建新 BomNode 并设置 `Quantity = quantity`（正确），但 `Children = cached.Children`（共享引用，数量未更新）。当同一物品在不同分支以不同数量出现时，子节点保留首次展开时的数量，导致材料汇总结果错误。
  
  **示例**:
  - 物品 A → 2×B + 3×C
  - 物品 C → 2×B（需 2×3=6 个 B）
  - 首次展开 B（来自 A→B，qty=2），缓存 B 的子节点 D(qty=2)
  - 二次命中 B（来自 A→C→B，qty=6），返回 B(qty=6) 但子节点仍为 D(qty=2)
  - 正确应为 D(qty=6)，MaterialAggregator 聚合结果偏差

- **修复建议**: 
  方案 A（推荐）：移除缓存，因 BOM 深度限制为 10 层，性能可接受
  ```csharp
  // 移除 _cache 字段和相关代码
  public BomNode Expand(uint itemId, int quantity)
  {
      return ExpandRecursive(itemId, quantity, 0, new HashSet<uint>());
  }
  ```
  方案 B：深度复制 + 按比例缩放子节点数量
  ```csharp
  if (_cache.TryGetValue(itemId, out var cached))
  {
      visited.Remove(itemId);
      var scale = (double)quantity / cached.Quantity;
      return CloneAndScale(cached, quantity, scale);
  }
  ```

---

### Major（重要 — 功能缺失或逻辑错误）

#### M-01: DetermineScripType 始终返回 OrangeScrip，紫票收藏品无法查询

- **文件**: `Data/GameData/RecipeRepository.cs` 第 322-330 行
- **问题描述**: `DetermineScripType` 方法硬编码返回 `ScripType.OrangeScrip`，导致 `GetCollectibles(ScripType.PurpleScrip)` 永远返回空列表。CollectibleTab 的紫票选择功能完全失效。
- **修复建议**: 通过 CollectablesShopItem 的 ScripShop 引用或 RowId 范围判断工票类型，或查询关联的 ScripShopItem 表获取 ScripType 字段。

#### M-02: EquipmentTab._classJobNames 从未填充，职业筛选器不工作

- **文件**: `UI/Tabs/EquipmentTab.cs` 第 36 行
- **问题描述**: `_classJobNames` 字典声明后从未被填充数据。DrawLeftPanel 中的职业下拉框只会显示"全部职业"，无法选择特定职业。应从 LuminaCache.ClassJobSheet 加载生产职业列表。
- **修复建议**:
  ```csharp
  // 构造函数或首次 Draw 时加载
  var craftJobs = new[] { 8, 9, 10, 11, 12, 13, 14, 15 }; // CRP-BSM-ARM-GSM-LTW-WVR-ALC-CUL
  foreach (var id in craftJobs)
  {
      if (_recipeRepo.GetClassJobName(id) is { } name)
          _classJobNames[id] = name;
  }
  ```

#### M-03: GetEquipmentByClassJob 忽略 version 参数

- **文件**: `Data/GameData/RecipeRepository.cs` 第 70 行
- **问题描述**: 方法签名接受 `int? version = null` 参数，但方法体内从未使用此参数进行版本筛选。注释和 PRD 均要求支持版本过滤。
- **修复建议**: 在遍历配方时增加版本判断逻辑（如通过配方的 IsExpert 或版本相关字段过滤）。

#### M-04: ConsumableTab 每帧调用 GetConsumables，性能严重问题

- **文件**: `UI/Tabs/ConsumableTab.cs` 第 87 行
- **问题描述**: `DrawLeftPanel()` 每次渲染帧都调用 `_recipeRepo.GetConsumables(_currentCategory)`，该方法内部遍历所有配方和物品数据。以 60FPS 计算，每秒执行 60 次全表扫描，将导致明显卡顿。
- **修复建议**: 缓存查询结果，仅在类别切换时重新查询
  ```csharp
  private List<Item> _cachedConsumables = [];
  private ConsumableCategory _cachedCategory = (ConsumableCategory)(-1);
  
  // DrawLeftPanel 中：
  if (_cachedCategory != _currentCategory)
  {
      _cachedConsumables = _recipeRepo.GetConsumables(_currentCategory);
      _cachedCategory = _currentCategory;
  }
  ```

---

### Minor（次要 — 不影响核心功能但应改进）

#### m-01: OpenMainUi 事件处理器未在 Dispose 中注销

- **文件**: `Plugin.cs` 第 91 行
- **问题描述**: `pluginInterface.UiBuilder.OpenMainUi += () => _mainWindow.IsOpen = true;` 注册的 lambda 在 Dispose 中未被移除。插件卸载后，Dalamud 可能仍尝试调用该委托。
- **修复建议**: 将 lambda 保存为字段，在 Dispose 中注销。

#### m-02: Checkbox 互斥模式可导致两项同时取消勾选

- **文件**: `UI/Tabs/ConsumableTab.cs` 第 67-76 行，`UI/Tabs/CollectibleTab.cs` 第 55-68 行
- **问题描述**: 食物/药品和橙票/紫票的 checkbox 不是真正的 Radio Button 模式，用户可以取消两项勾选，但内部状态仍保留上一个值。
- **修复建议**: 使用 ImGui.RadioButton 或在取消勾选时强制设置默认值。

#### m-03: EquipmentTab._filteredEquipment 初始为空

- **文件**: `UI/Tabs/EquipmentTab.cs` 第 37 行
- **问题描述**: 装备列表在首次显示时为空，需要用户主动切换筛选条件才会加载。
- **修复建议**: 在构造函数或首次 Draw 时调用 RefreshEquipmentList()。

#### m-04: CraftWithArtisan 一次性推送所有步骤

- **文件**: `UI/Widgets/MaterialListWidget.cs` 第 279-284 行
- **问题描述**: 架构设计要求"轮询 IsBusy() 再推送下一项"，但实现中一次性推送所有步骤。虽然 Artisan 的 Endurance 模式可能支持队列，但不符合设计意图，且无法在步骤间检查状态。
- **修复建议**: 实现异步推送机制，或在 UI 中添加帧回调逐步推送。

#### m-05: FindBestTier 逻辑在 CollectibleTab 和 CollectibleCalculator 中重复

- **文件**: `UI/Tabs/CollectibleTab.cs` 第 209-229 行 vs `Services/CollectibleCalculator.cs` 第 119-141 行
- **问题描述**: 同一逻辑在 UI 层和 Service 层重复实现，违反 DRY 原则。
- **修复建议**: CollectibleTab 直接调用 CollectibleCalculator 的 FindBestTier（需改为 public/internal）。

#### m-06: DrawTreeNode 使用 ItemId+Depth 作为 ImGui ID 可能冲突

- **文件**: `UI/Widgets/MaterialListWidget.cs` 第 138 行
- **问题描述**: `$"###BomNode_{node.ItemId}_{node.Depth}"` 在同一物品出现在同层不同分支时会产生 ID 冲突，导致 ImGui 树节点展开/折叠状态异常。
- **修复建议**: 使用递增计数器或节点路径生成唯一 ID。

---

## 详细审查结果

### 1. 编译一致性检查 ✅ (10/10 通过)

| 检查项 | 结果 |
|--------|------|
| 命名空间引用（using 语句） | ✅ 全部 25 个文件命名空间正确 |
| 类名跨文件引用一致性 | ✅ 所有类引用与定义匹配 |
| 构造函数参数类型匹配 | ✅ Plugin.cs 创建的所有实例参数与构造函数签名一致 |
| MainWindow → Tab 构造参数 | ✅ EquipmentTab/ConsumableTab/CollectibleTab 参数匹配 |
| MaterialListWidget 方法签名 | ✅ DrawSummary/DrawTree/DrawPushToGbrButton/DrawCraftWithArtisanButton 签名与调用处一致 |
| IIpcClient 接口实现 | ✅ GbrIpcClient 和 ArtisanIpcClient 均实现 IsAvailable/Subscribe/Dispose |
| 泛型类型参数 | ✅ ICallGateSubscriber 泛型参数与 IPC 签名匹配 |
| RecipeRepository 方法返回类型 | ✅ FindRecipeByItem 返回 Recipe? 与调用处一致 |
| ScoreTier 嵌套类定义 | ✅ CollectibleInfo.cs 中定义，RecipeRepository 和 CollectibleCalculator 中正确使用 |
| ServiceProviderExtensions | ✅ GetRequiredService 扩展方法正确实现 |

⚠️ 注意：`Plugin.cs` 第 90 行 `pluginInterface.UiBuilder.Draw += _mainWindow.Draw;` 虽然编译可能通过（方法签名兼容），但运行时行为不正确（见 C-01）。

### 2. BOM 展开算法验证 ❌ (4/5 通过)

| 检查项 | 结果 |
|--------|------|
| MAX_DEPTH = 10 | ✅ 第 15 行定义 |
| HashSet 循环检测 + 回溯 | ✅ 第 56 行 Add + 第 143 行 Remove |
| 8 个材料槽位遍历 | ✅ 第 113 行 `for (int i = 0; i < 8; i++)` |
| 缓存逻辑数量正确性 | ❌ **C-02**: 缓存命中时子节点数量未按新倍率缩放 |
| 空配方/空材料边界处理 | ✅ 第 110 行 `if (recipe.HasValue)` + 第 118 行 `ingredientId.RowId == 0` |

### 3. IPC 集成验证 ✅ (7/7 通过)

| 检查项 | 结果 |
|--------|------|
| GBR IPC Channel 名称 | ✅ 全部 8 个 Channel 与架构文档一致 |
| Artisan IPC Channel 名称 | ✅ 全部 11 个 Channel 与架构文档一致 |
| ICallGateSubscriber 泛型参数 | ✅ 所有泛型参数与 IPC 签名匹配 |
| try-catch 保护 | ✅ 所有 IPC 调用均有 IpcError catch |
| GBR 无 AddGatherable 降级方案 | ✅ MaterialListWidget 生成文本清单 + 剪贴板 + 可选 AutoGather |
| IPC 可用性检测 | ✅ IpcAvailabilityChecker 通过重试 Subscribe 检测 |
| IPC 资源释放 | ✅ GbrIpcClient/ArtisanIpcClient Dispose 清空所有引用 |

⚠️ 注意：GBR 事件订阅器（AutoGatherWaiting/AutoGatherEnabledChanged）已获取但未实际使用，属预留功能。

### 4. 数据层验证 ❌ (4/6 通过)

| 检查项 | 结果 |
|--------|------|
| Lumina Sheet 类型 | ✅ Item/Recipe/CollectablesShopItem 等类型使用正确 |
| RecipeByResultItem 索引构建 | ✅ GroupBy + ToDictionary 逻辑正确 |
| Recipe 材料字段访问 | ✅ Ingredient[i].RowId + AmountIngredient[i] 与 Dalamud v14 一致 |
| FindRecipeByItem 返回类型 | ✅ Recipe? 与调用处一致 |
| DetermineScripType 实现 | ❌ **M-01**: 硬编码返回 OrangeScrip |
| version 参数使用 | ❌ **M-03**: GetEquipmentByClassJob 忽略 version 参数 |

⚠️ 注意：CollectablesShopRefine 的字段名（BaseCollectableRating/BaseCollectableReward 等）需与实际 Lumina Sheet 验证，可能因版本差异不一致。IsGatherableItem 和 IsFoodCategory/IsMedicineCategory 使用硬编码 RowId 范围，需与实际数据校验。

### 5. UI 集成验证 ❌ (5/6 通过)

| 检查项 | 结果 |
|--------|------|
| MainWindow 注入服务 | ✅ 全部 10 个参数与 Plugin.cs 创建的实例匹配 |
| Tab 构造参数 | ✅ 三个 Tab 构造参数与 MainWindow 中 new 的参数匹配 |
| MaterialListWidget 方法签名 | ✅ 4 个公共方法签名与 Tab 调用处完全匹配 |
| ImGui API (ImGuiNET) | ✅ 所有 ImGui 调用使用 Dalamud 包装的 API |
| GBR/Artisan 按钮动态状态 | ✅ 根据 IpcAvailabilityChecker 结果启用/禁用 |
| ConsumableTab 每帧查询 | ❌ **M-04**: GetConsumables 每帧调用 |

⚠️ 注意：EquipmentTab._classJobNames 未填充（M-02），_filteredEquipment 初始为空（m-03）。

### 6. Dalamud v14 API 合规性 ❌ (4/5 通过)

| 检查项 | 结果 |
|--------|------|
| IDalamudPlugin 接口实现 | ✅ 构造函数签名和 Dispose 方法正确 |
| IServiceProvider.GetService | ✅ 通过扩展方法 GetRequiredService 正确解析 |
| Window 基类继承 | ✅ 继承 Dalamud.Interface.Windowing.Window |
| 斜杠命令注册 | ✅ AddHandler/RemoveHandler 使用正确 |
| WindowSystem 使用 | ❌ **C-01**: 未使用 WindowSystem 管理窗口 |

### 7. 边界与错误处理 ✅ (5/5 通过)

| 检查项 | 结果 |
|--------|------|
| MaterialEntry.Source 使用 MaterialSource 枚举 | ✅ 默认值 Unknown，调用 GetMaterialSource 返回正确类型 |
| CraftStep.Status 使用 StepStatus 枚举 | ✅ 默认值 Pending，Crafting 状态正确设置 |
| CollectibleCalculator 零除保护 | ✅ 第 39 行 `tier.ScripReward <= 0` 检查 |
| BomNode.Children null 保护 | ✅ 初始化为 `[]`，所有访问点检查 Count |
| Lumina 数据缺失处理 | ✅ LuminaCache 每个 Sheet 加载均有 null 检查和 Warning 日志 |

---

## 智能路由判定

### 需路由到 Engineer（Alex）的问题

以下问题均为源码 Bug，需要工程师修改实现代码：

| ID | 严重程度 | 文件 | 问题描述 |
|----|----------|------|----------|
| C-01 | Critical | Plugin.cs + MainWindow.cs | 未使用 WindowSystem，窗口无法正确渲染和切换 |
| C-02 | Critical | BomExpander.cs | 缓存命中时子节点数量未缩放，BOM 结果错误 |
| M-01 | Major | RecipeRepository.cs | DetermineScripType 硬编码 OrangeScrip |
| M-02 | Major | EquipmentTab.cs | _classJobNames 未填充，职业筛选失效 |
| M-03 | Major | RecipeRepository.cs | GetEquipmentByClassJob 忽略 version 参数 |
| M-04 | Major | ConsumableTab.cs | 每帧调用 GetConsumables，性能问题 |
| m-01 | Minor | Plugin.cs | OpenMainUi 事件未在 Dispose 注销 |
| m-03 | Minor | EquipmentTab.cs | _filteredEquipment 初始为空 |
| m-04 | Minor | MaterialListWidget.cs | 一次性推送所有步骤，不符合设计 |
| m-06 | Minor | MaterialListWidget.cs | ImGui TreeNode ID 可能冲突 |

### QA 自行修复的问题

无。所有发现的问题均为源码 Bug，非测试代码错误。

---

## IS_PASS: NO

项目存在 2 个 Critical 级别 Bug 和 4 个 Major 级别 Bug，核心功能（窗口渲染、BOM 计算、紫票查询、职业筛选、性能）均受影响，必须修复后重新验证。
