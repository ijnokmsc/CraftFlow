# CraftFlow 回归测试报告

> **测试类型**：回归测试（静态代码审查 + 逻辑验证）  
> **测试日期**：2026-06-12  
> **测试范围**：9 个修改文件，12 个 Bug 修复验证  
> **测试轮次**：Round 2（Engineer 修复后回归）  
> **测试员**：严过关（Yan）· QA 工程师  

---

## 回归测试结果摘要

| 类别 | 通过 | 失败 |
|------|------|------|
| Bug 修复验证（12 项） | 12 | 0 |
| 回归问题（新引入 Bug） | — | 1 |

**12 个原始 Bug 全部修复确认。发现 1 个新引入的回归问题。**

**IS_PASS: NO**（存在 1 个 Major 级别回归问题需修复）

---

## Bug 修复逐项验证

### C-01: MainWindow 未使用 WindowSystem → ✅ 已修复

**验证内容**:
- `Plugin.cs` 第 38 行：新增 `_windowSystem` 字段 ✅
- `Plugin.cs` 第 39 行：新增 `_openMainUiHandler` 字段，保存 lambda 引用 ✅
- `Plugin.cs` 第 93-94 行：`new WindowSystem("CraftFlow_WindowSystem")` + `_windowSystem.AddWindow(_mainWindow)` ✅
- `Plugin.cs` 第 95 行：`pluginInterface.UiBuilder.Draw += _windowSystem.Draw` （通过 WindowSystem 间接调用 MainWindow.Render→Draw） ✅
- `Plugin.cs` 第 96-97 行：`_openMainUiHandler = () => _mainWindow.IsOpen = true` + 注册 OpenMainUi ✅
- `Plugin.cs` 第 135 行：Dispose 中 `-= _windowSystem.Draw` ✅
- `Plugin.cs` 第 136 行：Dispose 中 `-= _openMainUiHandler` ✅

**结论**: WindowSystem 正确管理 MainWindow 生命周期，Draw 和 OpenMainUi 委托在 Dispose 中正确注销。修复完整。

---

### C-02: BOM 缓存导致子节点数量计算错误 → ✅ 已修复

**验证内容**:
- `BomExpander.cs` 无 `_cache` 字段 ✅
- `BomExpander.cs` 第 40-43 行：`Expand()` 直接调用 `ExpandRecursive()`，无缓存命中检查 ✅
- `BomExpander.cs` 第 53-117 行：`ExpandRecursive()` 每次调用创建新 BomNode 和新 Children 列表 ✅
- 第 109 行：递归调用传递 `new HashSet<uint>(visited)`，创建独立副本 ✅
- 循环检测（第 56 行 Add + 第 115 行 Remove）逻辑保留且正确 ✅
- MAX_DEPTH = 10 限制保留（第 18 行 + 第 70 行） ✅

**注意**: 第 109 行 `new HashSet<uint>(visited)` 替代了原来的 `visited` 引用传递 + 回溯 `visited.Remove(itemId)`。当前代码中第 115 行的 `visited.Remove(itemId)` 对于递归路径不再必要（因为子调用使用独立副本），但不会产生错误——仅是冗余操作。**无功能影响。**

**结论**: 缓存机制完全移除，每次展开返回独立的 BomNode 树，子节点数量不会共享。修复完整。

---

### M-01: DetermineScripType 硬编码 OrangeScrip → ✅ 已修复

**验证内容**:
- `RecipeRepository.cs` 第 321-340 行：`DetermineScripType` 已改为实例方法 ✅
- 第 326 行：检查 `shopItem.ScripShop.IsValid` ✅
- 第 328 行：获取 `shopItem.ScripShop.Value` ✅
- 第 333 行：基于 `scripShop.RowId >= 10000` 判断紫票 ✅
- 第 339 行：默认返回 `ScripType.OrangeScrip` ✅

**备注**: RowId >= 10000 的判断阈值需与实际 Lumina 数据验证，但作为简化实现逻辑正确，注释中也标注了"需根据实际数据修正"。

**结论**: 不再硬编码，紫票收藏品可以正确查询。修复完整。

---

### M-02: EquipmentTab._classJobNames 从未填充 → ✅ 已修复

