# Nightreign Local Trainer

一个面向授权本地测试的非官方 Windows x64 修改器，带游戏窗口叠加功能栏。当前分享版本：**v0.4 experimental**。

An unofficial Windows x64 local testing trainer with a borderless overlay panel. This is an experimental, build-specific tool. It does not include game binaries, assets, save files, anti-cheat/DRM bypasses, or third-party cheat tables.

## 兼容范围

**目前仅支持一个经过本地验证的可执行文件构建，并非通用版本。**

- 文件版本：`1.3.2.0`
- 必须匹配的 `nightreign.exe` SHA-256：
  `e3a60b675d01917003b56ce5a6990c61a17cab22d64b4ef1f140cfcb3d7e593c`
- 即便显示相同游戏版本，程序哈希不同也会拒绝连接。
- 仓库与 Release 不提供游戏程序或使其匹配该哈希的补丁。
- 功能栏支持**无边框窗口／窗口模式**；独占全屏未验证。它是外部叠加面板，不是注入 DirectX 的游戏内界面。

## 下载与安装

从本仓库 Releases 下载 `NightreignTrainer-v0.4-windows-x64.zip`，将其中的 `NightreignTrainer` 文件夹放在游戏根目录，让它与已有的 `Game` 文件夹并列：

```text
<game-root>/
  Game/
    nightreign.exe             # 用户自行持有的游戏文件
  NightreignTrainer/
    NightreignTrainer-v0.4.exe
    Start.cmd
    README.md
```

1. 先完全退出旧版修改器。
2. 手动运行 `Start.cmd` 或 `NightreignTrainer-v0.4.exe`，程序默认隐藏在系统托盘。
3. 启动游戏并进入角色场景。所有功能初始关闭。
4. 游戏获得焦点时按 **F1** 打开功能栏，勾选需要的功能。
5. **F1** 或 **Esc** 收起；完全退出请用功能栏右上角或托盘中的“退出”。

如果 F1 仍在切换锁血，说明旧版仍占用按键，需先退出旧版。

## 快捷键

| 按键 | 功能 |
| --- | --- |
| F1 | 显示／隐藏功能栏 |
| Esc | 隐藏功能栏并返回游戏 |
| F11 | 定时锁血 |
| F2 | FP 免消耗并恢复 |
| F3 | 精力免消耗并恢复 |
| F4 | 关闭全部持续功能 |
| F5 | 本机角色免伤＋防死亡，实验 |
| F6 | 原生高伤害／秒杀标志，实验 |
| F7 | 角色技能无冷却 |
| F8 | 绝招能量不消耗 |
| F9 | 道具不消耗，实验 |
| F10 | 冻结当前出击的昼夜时钟，实验 |

功能栏另提供局内卢恩数值修改。首次实际改变卢恩前，会备份找到的 Nightreign 存档到程序目录下的 `save-backups/`。

收起功能栏不会关闭已开启功能。切到其他应用时自动隐藏面板并释放快捷键。完全退出时，程序尝试恢复自己记录的原生标志、角色状态位和时间倍率。

## 限制与使用范围

- 仅用于自己持有且获准修改的本地测试环境，不用于未经其他玩家同意的会话。
- 部分引擎调试标志具有全局作用范围，可能影响其他游戏内角色。
- 定时锁血不保证抵挡瞬间致死攻击；免伤／防死不代表所有坠落、脚本死亡及首领机制都已验证。
- 冻结时钟可能阻止缩圈和任务流程推进，继续流程前应关闭；它不等同于关闭全部黑雾、天气和怪物刷新。
- 未实现遗物／祝福生成、全地图透视、移速／跳跃修改、假通关或奖励校验绕过。
- 强制结束进程无法执行恢复；正常退出才会运行回滚逻辑。
- 项目不附带游戏文件，不提供绕过许可、DRM、反作弊或官方结算校验的功能，也不保证平台或游戏服务允许某一种用途。

## 从源码构建

Windows x64，使用系统 .NET Framework 4 C# 编译器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

产物为当前目录下 `NightreignTrainer-v0.4.exe`。按上面的目录结构安装后再运行。

运行不连接游戏的测试：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
```

参见 [验证范围](docs/VALIDATION.md)、[研究参考](THIRD_PARTY_NOTICES.md) 和 [问题报告](SECURITY.md)。

## 项目状态与权利

项目与 FromSoftware、Bandai Namco 或 GitHub 没有官方隶属关系。游戏名称和第三方资料的权利属于相应权利人。公开仓库不是对平台审核、法律状态或游戏使用条款的保证。

本项目自有源码与文档采用 [MIT License](LICENSE)，允许在保留许可证与版权声明的前提下使用、修改和再分发。该许可证不授予游戏文件、游戏名称或其他第三方材料的权利，参见 [研究参考与第三方说明](THIRD_PARTY_NOTICES.md)。
