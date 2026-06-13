# CraftFlow

FF14（最终幻想XIV）Dalamud 生产辅助插件，面向国服 XIVLauncherCN。

## 功能

- **装备制作**：按角色分组/职业/槽位三级选择，一键添加整套装备到制作清单
- **消耗品制作**：食物/药品同理，快速展开 BOM 并汇总材料
- **收藏清单**：保存/加载常用制作组合，跨游戏会话持久化
- **推荐套装**：内置 770HQ / 750HQ 等推荐预设，动态查询当前版本最佳装备
- **BOM 展开**：递归展开配方材料树，聚合汇总去重
- **GBR 联动**：一键将采集需求推送到 GatherBuddyReborn
- **Artisan 联动**：一键触发 Artisan 制作，独立进度弹窗实时跟踪，支持暂停/继续/放弃
- **制作进度持久化**：崩溃或断线后重启可恢复制作进度

## 运行环境

| 依赖 | 版本 |
|------|------|
| Dalamud API | 15 |
| .NET | 10 |
| 游戏客户端 | 国服 XIVLauncherCN |
| 依赖插件 | Artisan（制作）、GatherBuddyReborn（采集） |

## 编译

```bash
git clone https://github.com/ijnokmsc/CraftFlow.git
cd CraftFlow/CraftFlow
dotnet build
```

编译产物自动部署到：
```
C:\Users\你的用户名\AppData\Roaming\XIVLauncherCN\devPlugins\CraftFlow\
```

> 如需手动部署，将 `bin\Debug\` 下所有文件复制到上述目录。

## 安装到游戏

### 方式一：插件仓库（推荐）

1. 打开游戏，输入 `/xlsettings`
2. 左侧菜单 → **插件仓库** → **添加仓库**
3. 粘贴以下地址：
   ```
   https://raw.githubusercontent.com/ijnokmsc/CraftFlow/main/repo.json
   ```
4. 确定 → 重新整理插件列表
5. 搜索 **CraftFlow** → 安装

### 方式二：手动安装

1. 从 [Releases](https://github.com/ijnokmsc/CraftFlow/releases/latest) 下载 `latest.zip`
2. 解压到 `%APPDATA%\XIVLauncherCN\devPlugins\CraftFlow\`
3. 游戏内 `/xlplugins` → 开发者插件 → 刷新并启用

### 使用命令

| 命令 | 说明 |
|------|------|
| `/cf` | 打开/关闭主窗口 |
| `/craftflow` | 同上（完整命令） |

## 项目结构

```
CraftFlow/
├── CraftFlow/               # 插件主项目（C#）
│   ├── CraftFlow.csproj
│   ├── Plugin.cs            # 插件入口
│   ├── Data/               # 数据模型 / 游戏数据仓储
│   ├── Services/            # BOM 展开 / 材料聚合 / 制作顺序 / 预设
│   ├── IPC/                # Artisan / GBR IPC 封装
│   └── UI/                # ImGui 界面（Tab / Widget）
│       ├── Tabs/           # 装备 / 消耗品 / 收藏 / 推荐
│       └── Widgets/        # 材料面板 / 进度条 / 状态栏
├── docs/                   # 设计文档（PRD / 架构）
└── README.md
```

## 文档

- [产品需求文档（PRD）](docs/PRD.md)
- [系统架构文档](docs/ARCHITECTURE.md)

## 许可证

MIT