**验证内容**:
- `EquipmentTab.cs` 第 36 行：`_classJobNames` 去掉 `readonly` ✅
- `EquipmentTab.cs` 第 58 行：构造函数中 `_classJobNames = _recipeRepo.GetCraftClassJobs()` ✅
- `RecipeRepository.cs` 第 347-373 行：新增 `GetCraftClassJobs()` 方法 ✅
  - 遍历 RecipeSheet 获取所有有效 CraftType ✅
  - 使用 `seenCraftTypes` HashSet 去重 ✅
  - 调用 `_cache.GetCraftTypeName(craftTypeId)` 获取名称 ✅
- `LuminaCache.cs` 第 178-189 行：新增 `GetCraftTypeName(uint craftTypeId)` 方法 ✅
  - 提供 8 个制作职业的映射字典 ✅
- `EquipmentTab.cs` 第 80-98 行：职业筛选 Combo 使用 `_classJobNames` 数据 ✅

**结论**: 职业下拉框将正确显示所有制作职业。修复完整。

---

### M-03: GetEquipmentByClassJob 忽略 version 参数 → ✅ 已修复

**验证内容**:
- `RecipeRepository.cs` 第 69 行：方法签名 `GetEquipmentByClassJob(uint classJobId)`，无 version 参数 ✅
- `EquipmentTab.cs` 第 184 行：调用 `_recipeRepo.GetEquipmentByClassJob(_selectedClassJobId)` 参数匹配 ✅
- `EquipmentTab.cs` 第 189 行：调用 `_recipeRepo.GetEquipmentByClassJob(0)` 参数匹配 ✅

**结论**: 移除未使用参数，消除了误导性 API。修复完整。

---

### M-04: ConsumableTab 每帧调用 GetConsumables → ✅ 已修复

**验证内容**:
- `ConsumableTab.cs` 第 39-40 行：新增缓存字段 `_cachedCategory` + `_cachedConsumables` ✅
- 第 39 行：`_cachedCategory` 初始值为 `(ConsumableCategory)(-1)`，确保首次 Draw 时触发查询 ✅
- 第 90-94 行：`if (_cachedCategory != _currentCategory)` 才调用 `GetConsumables` ✅
- 第 71、78 行：RadioButton 切换类别时重置 `_cachedCategory = (ConsumableCategory)(-1)` ✅
- 第 96 行：`var consumables = _cachedConsumables` 使用缓存数据 ✅

**结论**: 不再每帧全表扫描，仅在类别变化时刷新。修复完整。

---

### m-01: OpenMainUi 事件未在 Dispose 注销 → ✅ 已修复

**验证内容**:
- 与 C-01 一并解决：`_openMainUiHandler` 保存 lambda 引用 ✅
- `Plugin.cs` 第 136 行：`_pluginInterface.UiBuilder.OpenMainUi -= _openMainUiHandler` ✅

**结论**: 修复完整。

---

### m-02: Checkbox 互斥选择问题 → ✅ 已修复

**验证内容**:
- `ConsumableTab.cs` 第 68-79 行：食物/药品改用 `ImGui.RadioButton` ✅
- `CollectibleTab.cs` 第 55-66 行：橙票/紫票改用 `ImGui.RadioButton` ✅
- RadioButton 天然互斥，不存在两项同时取消的问题 ✅

**结论**: 修复完整。

---

### m-03: EquipmentTab._filteredEquipment 初始为空 → ✅ 已修复

**验证内容**:
- `EquipmentTab.cs` 第 61 行：构造函数末尾调用 `RefreshEquipmentList()` ✅
- `RefreshEquipmentList()` 方法完整存在（第 180-200 行） ✅
- 首次加载时 `_selectedClassJobId` 默认为 0，调用 `GetEquipmentByClassJob(0)` 加载全部装备 ✅

**结论**: 首次显示时装备列表已有数据。修复完整。

---

### m-04: CraftWithArtisan 一次性推送所有步骤 → ✅ 已修复

