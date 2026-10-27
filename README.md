# Chicken Killer

> 一个用 **C++20 + Unity 6.3** 实现的 3D 动作战斗原型。
> 重点不是“做一个完整的游戏”，而是验证一套可测试、可配置、可复现的动作战斗架构：
> **C++ 权威战斗核心 + C# Unity 表现/输入层**。

![status](https://img.shields.io/badge/status-phase%201%20prototype-orange)
![engine](https://img.shields.io/badge/Unity-6000.3.26f1-black)
![language](https://img.shields.io/badge/C%2B%2B-20-blue)
![platform](https://img.shields.io/badge/platform-Windows%20x64%20%7C%20DX11-lightgrey)

---

## 目录

- [项目简介](#项目简介)
- [功能特性](#功能特性)
- [技术栈](#技术栈)
- [架构](#架构)
- [目录结构](#目录结构)
- [快速开始](#快速开始)
- [操作说明](#操作说明)
- [测试与验证](#测试与验证)
- [配置](#配置)
- [设计要点](#设计要点)
- [已知限制](#已知限制)
- [开发状态](#开发状态)
- [Git LFS 与美术资产](#git-lfs-与美术资产)
- [文档](#文档)
- [License](#license)

---

## 项目简介

Chicken Killer 是一个平面场景的 3D 动作战斗原型：

- 玩家和敌人都是程序化生成的圆柱角色；
- 双方各有一把程序化生成的剑；
- 支持三段普攻、长按重击、格挡、闪避、Q/E/R/F 技能与破防 Buff；
- 命中判定使用 **武器胶囊 vs 身体胶囊**；
- 战斗规则运行在 C++ 原生核心中，Unity 侧只负责输入、移动、表现和 HUD；
- 所有玩法数值集中在 `Config/CombatPrototype.json`；
- 每个模块都有原生/EditMode/PlayMode 测试与 Play Mode 截图验证。

本项目是第一阶段原型，不是商业发行版本；没有敌人 AI、死亡流程、正式动画和音频资源。

---

## 功能特性

### 战斗

- 固定 **60Hz** 逻辑 tick，战斗与渲染帧率解耦；
- 三段普攻连段（`normal_1 → normal_2 → normal_3`）；
- 长按左键 **0.22 秒**触发重击；
- 按住空格格挡，格挡期间移动速度降低；
- 右键/左 Shift 闪避，包含 **18 帧无敌**与冷却；
- Q/E/R/F 四个技能，Q 命中后降低敌人防御（破防 Buff）；
- 伤害公式、Active 帧、冷却、连段窗口、输入缓冲、命中锁存全部由 C++ 核心处理；
- 武器胶囊与剑的渲染使用同一个 pivot，保证“看到哪里就打到哪里”。

### 数据与扩展

- `Config/CombatPrototype.json` 是唯一玩法数值来源；
- 配置加载器做严格字段校验和跨字段校验；
- 连段步数、武器数量、武器/装备槽数量均可通过 JSON 扩展；
- 已通过临时配置验证 **4 段连段** 与 **4 武器/4 装备槽**，无需改代码。

### 工程与质量

- C++ 原生核心可脱离 Unity 独立测试；
- 32 个原生测试、167 个 checks；
- 26 个 EditMode 测试、8 个 PlayMode 测试；
- Gate 自动化：虚拟输入设备 + 确定性输入 harness + 1280×720 截图 + `summary.json` expected/observed；
- Windows x64 Player：Mono 冒烟构建 + IL2CPP 发布构建，仅 Direct3D11；
- HUD 中文化，动态加载系统中文字体。

---

## 技术栈

| 层 | 技术 |
|---|---|
| 原生核心 | C++20、CMake 3.24+、Visual Studio 2022 x64 |
| 引擎 | Unity 6.3 LTS（6000.3.26f1）、URP 17.3.0 |
| 脚本/表现 | C#、Input System、IMGUI、CharacterController |
| 跨语言 | C ABI、P/Invoke、blittable 结构体、IL2CPP |
| 平台 | Windows x64、Direct3D11 |
| 测试 | CTest、Unity Test Framework（EditMode/PlayMode）、NUnit |
| 工具 | PowerShell 5.1、Git、Git LFS |

---

## 架构

```text
Config/CombatPrototype.json
        │
        ▼
CombatConfigLoader（Newtonsoft.Json + 严格校验 + FNV 哈希）
        │
        ▼
CombatConfig DTO
        │
        ├──────────────► CombatPrototypeBootstrap（相机/灯光/地面/角色/HUD）
        ▼
NativeCombatBridge（P/Invoke）
        │  CkCommand / CkActorFrame
        ▼
ck_combat.dll（C++20 权威核心）
  技能状态机 / 冷却 / 连段 / 输入缓冲 / 格挡 / 闪避 /
  Buff / 胶囊命中 / 伤害公式 / 事件 / 快照
        │
        ▼
CkEvent / CkActorSnapshot
        │
        ├──────────────► CombatDirector（事件/计数/HUD 状态）
        └──────────────► PrototypeHud（IMGUI）
```

### 权威边界

| 内容 | 负责方 |
|---|---|
| 技能阶段、冷却、连段、输入缓冲 | C++ |
| 格挡/闪避/无敌帧、命中查询、伤害、Buff | C++ |
| 玩家位置/朝向/移动、相机相对输入 | C# |
| 剑的姿态、武器胶囊、画面表现 | C# |
| HUD、背包/地图占位面板 | C# |
| 配置解析与校验 | C# |

> C# 不计算伤害/命中，C++ 不依赖 UnityEngine。

---

## 目录结构

```text
ChickenKiller/
├─ Config/
│  └─ CombatPrototype.json          # 唯一玩法数值来源
├─ Native/
│  ├─ include/ck/ck_combat.h        # C ABI
│  ├─ src/core/combat_core.{h,cpp}  # 权威战斗规则
│  ├─ src/c_api.cpp                 # C ABI 适配
│  ├─ tests/test_main.cpp           # 32 个原生测试
│  └─ CMakeLists.txt
├─ Assets/
│  ├─ Plugins/Windows/x86_64/       # ck_combat.dll 构建输出（gitignore）
│  ├─ Scenes/CombatPrototype.unity
│  └─ Game/
│     ├─ Domain/                    # DTO/配置/接口，不依赖 UnityEngine
│     ├─ Native/                    # P/Invoke 桥
│     ├─ Runtime/                   # Bootstrap/Input/Combat/Actors/View/UI/Automation
│     ├─ Editor/                    # Gate/构建/截图/配置校验
│     └─ Tests/                     # EditMode + PlayMode
├─ Tools/
│  ├─ build_native.ps1
│  ├─ Run-Gate.ps1
│  └─ Run-Player-Gate.ps1
├─ Docs/                            # 本地文档（gitignore）
└─ TestArtifacts/                   # 截图/证据（gitignore）
```

---

## 快速开始

### 1. 环境要求

- Windows 10/11 x64
- Unity **6000.3.26f1**（Unity 6.3 LTS）
- Visual Studio 2022，安装 **Desktop development with C++**
- CMake 3.24+
- PowerShell 5.1+
- Git + Git LFS（仓库包含 LFS 资源）

### 2. 克隆仓库

```powershell
git lfs install
git clone <repo-url>
cd ChickenKiller
```

> 如果克隆后 `Assets/TutorialInfo/Icons/URP.png` 只是指针文件，执行：
> `git lfs pull`

### 3. 构建原生插件

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/build_native.ps1
```

脚本会：

- 用 CMake 生成 VS2022 x64 工程；
- 构建 `ck_combat.dll`；
- 复制到 `Assets/Plugins/Windows/x86_64/`；
- 运行 32 个原生测试。

也可以手动构建：

```powershell
cmake -S Native -B Native/build -G "Visual Studio 17 2022" -A x64
cmake --build Native/build --config Debug
ctest --test-dir Native/build -C Debug --output-on-failure
```

### 4. 在 Unity 中运行

1. 用 Unity Hub 打开项目根目录。
2. 打开场景：
   `Assets/Scenes/CombatPrototype.unity`
3. 点击 **Play**。

> 编辑模式下 Game View 可能偏暗/空白，这是正常的：
> 地面、角色、剑、HUD 都是 `CombatPrototypeBootstrap` 在 Play Mode 中程序化生成的。

建议 Game View 设置：

```text
Aspect: 16:9 或 1280x720
Scale: 1x
Play Focused（或保持 Play Maximized + 16:9）
```

### 5. 构建 Windows Player

菜单：

```text
Tools → Prototype → Build → Windows64 Mono (M0 smoke)
Tools → Prototype → Build → Windows64 IL2CPP (M7)
```

命令行：

```powershell
$unity = "C:\Path\To\Unity.exe"

& $unity -batchmode -projectPath . `
  -executeMethod CK.Editor.BuildScript.BuildMonoCli `
  -quit -force-d3d11 -logFile TestArtifacts/build-mono.log

& $unity -batchmode -projectPath . `
  -executeMethod CK.Editor.BuildScript.BuildIl2CppCli `
  -quit -force-d3d11 -logFile TestArtifacts/build-il2cpp.log
```

产物：

```text
Build/Win64-Mono/ChickenKiller.exe   # Mono，开发冒烟
Build/Win64/ChickenKiller.exe        # IL2CPP，DX11
```

---

## 操作说明

| 输入 | 行为 |
|---|---|
| `WASD` | 相机相对移动 |
| 左键点按 | 三段普攻 |
| 左键长按 ≥ 0.22s | 重击（只触发一次） |
| 右键 / 左 `Shift` | 闪避（18 帧无敌、30 帧冷却） |
| 按住空格 | 格挡（移动速度 30%） |
| `Q` | 破防技能（DEF -50，600 帧） |
| `E` / `R` / `F` | 占位技能（有独立冷却） |
| `1`–`4` | 物品栏槽位（空槽反馈） |
| `Tab` | 循环切换武器槽（动作/格挡中拒绝） |
| `B` | 背包/装备/武器占位面板 |
| `M` | 地图占位面板 |

---

## 测试与验证

### 原生测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/build_native.ps1
```

预期：

```text
32 tests, 167 checks, 0 failures
```

### EditMode / PlayMode

```powershell
$unity = "C:\Path\To\Unity.exe"

& $unity -batchmode -projectPath . -runTests -testPlatform EditMode `
  -testResults TestArtifacts/editmode.xml -logFile TestArtifacts/editmode.log -force-d3d11

& $unity -batchmode -projectPath . -runTests -testPlatform PlayMode `
  -testResults TestArtifacts/playmode.xml -logFile TestArtifacts/playmode.log -force-d3d11
```

预期：

- EditMode：**26/26**
- PlayMode：**8/8**

### 单 Gate 自动化

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Run-Gate.ps1 -Module M3
```

> `Tools/Run-Gate.ps1` 里的 Unity 路径是按开发机硬编码的；在其他机器上运行前，
> 将脚本顶部的 `$unity` 改成自己的 Unity 可执行文件路径。

Gate 会输出：

```text
TestArtifacts/<Module>/<timestamp>/
├─ summary.json
├─ screenshots/*.png
├─ editmode-results.xml
├─ playmode-results.xml
├─ native-results.xml
├─ native-tests.log
├─ unity-console.log
└─ DONE.txt
```

### Player Gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Run-Player-Gate.ps1 `
  -Build IL2CPP -Module M7
```

---

## 配置

唯一数值来源：

```text
Config/CombatPrototype.json
```

主要字段：

```jsonc
{
  "tickRate": 60,
  "combat": {
    "defenseConstant": 100,
    "minDefense": 0,
    "heavyHoldSeconds": 0.22,
    "comboWindowSeconds": 0.35,
    "inputBufferFrames": 10,
    "dodgeInvulnFrames": 18,
    "dodgeDistance": 4.0,
    "blockMoveMultiplier": 0.3
  },
  "actors": {
    "player": { "maxHealth": 1000, "attackPower": 100, "defense": 50, "moveSpeed": 5.0 },
    "enemy":  { "maxHealth": 1000, "attackPower": 100, "defense": 50, "moveSpeed": 0.0 }
  },
  "loadout": {
    "weaponSlots": 3,
    "equipmentSlots": 3,
    "inventorySlots": 50,
    "itemBarSlots": 4
  }
}
```

伤害公式：

```text
damage = roundAwayFromZero(
  attackPower × abilityMultiplier × 100 / (effectiveDefense + 100)
)
```

当前数值：

- 普攻：`100 × 1.0 × 100 / 150 = 67`
- 重击：`100 × 1.5 × 100 / 150 = 100`
- Q：`100 × 0.5 × 100 / 150 = 33`
- Q 破防后：普攻 100，重击 150

修改配置后重新进入 Play Mode；HUD 会显示 JSON 的 `config` 哈希和原生 `native` 哈希，便于确认加载的是哪份数据。

---

## 设计要点

### 1. C++ 权威核心

- 技能状态机、Active 帧、冷却、连段、输入缓冲、格挡/闪避、Buff、命中、伤害全部在 C++；
- Unity 只提交命令与胶囊，读取事件与快照；
- 规则可以脱离 Unity 用 CTest 测试。

### 2. 数据驱动

- 技能/阶段/倍率/冷却/连段/武器/Buff/槽位全部来自 JSON；
- 加载器拒绝静默回退；
- 仅改 JSON 即可验证 4 段连段和 4 槽位扩展。

### 3. 表现与判定一致

- 剑的 `WeaponPivot` 同时驱动渲染和武器胶囊；
- 身体胶囊尺寸来自配置；
- 不需要维护第二套“判定距离”。

### 4. 命中锁存

- 技能 Active 窗口通常有多帧；
- `hitLatch` 保证一次技能激活期间对同一目标只结算一次命中；
- 下一次技能激活时清空，因此连段每段都能独立命中。

### 5. 确定性自动化

- 固定 60Hz；
- Gate 通过虚拟 Input System 设备注入输入；
- 自动化 harness 覆盖轮询状态，避免编辑器清空合成设备；
- 截图 + expected/observed + 测试 XML 形成证据链。

---

## 已知限制

- 敌人没有 AI/移动/选招，固定每 120 帧挥剑；
- 没有死亡/结算流程，HP 归零只显示提示；
- 没有正式动画、音频资源；角色/剑为程序化几何；
- 背包/装备/地图是占位 UI，不支持拖拽/拾取/穿脱；
- `weapons.damageMultiplier` 尚未进入伤害公式；
- 武器 `hitVolumes.radiusScale` 尚未参与剑半径；
- 没有网络/多人/回滚；
- Gate 证据与 `Docs/` 默认不提交，需本地生成/单独上传；
- 当前未附带开源许可证。

---

## 开发状态

| 模块 | 内容 | 状态 |
|---|---|---|
| M0 | 基线、场景、DX11、原生 DLL、HUD | PASS |
| M1 | 数据驱动 Actor/HUD/输入 | PASS |
| M2 | WASD/身体碰撞 | PASS |
| M3 | 三段/重击/命中 | PASS |
| M4 | 敌人攻击/格挡/闪避 | PASS |
| M5 | QERF/Buff/冷却 | PASS |
| M6 | 武器槽/背包/装备/物品栏 | PASS |
| M7 | 集成/IL2CPP Player/稳定性 | PASS |

测试现状：

```text
Native   32 / 32
EditMode 26 / 26
PlayMode  8 / 8
```

---

## Git LFS 与美术资产

仓库已经包含 `.gitattributes`，对常见二进制资产启用 Git LFS：

```text
*.fbx / *.obj / *.blend / *.max / *.ma / *.mb
*.png / *.jpg / *.jpeg / *.tga / *.psd / *.exr / *.hdr
*.wav / *.mp3 / *.ogg / *.flac
*.mp4 / *.mov / *.webm
*.ttf / *.otf
*.dll / *.pdb / *.so / *.dylib
```

使用要求：

```powershell
git lfs install
git lfs pull
git lfs ls-files
```

注意事项：

- **在第一次提交美术资产之前**就应安装 LFS；否则大文件会进入普通 Git 历史，后续清理需要重写历史；
- 不要把 `.unity`、`.prefab`、`.asset`、`.mat` 等 Unity YAML 文本放进 LFS，否则无法正常 diff/合并；
- Unity 的 `.meta` 必须和资产一起提交；
- GitHub 免费 LFS 有存储/流量额度，正式项目如果资产很大，建议使用 Git LFS 数据包或外部资产管理；
- 不要用 GitHub 网页直接拖拽大二进制文件，使用 `git push`。

---

## 文档

仓库默认把 `Docs/` 和 `TestArtifacts/` 放在 `.gitignore` 中。若需要公开设计文档，可选择性提交：

```powershell
git add -f "Docs/Learning-Guide.md"
git add -f "Docs/Report/游戏设计第一阶段-实现基本逻辑-任务报告.md"
git add -f "Docs/Report/游戏开发经验总结.md"
git add -f "Docs/Resume/ChickenKiller-项目简历文案.md"
```

不建议提交：

- `Docs/3D动作游戏项目任务书.md`（除非确认可以公开）；
- `TestArtifacts/`（大量截图、日志、绝对路径）；
- `Build/`（可执行文件，建议放 GitHub Releases）。

---

## License

当前项目尚未添加开源许可证。

在没有添加 LICENSE 之前，默认保留所有权利。如果计划公开仓库，建议选择 MIT / Apache-2.0 等许可证，并在仓库根目录添加 `LICENSE` 文件；如果包含第三方字体、美术或声音资源，还需要确认其授权允许再分发。

---

## 致谢

- Unity URP / Input System / Test Framework
- CMake / Visual Studio 2022
- 所有参与验证与截图复核的工具与流程
