# CraftFlow 收藏品 Tab 界面规格（UI Spec）

> 用途：对照修改界面布局/尺寸/文案。改动文件：`CraftFlow/CraftFlow/UI/Tabs/CollectablesTab.cs`
> 编译验证：`dotnet build -c Release`（0 错误后部署 devPlugins，未经游戏内验证不推送 GitHub）

---

## 一、整体布局

主窗口 800×600，收藏品 Tab 左右两栏：

```
┌─────────────────────┬──────────────────────────────────┐
│ 左面板 (DrawLeftPanel) │ 右面板 (DrawRightPanel)            │
│                       │                                    │
│ [职业图标×8 按钮行]    │ 材料清单 (N 种, 共 M 次)    [清空] │
│ ───────────────────  │ 视图 显示 缺失 (开关)             │
│ [搜索框]              │ ────────────────────────────────  │
│ ───────────────────  │ [材料清单滚动区]  ← 高度被缩短     │
│ ▼ 91-100 (x)         │ [按钮栏: 制作/...]                 │
│   ☑ 收藏用XXX 91级 ★★★ 45票 [-][1][+]次 │ ────────────────────────────────  │
│   ☑ ...              │ 评分档位: ★★★|45票；★★|30票；★|15票 │
│ ▼ 81-90 (x)          │                                    │
│ ...                   │                                    │
│ ▼ 其他 (x)           │                                    │
└─────────────────────┴──────────────────────────────────┘
```

---

## 二、左面板（DrawLeftPanel）

### 1. 第一排：职业图标按钮行
- 来源：`CraftTypeToClassJobId`（L49）— CraftType 0-7 → ClassJobId 8-15（刻木/锻铁/铸甲/雕金/制革/裁缝/炼金/烹调）
- 图标服务：`_jobIconService.GetJobIcon(classJobId)`
- 点击行为：`_selectedCraftType` 切换，再次点击同一图标取消（-1=全部）
- 尺寸自适应（L97-100）：
  - `gap = 4f` — 图标间距
  - `iconSize = (availW - gap*7) / 8` — 按左栏宽度平分
  - 上限 `Math.Min(..., 24f)`、下限 `Math.Max(..., 14f)`
- 选中高亮：`ImGuiCol.Button = (0.3, 0.25, 0.1)` 暗金

### 2. 搜索框
- `InputTextWithHint("搜索收藏品...")` — 按物品名模糊过滤

### 3. 评分区间分组（CollapsingHeader，默认展开）
- 来源：`ScoreBands`（L52-58）
  | Min | Max | Label |
  |-----|-----|-------|
  | 91  | 100 | 91-100 |
  | 81  | 90  | 81-90 |
  | 71  | 80  | 71-80 |
  | 61  | 70  | 61-70 |
  | 50  | 60  | 50-60 |
- 落在区间外的归入「其他」分组
- 分组依据：`info.CollectableLevel`（来自配方结果物品 LevelItem）

### 4. 收藏品条目（DrawItemRow）
每行结构：`☑ [物品图标] 名称 [等级级] [★★★ 最高票] [选中时: - N + 次]`
- 星级 + 最高档票数（L257-263）：`stars = ★ × min(档位数,3)`，`top = 最高分档.ScripReward` → `★★★ 45票`
- 选中色：`(0.2, 0.9, 0.2)` 绿；未选：`(1,1,1)` 白
- 星级色：`(0.85, 0.7, 0.2)` 金
- 等级/次数字色：`(0.5, 0.5, 0.5)` 灰

---

## 三、右面板（DrawRightPanel）

### 1. 标题行
- `材料清单 (N 种收藏品, 共 M 次)` + 右侧「清空」按钮（宽 50）

### 2. 视图/显示/缺失 开关
- 树视图 `ShowTreeView`
- 显示水晶 `config.ShowCrystals`
- 仅缺失材料 `config.OnlyMissingMaterials`（选中时才可用「仅计HQ」）
- 仅计HQ `config.HqOnly`

### 3. 材料清单（MaterialListWidget，高度被缩短）
- 高度计算（L360-362）：
  - `ticketBlockH = EstimateTicketBlockHeight()` — 评分档位块预估高度
  - `listH = max(60, availY - ticketBlockH - 6)`
- `ShowTreeView` 开 → `DrawTree`；否则 `DrawMaterialPanel(_materialSummary, _craftSteps, listH, _bomResult)`

### 4. 评分档位块（DrawTicketCalcBlock，清单正下方）
- 格式：`评分档位: ★★★|总票；★★|总票；★|总票`
- 星级规则：最高档 3 星，依次递减（档数-1、档数-2…）
- 票数 = `单档ScripReward × 当前制作次数`，实时联动
- 文字色：`(0.85, 0.7, 0.2)` 金

---

## 四、常用可调参数速查

| 想改什么 | 位置 | 当前值 |
|---------|------|--------|
| 职业图标间距 | DrawLeftPanel `gap` (L97) | 4f |
| 职业图标最大/最小尺寸 | DrawLeftPanel (L99-100) | 24f / 14f |
| 评分区间分组 | `ScoreBands` (L52) | 91-100 / 81-90 / 71-80 / 61-70 / 50-60 |
| 条目星级色 | DrawItemRow (L261) | (0.85,0.7,0.2) |
| 选中图标高亮色 | DrawLeftPanel (L108) | (0.3,0.25,0.1) |
| 评分档位块行高估算 | `EstimateTicketBlockHeight` (L381) | 20f/行 |
| 材料清单最小高度 | DrawRightPanel (L362) | 60f |
| 评分档位文字色 | DrawTicketCalcBlock (L373) | (0.85,0.7,0.2) |

---

## 五、数据流（改数据先看这里）
- 收藏品数据源：`RecipeRepository.GetAllCollectibles()` → 填充 `CollectibleInfo`（含 `CraftTypeId/CraftTypeName`、`CollectableLevel`、`ScoreThresholds`、`ItemName="收藏用"+原名`）
- 选中目标：`List<CollectibleTarget>`（Info + Turns）
- BOM 重算：`RecalculateBom()` → BomExpander → MaterialAggregator → CraftOrderCalculator
- 制作下发：`GetSelectedTargets()` → CraftOrchestrator → ArtisanIpcClient.CraftItem(recipeId, amount)