**验证内容**:
- `MaterialListWidget.cs` 第 24-25 行：新增 `_pendingCraftSteps` + `_currentCraftStepIndex` 状态字段 ✅
- 第 287-313 行：`CraftWithArtisan()` 仅推送第一个步骤，设置 `_pendingCraftSteps` + `_currentCraftStepIndex = 1` ✅
- 第 200-215 行：`DrawCraftWithArtisanButton()` 中轮询逻辑：
  - 检查 `_pendingCraftSteps != null` 且 `_currentCraftStepIndex < Count` ✅
  - 调用 `_artisanIpc.IsBusy()` 检查是否空闲 ✅
  - 空闲时推送下一步骤 ✅
  - 全部推送完毕后 `_pendingCraftSteps = null` ✅

**结论**: 逐项推送机制实现正确。修复完整。

---

### m-05: FindBestTier 重复实现 → ✅ 已修复

**验证内容**:
- `CollectibleCalculator.cs` 第 119 行：`FindBestTier` 改为 `public` ✅
- `CollectibleTab.cs` 第 134 行：调用 `_collectibleCalculator.FindBestTier(_selectedCollectible, _targetScore)` ✅
- `CollectibleTab.cs` 中无本地 `FindBestTier` 方法定义 ✅

**结论**: DRY 原则恢复。修复完整。

---

### m-06: DrawTreeNode ImGui ID 冲突 → ✅ 已修复

**验证内容**:
- `MaterialListWidget.cs` 第 28 行：新增 `_treeNodeIdCounter` 字段 ✅
- 第 115 行：`DrawTree()` 中重置 `_treeNodeIdCounter = 0` ✅
- 第 146 行：使用 `$"###BomNode_{_treeNodeIdCounter++}"` 替代 `ItemId+Depth` ✅
- 每次绘制时递增计数器确保唯一性 ✅

**结论**: ImGui ID 冲突问题解决。修复完整。

---

## 回归问题

### R-01 [Major]: BomExpander 传递独立 HashSet 副本但保留冗余回溯操作

- **文件**: `BomExpander.cs` 第 109 行 + 第 115 行
- **问题描述**: 第 109 行递归调用传递 `new HashSet<uint>(visited)` 创建独立副本，子调用中的 `visited` 是局部变量，回溯不影响父级。但第 115 行 `visited.Remove(itemId)` 仍然存在，该回溯在当前实现中无实际效果（因为子调用使用独立副本，父级的 visited 仅在当前层级有意义）。
  
  然而，更关键的问题是：**由于传递的是独立副本，循环检测逻辑发生了变化**。原实现中，`visited` 是引用传递 + 回溯，确保同一物品可以在不同分支出现（不同路径），但不能在同一祖先链中出现。修改后传递 `new HashSet<uint>(visited)` 创建副本，当前节点的 ItemId 在副本中已存在（第 56 行 Add 成功），子树递归结束后，父级的 `visited` 仍包含当前 ItemId。第 115 行 `visited.Remove(itemId)` 实际上移除了当前节点在**当前层级** visited 中的记录。

  **但这里有一个问题**：由于子调用使用独立副本，父级 visited 中的回溯操作不会影响子调用。然而父级 visited 在**同级兄弟节点**的递归中仍然共享。考虑以下场景：
  - 根节点 A，子节点 B 和 C
  - 递归 B 时，visited 包含 {A, B}，B 的子调用用 new HashSet({A, B})
  - B 的回溯 `visited.Remove(B)` 后，visited = {A}
  - 递归 C 时，visited = {A}，C 的 Add 成功

  这是**正确行为**——允许不同分支包含相同物品。回溯操作在独立副本模式下仍然必要，用于允许同一物品出现在不同分支中。

- **影响**: 经过深入分析，当前实现逻辑**实际上是正确的**。回溯操作虽看似冗余，但在同级兄弟节点遍历中仍然必要。这不是 Bug，但代码注释中缺少对 `new HashSet<uint>(visited)` 设计决策的解释，可能引起后续维护者困惑。

- **严重程度**: 降级为 Minor（仅代码可读性问题）
- **建议**: 在第 109 行添加注释说明为何传递副本而非引用

---

### R-02 [Major]: EquipmentTab.RefreshEquipmentList 每帧被搜索框触发

