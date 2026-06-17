## CraftFlow v0.2.3 — 物品图标 + 修复

### 新增
- 装备选择列表：每个装备名称前显示对应物品图标 (20x20)
- 食物药品列表：每个物品名称前显示对应物品图标
- 材料汇总表：每行材料名称前显示对应物品图标
- BOM 树视图：每个节点名称前显示对应物品图标
- ItemIconService：通过 Lumina Item.Icon + TextureProvider 异步加载游戏内图标纹理

### 修复
- 修复 EquipmentSlotGroupWidget 构造时 NullReferenceException（_itemIconService 赋值顺序）

### 变更
- 新增 CraftFlow.Tests 测试项目框架
- 主界面隐藏通知日志面板
