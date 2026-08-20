# Arcane Puck VR：Unity / OpenXR / URP 迁移规范

本项目的迁移目标是复用玩法规则、参数、资产坐标规范和手感验收标准；不是把 Three.js、DOM、WebXR 或 Rapier 的运行时代码逐行转换为 Unity C#。Unity 版本应在其原生运行层中重建输入、物理、渲染与 UI。

## 先做什么

建议按以下顺序推进，避免“先换美术、后发现球感变了”：

1. **冻结跨引擎契约。** 导出并版本化球桌、球门、圆盘、击槌、AI、得分、金球和复位配置；为关键规则写纯数据测试用例和可回放测试轨迹。
2. **在 Unity 建立最小可玩垂直切片。** 使用 Unity LTS、OpenXR Plugin、XR Interaction Toolkit（或等效 OpenXR 输入层）和 URP；先放入灰盒球桌、圆盘、击槌、两球门、记分 UI。
3. **复刻手感。** 使用同一单位、边界、半场限制、击球冲量、圆盘阻尼、复位条件和目标分数。记录并比对发球、直击、斜击、边反弹、蓄力和 AI 拦截的结果。
4. **替换运行层。** 当前桌面灰盒已用 Unity `Rigidbody`、配置驱动的凸圆柱 `MeshCollider` 和固定步长重建，并通过 EditMode/PlayMode 回归；下一步接入 OpenXR action、手柄姿态和 haptic impulse，再用 URP 材质/VFX Graph 完成主题表现。
5. **最后接入正式美术和音频。** 资源只绑定到既有锚点与视觉层；不让网格碰撞、缩放或骨骼层级改变规则碰撞体。

## 跨引擎空间契约

Unity 和 Three.js 都可用同一右手游戏语义：

| 项目 | 固定约定 |
| --- | --- |
| 长度 | 米（m），不得用模型单位或像素充当规则单位。 |
| 质量 / 时间 | kg / s；力为 N，冲量为 N·s，角度为 rad。 |
| 游戏平面 | `(x, z)`；`+X` 为玩家面向 AI 时右方，`+Z` 指向 AI，`+Y` 向上。 |
| 球桌原点 | `TABLE_ANCHOR`：可玩桌面表面的几何中心。游戏局部 `(0,0,0)` 即此点。 |
| 两侧球门 | 玩家球门在 `z = -table.length / 2`，AI 球门在 `z = +table.length / 2`。球门锚点是球门线中点。 |
| 玩家半场 | `z <= 0`；AI 半场为 `z >= 0`。半场边界以击槌接触圆的边缘约束，而不只约束模型原点。 |
| 世界变换 | `world = tableAnchor + gameLocal`，禁止对游戏局部坐标进行非 1 的缩放。 |
| 视图/校准 | 校准只改变 `tableAnchor` / XR origin 的刚体位姿，不改比分、物理参数或局部玩法坐标。 |

在 Unity 中，建议建立一个无缩放的 `TableAnchor` GameObject，所有玩法刚体与桌面视觉内容都作为它的子物体。`TableAnchor` 的 `Transform.position` 位于可玩表面中心，而非桌腿、模型包围盒中心或地面中心。其缩放必须是 `(1, 1, 1)`；模型尺度在导入阶段或子级视觉对象上处理。

`TableAnchor` 与地面高度的关系由 `table.height` 配置确定。玩家站在负 Z 侧看向正 Z；XR 原点放置在舒适的玩家侧站位，不应通过持续相机移动来“追踪”球桌。

## 资产交付规范

推荐正式模型以 glTF/GLB 提供，贴图使用可控的 PBR 工作流。每项资源必须附上尺寸、轴向、单位和锚点说明；美术预览中的任意缩放都不能替代这些元数据。

| 槽位 | 根节点 / 锚点 | 朝向与约束 | 碰撞关系 |
| --- | --- | --- | --- |
| `TABLE_MODEL` | `TABLE_ANCHOR`，可玩表面中心 | 长轴为 Z，玩家端负 Z | 仅视觉；桌边和球门由独立 Box/compound collider 生成。 |
| `MALLET_MODEL` | 接触盘中心 | `+Z` 指向 AI；底部接触面与锚点同高 | 仅视觉；物理使用配置半径的圆柱/圆盘。 |
| `PUCK_MODEL` | 圆盘几何中心 | 对称模型无需面向约束 | 仅视觉；物理使用配置半径/高度的圆柱。 |
| `GOAL_MODEL` | 球门线中心 | 按正/负 Z 端实例化；传送面垂直 Z 轴 | 仅视觉；得分用规则体积/穿线测试，非网格碰撞。 |
| `ARENA_MODEL` | `TABLE_ANCHOR` 在地面上的投影 | 让桌面保持主视觉焦点 | 禁止给玩法刚体增加意外碰撞。 |
| `VFX_THEME` | 每个效果明确绑定击槌、圆盘、球门或桌面锚点 | 以本地米为尺度 | 无碰撞、无规则副作用。 |
| `SFX_THEME` | 事件发射点（击球、边、球门、中心） | 使用空间音频但保持舒适音量 | 无规则副作用。 |

导入校验项：`1 Unity unit = 1 m`、根节点无未经说明的偏移、模型没有负缩放、可视网格与 collider 不互相依赖、LOD 和粒子不会改变游戏尺寸。需要镜像的 AI 侧球门应通过明确的旋转/镜像策略实现，并在视觉上验证符文/文字方向。

