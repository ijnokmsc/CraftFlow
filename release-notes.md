## CraftFlow v0.2.2 — Bug 修复版本

### 🐛 Bug 修复

| # | 文件 | 描述 |
|---|------|------|
| 1 | `MaterialAggregator.cs` | 水晶过滤改用精确名称集合，避免 `Contains("crystal")` 误杀 Crystal Glass 等物品 |
| 2 | `RecipeRepository.cs` | `GetMaterialSource` 新增 Drop（怪物/副本掉落）来源判断 |
| 3 | `CollectibleCalculator.cs` | `CalculateMaterials` 改用 `BomExpander` 递归展开 BOM（此前仅一层） |
| 4 | `CraftFlow.Tests` | 修复测试项目 TargetFramework + 补 `using Xunit` |
| 6 | `RecipeRepository.cs` | `IsGatherableItem` 添加版本维护注释 |
| **7a** | `InventoryHelper.cs` | **仅缺失材料** 共享中间产物库存跨分支重复扣减（核心 Bug） |
| **7b** | `MaterialListWidget.cs` | 树视图库存跨分支追踪 |
| **7c** | `MaterialListWidget.cs` | 树视图叶节点改用分支 scale×Quantity（修复前所有同 ItemId 叶节点显示相同全局值） |
| **7d** | `InventoryHelper.cs` + `MaterialListWidget.cs` | 成品（Depth==0）不扣背包库存 |

### ✨ 新增
- 测试项目框架 (`CraftFlow.Tests/`)

### 🔧 变更
- 主界面隐藏通知日志面板

### 安装方式
1. 下载 `CraftFlow-v0.2.2.zip`
2. 解压到 `%APPDATA%\XIVLauncherCN\devPlugins\CraftFlow\`
3. 在游戏内输入 `/xlplugins` → 开发者插件 → 刷新并启用 CraftFlow