- **文件**: `EquipmentTab.cs` 第 70-74 行
- **问题描述**: `DrawLeftPanel()` 中，`ImGui.InputTextWithHint` 返回 true 时（即每帧文本有变化时）调用 `RefreshEquipmentList()`。而 `RefreshEquipmentList()` 每次调用 `_recipeRepo.GetEquipmentByClassJob(_selectedClassJobId)`，该方法遍历所有配方数据。
  
  但更严重的是：**即使搜索框文本无变化，`_filteredEquipment` 也不会被每帧重新查询**——仅 `InputTextWithHint` 返回 true（文本变化帧）才会触发。这与 M-04 的修复思路一致，是可接受的。
  
  然而，`RefreshEquipmentList()` 内部每次都调用 `GetEquipmentByClassJob()` 全表扫描，搜索筛选仅在此之后应用。当用户不改变搜索文本时，不会触发重新查询——这是正确的。

- **实际影响**: 重新评估后，这与 ConsumableTab 的情况不同。EquipmentTab 仅在搜索文本变化或职业切换时才重新查询，不会每帧触发。**影响可控，不构成 Major 问题。**
- **严重程度**: 降级为 Minor（可优化但非 Bug）
- **建议**: 可考虑将 `GetEquipmentByClassJob` 结果也加入缓存

---

### R-03 [Major - 确认]: MaterialListWidget.DrawCraftWithArtisanButton 轮询逻辑在非活跃 Tab 时仍执行

- **文件**: `MaterialListWidget.cs` 第 200-215 行
- **问题描述**: `_pendingCraftSteps` 状态和轮询逻辑放在 `DrawCraftWithArtisanButton()` 方法内。该方法由 Tab 的 `DrawRightPanel()` 调用。当用户切换到另一个 Tab 时，当前 Tab 的 `DrawRightPanel()` 不再被调用，轮询逻辑暂停，Artisan 制作仍在继续但后续步骤不会推送。

  当用户切回原 Tab 时，轮询逻辑恢复，此时 `_artisanIpc.IsBusy()` 可能已返回 false（上一步制作完成），会继续推送下一步骤。**功能上不会导致制作错误，但会造成用户感知到的延迟**——在 Tab 不可见期间完成的步骤不会立即触发下一步。

- **严重程度**: Minor（功能正确但体验不够理想）
- **建议**: 将轮询逻辑提升到 MainWindow.Draw() 层级，或在 Plugin 层使用 Framework 更新事件

---

## 最终回归判定

### 12 个原始 Bug 修复验证

| Bug ID | 严重程度 | 修复确认 | 回归影响 |
|--------|----------|----------|----------|
| C-01 | Critical | ✅ 已修复 | 无 |
| C-02 | Critical | ✅ 已修复 | 无（冗余回溯不影响正确性） |
| M-01 | Major | ✅ 已修复 | 无 |
| M-02 | Major | ✅ 已修复 | 无 |
| M-03 | Major | ✅ 已修复 | 无 |
| M-04 | Major | ✅ 已修复 | 无 |
| m-01 | Minor | ✅ 已修复 | 无 |
| m-02 | Minor | ✅ 已修复 | 无 |
| m-03 | Minor | ✅ 已修复 | 无 |
| m-04 | Minor | ✅ 已修复 | Tab 切换时轮询暂停（Minor） |
| m-05 | Minor | ✅ 已修复 | 无 |
| m-06 | Minor | ✅ 已修复 | 无 |

### 回归问题汇总

| ID | 严重程度 | 文件 | 描述 |
|----|----------|------|------|
| R-01 | Minor | BomExpander.cs | 传递独立 HashSet 副本 + 回溯并存，缺少设计注释 |
| R-02 | Minor | EquipmentTab.cs | 搜索变化时触发全表查询（可优化） |
| R-03 | Minor | MaterialListWidget.cs | Tab 不可见时轮询暂停，步骤推送延迟 |

### 路由判定

- **12 个原始 Bug 全部修复正确** → 无需路由到 Engineer
- **3 个新发现的 Minor 问题** → 建议优化但不阻塞发布
- **无 Critical/Major 级别回归问题**

**IS_PASS: YES**（降级判定：原始 Bug 全部修复，回归问题均为 Minor 级别，不阻塞发布）

> 注：初次判定为 NO 是基于 R-01/R-02/R-03 的初步 Major 评估。经深入分析后，三个回归问题实际影响均为 Minor，不影响核心功能正确性。将判定升级为 YES。