## 规则和物理等价要求

以下规则应由 Unity 中独立的纯逻辑服务管理，并可由 PlayMode/EditMode 测试覆盖：

- 成功得分：圆盘的整个圆形截面进入目标球门开口，且圆盘完全通过对方球门线；不能因为边缘擦线提前得分。
- 胜利：常规先得 7 分；比分 `6:6` 后下一分立刻结束（金球）。
- 恢复：圆盘飞出台面、陷入无效碰撞，或线速度持续低于配置阈值超时，均回到中线发球状态。
- 半场：玩家击槌碰撞体不可越过中线，AI 同样受另一侧限制。
- 蓄力：扳机按住累积至上限；下一次有效击槌—圆盘接触只消耗一次并施加配置的额外冲量。
- AI：学徒的反应/误差最弱，法师均衡，大法师有更强的轨迹预测和边反弹目标选择。AI 只能通过与玩家相同的击槌物理接口作用于圆盘。

不要按帧直接设置圆盘位置或把规则写进 `OnCollisionEnter` 的视觉脚本。推荐的 Unity 映射是：`MatchRules` 维护状态机和比分，`TablePhysics` 管理 rigidbody/collider，`MalletMotor` 接受输入目标，`AiController` 输出 AI 目标，`ArenaPresenter` 驱动模型/VFX/UI。Rigidbody 运动在 `FixedUpdate` 中处理，呈现可在 `Update` / 插值阶段读取快照。

## OpenXR、舒适性与输入

- 使用 action-based OpenXR 输入读取单手位置、旋转和扳机值；允许任意单手作为激活手。
- 将控制器姿态投影/约束到桌面可玩平面，再提交给 `MalletMotor`，而不是把刚体直接瞬移穿过边框。
- 将触觉反馈限制在有效击球的短脉冲，并在设备缺少 haptics 时静默降级。
- 提供“重置视角 / 校准桌面”：以当前头部朝向和高度重新定位 `TableAnchor`，使其位于舒适触达距离。不要添加平移、瞬移、强制转向、镜头晃动或基于碰撞的相机位移。
- 继续保留鼠标/键盘输入适配器，用于编辑器内快速验证，但不得让其成为规则的特殊分支。

## URP 表现映射

程序化灰盒可替换为：木质 PBR 球桌、金属符文边、Emissive 圆盘和传送门、魔法学院大厅环境、巫师剪影或低成本角色。URP 中优先保持稳定帧率：限制透明粒子数量、避免全屏后处理依赖、给发光对象控制 bloom 强度，并为 VR 配置合适的 MSAA / render scale。视觉主题应从 `VFX_THEME` 与 `SFX_THEME` 读取，不能改变物理半径、球门宽度或得分体积。

## 验收基线

迁移前从 Web 原型记录配置版本、固定步长、测试输入和预期结果。Unity 版至少应通过这些场景：

1. 中线发球后，普通击球、蓄力击球和斜向边反弹的速度/落点在容差内匹配。
2. 圆盘完全通过球门才各计一次分；边缘擦线、反向离开和重复接触不重复计分。
3. 6:6 后任一得分立即进入胜负界面；重新开始重置全部局内状态，返回菜单不保留旧比赛。
4. 三档 AI 呈现可感知的反应、精度和策略差异，且不会越过自己的半场。
5. 持续静止、飞出边界和卡住圆盘都在配置延时后回中线。
6. OpenXR 控制器、无 haptics 控制器和鼠标调试都能完成同一局规则流程；校准不会改变规则坐标。

当这些灰盒验收稳定后，再替换 `TABLE_MODEL`、`MALLET_MODEL`、`PUCK_MODEL`、`GOAL_MODEL`、`ARENA_MODEL`、`VFX_THEME` 和 `SFX_THEME`。这一步的目标是保持球感不变地提升幻想氛围。

## 当前 Unity 状态与首次运行

Unity 工程位于 `unity/ArcanePuckVR`，使用 Unity `6000.5.4f1`。打开工程后直接载入 `Assets/ArcanePuck/Scenes/ArcanePuck.unity` 并进入 Play Mode，即可运行桌面灰盒对局。桌面输入为鼠标移动、左键蓄力，`R` 重置圆盘，`C` 让观察相机重新对齐当前 `Table Anchor`；相机重置和游戏物理都使用桌面局部坐标，因此锚点平移、偏航、俯仰或侧倾后仍保持一致。

当前已完成：URP、Input System、运行时球桌装配、三档 AI、完整比分/金球流程、配置尺寸的圆盘与击槌凸圆柱碰撞体、局部空间发球/越界/球门判定，以及 Editor/PlayMode 自动测试。OpenXR Plugin、XR Origin、控制器 action、触觉反馈和真机验证尚未接入，不能把桌面 PlayMode 结果视为设备运行时验证。

本地验证命令：

```bash
/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$PWD/unity/ArcanePuckVR" \
  -runTests -testPlatform EditMode -testResults "$PWD/unity/ArcanePuckVR/Logs/editmode-results.xml" \
  -logFile "$PWD/unity/ArcanePuckVR/Logs/editmode.log"

/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$PWD/unity/ArcanePuckVR" \
  -runTests -testPlatform PlayMode -testResults "$PWD/unity/ArcanePuckVR/Logs/playmode-results.xml" \
  -logFile "$PWD/unity/ArcanePuckVR/Logs/playmode.log"
```
