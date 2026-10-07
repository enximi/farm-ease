# 验证记录 · 2026-10-08

目标：Stardew Valley 1.6.15、SMAPI 4.5.2、macOS。使用 .NET 8.0.425 编译为 .NET 6。

## 骑乘快捷开箱修复与 HorseEase 0.1.0 闻哨而来 · 2026-10-08

- 源码定位骑乘开箱的三处阻断：StorageNavigation 的入口检查默认拒绝骑乘；共享 TickMenuHold 在计时中仍使用默认骑乘限制；StorageOpeningMenu/StorageOptionsMenu 继承的 EaseMenu 在输入和每帧检查时拒绝骑乘。现在入口、返回主菜单及长按计时允许稳定骑乘，EaseMenu 新增默认关闭的 AllowMounted，仅仓储等待/设置界面启用；上下马动画、工具使用、过场等原有检查保留。手动整理等其他界面不因此放开骑乘。
- 新增独立「闻哨而来」HorseEase 0.1.0：首页单动作、F10 快捷键、可配置专用手柄键（默认 None），支持单独安装。沿用 Utility.GetHorseWarpRestrictionsForFarmer 与 FarmerTeam.requestHorseWarpEvent；只发出当前玩家的原生马笛请求，房主通过既有 Stable 归属查找、空间检查、马的 mutex 和 warpCharacter 搬运原马。没有创建马、改写归属或另建客机角色副本，无需房主额外安装此 Mod。已骑乘或马已在身边时给出提示；每屏 1.5 秒节流，不自动重试。
- 只读核对本机 1.6.15 Object 的马笛使用路径、Utility 限制与 FarmerTeam.OnRequestHorseWarp。原生请求无成功回执，「已呼唤坐骑」仅表示已发出；房主处理时条件变化仍可能拒绝。菜单描述与 README 明确需要已有认领命名的马、室外和空间限制。
- 共享菜单提供者列表末尾追加 HorseEase，保持既有优先顺序。因菜单源码分别编译进各 Mod，六个已有模块同步补丁版本：TravelEase 1.4.1、GardenEase 1.9.1、FishingEase 0.3.3、StorageEase 1.5.2、SignEase 0.1.1、RidingEase 0.1.1。构建、打包、安装脚本已纳入第七个 Mod；未运行全量打包脚本。
- 七个项目最终 Release 编译均为零警告、零错误；build.sh 语法、Python 安装/打包脚本语法、manifest 与 csproj 版本及 Git 差异空白检查通过。遵守要求，没有编写或运行测试，没有启动游戏，没有生成 ZIP。骑乘连续长按/F9 开箱、等待/取消/换箱、实际召唤、远程与分屏、马被占用时的竞争及自定义地图行为仍待实机验证。
- 确认游戏退出后备份旧六个模块，仅替换各自 DLL、manifest、README，并新增 HorseEase。七个构建/dist/正式安装对应文件一致；其他 Mod、配置、存档及语言共 18 个受保护文件摘要未变。备份：`.work/backups/horse-and-mounted-storage-20261008-010222-049629`；证据：`.work/verification/horse-and-mounted-storage-install.json`。StorageEase DLL SHA-256：`7c140bad5e884131ee77c3a8561ca635bcfa7e713a1a60d16b1ddb143e4cab1d`；HorseEase DLL SHA-256：`4bec5bda776d23c70085c6a2775c0160544d4a4e83223cb3a05ab52fc941ed39`。

## GardenEase 1.9.0 一键牧草疏植 · 2026-10-07

- 在既有统一菜单注册「牧草疏植」，不新增独立 Mod 或配置页。点击后直接执行并进入整理画面，显示实际搬移数，沿用 X / Z 整批撤销和退出清空会话；普通「田园巧整」入口仍直接进入手动整理。
- 按用户最后调整，仅根据已建成 AnimalHouse 建筑的位置规划，不读取农场动物当前坐标或数量。以动物门外格为多源 BFS 起点，按可通行路径计算各建筑附近空地，候选归入最近可达建筑，多栋建筑轮流分配；无建筑或优先区域耗尽时，以现有草丛附近及其他合法空地兜底。同一距离带优先保留已有草格，再考虑四邻生长空间。
- 普通/蓝色牧草沿用精确类型白名单；目标使用统一 `(x+y)%2==0` 棋盘格、Diggable/NoSpawn/地图通行与占位检查，跳过耕地、道路、物体、建筑、水域、地图出入口及其他编辑者预留格。围栏/设施下的牧草、暂不可用草和无足够位置的多余草保持原位。牧草忽略农场 animals 占位，仍检查 NPC、宠物及未骑乘的马。
- 只迁移原对象，不拆分、不克隆、不补草，保留草种、草量及蓝草状态。扩展 BatchMove 内部草专用稀疏计划，允许全农场超过 256 格；手动框选限制保持不变。复用批量最终复核、异常恢复、客户端显示刷新与撤销；撤销保留当下草量，已消失草格或原位置被占用时拒绝整批撤销。
- 多人等待 Open 确认后发起一次 Spread；布局只在房主按当前农场状态计算，重试沿用请求序号去重，成功后更新本会话观察值并使其他会话重叠旧撤销失效。跳过其他玩家当前选中对象，撤销也检查格子预留。需要参与者与房主版本一致。
- 只读核对本机 1.6.15 Grass.dayUpdate、GameLocation.growWeedGrass/IsTileBlockedBy/IsNoSpawnTile、Utility 四邻算法和 Building.getRectForAnimalDoor：自然增长有季节、草量及随机条件，疏植不修改这些规则，不承诺草量持续净增。
- Release 编译零警告、零错误，Git 差异空白检查通过。没有编写或运行测试，没有启动游戏，没有生成 ZIP。棋盘布局实机观感、各农场/自定义地图、实际多人及分屏、吃草后的撤销、保存加载和大农场性能仍待实际游戏验证。
- 确认游戏关闭后备份并仅替换 GardenEase 的 DLL、manifest、README；构建/dist/正式安装逐字节一致。其他 Mod、配置、存档与语言共 33 个文件摘要未变。备份：`.work/backups/garden-pasture-spread-20261007-233935-684032`；证据：`.work/verification/garden-pasture-spread-install.json`；DLL SHA-256：`5c220043cfaed9f464b1dece678ca6bfdd22d752685fb715d2e42d336842daf3`。

## GardenEase 1.8.3 牧草搬移允许农场动物占位 · 2026-10-07

- 普通/蓝色牧草在原位置与目的地均忽略农场 animals 集合内的动物，覆盖单件选择、预览、房主最终执行、交换、批量选择/校验和撤销。混合批量逐对象判断，围栏、箱子等设施仍保留原版动物占位限制，有设施冲突时整批不执行。
- 核对本机 GameLocation.IsTileOccupiedBy、IsTileBlockedBy、isTilePlaceable：农场动物与 NPC 共用 Characters 标志。新增 ArrangeItem.HasCollision 统一入口，仅对支持的牧草移除原生 Characters 检查，同时按原版可见 NPC 碰撞规则补回 characters 集合检查；村民、宠物、未骑乘的马仍阻挡。不修改动物位置或行为，不临时移除动物，其他掩码、地图通行、设施、容器锁及多人格子预留规则保留。
- 草量和原对象身份仍在提交前复核；被动物完全吃掉或替换的草拒绝搬移，剩余草量变化按当前值保留，撤销不会补草。仅查询碰撞，原有对象迁移和 P2 显示刷新流程未改。
- Release 编译零警告、零错误，Git 差异空白检查通过。按项目要求未编写测试、未启动游戏、未生成 ZIP；动物走动/吃草、混合批量、交换/撤销与实际多人/分屏仍需实机确认。房主和参与整理者需统一更新至 1.8.3。
- 确认游戏关闭后备份并仅替换 GardenEase 的 DLL、manifest、README；构建/dist/正式安装逐字节一致，其他 Mod、配置、存档及语言共 33 个文件摘要未变。备份：`.work/backups/garden-pasture-animals-20261007-231618-929582`；证据：`.work/verification/garden-pasture-animals-install.json`；DLL SHA-256：`a971d52948913cd968473cc673fa3ae6f6a352b95789adac87e48c22a4129940`。

## TravelEase 1.4.0 骑马传送人马同行 · 2026-10-07

- 回家、固定目的地和返回上个位置统一允许稳定骑乘，R3/F8 长按入口同步放开；骑手与马引用不一致或上下马动画中拒绝传送。统一菜单的打开/浏览允许骑马，功能调用默认仍拒绝骑乘，仅传送显式放开；一键睡觉、整理和开箱不随菜单浏览放开。
- 只读核对本机 Game1.warpFarmer、ShouldDismountOnWarp、Farmer.netMount/update、Horse.update/SyncPositionToRider、Character.GetBoundingBox、GameLocation.resetForPlayerEntry/startEvent 与 GameMenu。原版坐骑保存在玩家 netMount 中并跟随玩家位置/地图；原版在提交室内传送时同步要求下马。新增 MountedTravel，仅在本 Mod 同步提交传送的作用域内，按本屏玩家、坐骑、源地图与目标地图精确匹配，覆盖该次下马判断；finally 清理作用域。没有新建马、重设骑手、改马的归属或释放原生骑乘锁，普通走门及其他传送不在作用域内。若目标触发原版剧情，允许原版在目的地下马。
- 用原版传送像素落点与马未挤门时的自然碰撞宽度，检查整个覆盖区域；不为预判而临时修改活马坐标。保留原落点边界、水面、设施、角色、触发格、出口、活动与开放条件检查，补充动物跨格碰撞；避开原版会左移的地图最右列。无安全落点时提交前拒绝，人马留在原处。原生网络同步保留，多人只需传送使用者更新；分屏作用域与返回点独立。
- 只编译和安装 TravelEase，版本 1.4.0。TravelEase 是现有菜单候选中第一顺位，安装新版后提供可在骑马时打开的统一菜单，其他已安装 DLL 不需覆盖。Release 编译零警告、零错误，Git 差异空白检查通过；未编写测试、未启动游戏、未生成 ZIP。Harmony 实际加载、人马到达/下马、矿洞大厅与住宅内返回、无空位、多人与分屏、Windows 及第三方坐骑尚未实机验证。
- 确认游戏关闭后备份并仅替换 TravelEase 的 DLL、manifest、README；构建/dist/正式安装逐字节一致，其他 Mod、配置、存档及语言共 33 个文件摘要未变。备份：`.work/backups/travel-mounted-20261007-225306-317212`；证据：`.work/verification/travel-mounted-install.json`；DLL SHA-256：`768bc584ac7b229ac59b5917194de5eb21abeb1fed360c88de488e3d260d4b04`。

## RidingEase 0.1.0 悠然骑行 · 2026-10-07

- 新增独立 RidingEase /「悠然骑行」，默认屏蔽马蹄落地震动；接入统一菜单，可保存开关并通过刷新配置重读。本机分屏共用设置，只需使用者安装，不要求房主安装，不读写存档或发送多人消息。
- 只读核对本机 1.6.15 Horse.PerformDefaultHorseFootstep 与 Rumble：木地、石地和其他地面各有一处 rumble(float,float)，原方法均先判断 rider == Game1.player。Harmony 仅替换这三处调用为可按配置放行的同签名方法，保留原指令标签、异常块、音效与本屏骑手判断；未改全局 Rumble、GamePad.SetVibration 或总震动设置，不逐帧清除震动。调用数量不符时拒绝加载并撤回本 Mod 功能补丁，菜单显示未生效。
- 菜单候选仅在末尾追加 RidingEase，现有菜单提供者优先顺序不变；可连接已安装五个 Mod 的现有菜单 API，也可单独提供菜单。构建、打包和安装脚本纳入第六个 Mod；本次仅构建与安装 RidingEase，没有重新编译或覆盖其他 Mod。
- RidingEase Release 编译零警告、零错误；构建脚本 Bash 语法、Python 打包/安装脚本 AST 语法、Git 差异空白检查通过。未编写测试、未启动游戏、未生成 ZIP；Harmony 实际运行加载、真实手柄震动、菜单开关、多人/分屏、Windows 及第三方坐骑兼容性仍需实机确认。
- 确认游戏关闭后新增正式 Mods/RidingEase，DLL、manifest、README 与构建/dist 逐字节一致；无旧版需覆盖。其他 Mod、配置、存档及语言共 33 个文件摘要未变。证据：`.work/verification/riding-ease-install.json`；DLL SHA-256：`246d73a2e7e3a0ea4ce51f65b8bf3b90608f10ba07120e6d27d3568a69eb505a`。

## GardenEase 1.8.2 其他搬移对象的本地刷新 · 2026-10-07

- 扩展 CropDisplay 为 LayoutDisplay，保留作物最终坐标绘制刷新；对原版牧草更新本地草丛偏移，对普通树木补齐季节/纹理缓存，对果树加载纹理，按地形加入/移除后的最终布局刷新改动格及周围八格的道路连接。核对原生 Grass.setUpRandom、Tree.performPlayerEntryAction、FruitTree.loadSprite、Flooring.OnAdded，所调用路径不改写生长、草量或随机地板朝向；跳过临时隐藏地形。没有主动调用客户端 HoeDirt.updateNeighbors，因其会间接计算水稻灌溉并可能写联网字段，保留原生耕地回调。
- 原生围栏附件不独立进入对象字典，旧搬移流程只更新围栏主体。房主现同步设置附带火把的位置及地图关联，并在捕获灯光前补齐附件地图关联以生成正确的目标灯光标识；客机收到围栏后仅补本地地图引用。复用既有 MoveLights 迁移/撤销，不调用会重新点燃附件的 actionOnPlayerEntry。
- 原生箱子推动结束只清本地动画，联网 kickStartTile 仍可保留旧起点，P2 重建实例时可能重播旧推动。房主整理时将旧起点恢复原生无动画哨兵值；正在被推动的箱子暂不允许整理，避免两个移动流程重叠。每端收到箱子后补充本地箱盖刷新，不修改箱子库存或原生锁。
- 单件、交换、批量和撤销共用以上路径；机器、洒水器、稻草人、告示牌及采集器继续沿用原生对象加入时的位置刷新，篝火保留既有灯光与本屏环境声迁移。本次未发现这些类别需要另外重建对象的证据。以上为源码审查与针对性补齐，不代表所有类别均已复现过消失，也不代表完成实机多人验证。
- Release 编译零警告、零错误，Git 差异空白检查通过。未编写测试、未启动游戏、未生成 ZIP。实际 P2 / 分屏显示、重叠批量、交换、撤销、保存加载及围栏附件的视觉/光照仍需实机确认。房主和参与整理者需统一使用 1.8.2；旁观玩家若要获得本地显示修复也应安装本版。
- 确认游戏关闭后备份并仅替换 GardenEase 的 DLL、manifest、README；构建/dist/正式安装逐字节一致，其他 Mod、配置、存档及语言共 30 个文件摘要未变。备份：`.work/backups/garden-layout-display-20261007-220558-331946`；证据：`.work/verification/garden-layout-display-install.json`；DLL SHA-256：`aba22b5392c9e39434e4a900469163af6d8d28a8bd85e171ca6d1f651a843b75`。

## GardenEase 1.8.1 P2 搬移作物后不可见 · 2026-10-07

- 用户确认搬移的是作物：P2 原位置与目标位置均看不到，房主正常显示，P2 退出整理并传送后恢复。核对本机 1.6.15 Crop、HoeDirt、TerrainFeature、GameLocation 与 NetDictionary/NetRef 原生源码，确认作物绘制坐标、贴图矩形和层级不属于网络字段；网络反序列化创建作物时地形尚未获得最终坐标，随后 HoeDirt.Tile 仅更新 crop.tilePosition，不调用 updateDrawMath。房主 ArrangeItem.Refresh 显式更新缓存，因此双方显示不同；此为源码定位，未启动游戏重现。
- 新增 CropDisplay，订阅 TerrainFeatureListChanged；在农场收到已加入的原版 HoeDirt/Crop 后，核对该格仍是同一地形实例，并用字典中的最终坐标刷新本屏作物绘制缓存。适用于房主、客机与本地分屏，移动、交换、批量及撤销复用同一事件；不依赖整理成功回复的到达顺序，不重发或复制作物、不改写客户端地形字典，不调用会写入水稻缓存等网络字段的房主 Refresh。
- Release 编译零警告、零错误，Git 差异空白检查通过。未编写测试、未启动游戏、未生成 ZIP。实际 P2 连续搬移、交换、批量、撤销、多屏/联机显示与保存加载仍需实机确认。
- 确认游戏关闭后备份并仅替换 GardenEase 的 DLL、manifest、README；构建/dist/正式安装逐字节一致。其他 Mod、配置、存档及语言共 30 个文件摘要未变。备份：`.work/backups/garden-crop-display-20261007-215607-560277`；证据：`.work/verification/garden-crop-display-install.json`；DLL SHA-256：`be7b5ec18c6d96f3c73d917b9f0491dbf4c4b73180b269b6c0e3906a4f3fc1cd`。

## StorageEase 1.5.1 远程箱子首次点击误报占用 · 2026-10-04

- 用户反馈某些箱子稳定出现第一次点击提示“箱子正在执行其他操作，请重试”、第二次才成功。核对当前 StorageAccess.Acquire 与本机 1.6.15 NetMutex、AbstractNetEvent1 源码：ReleaseLock 清空 owner 和回调但不更新 prevOwner；远程箱子在两次操作之间可能不轮询原生锁，导致旧解锁状态在新 RequestLock 注册回调后才被 Update 处理，误触发本次 onLockFailed。房主同持有者重复请求也可能因为 owner 等于遗留 prevOwner 而漏掉成功回调。此为源码确认的可触发路径，未在用户存档中复现。
- 在每个新原生锁请求前先调用 Update(在线玩家)，完成旧状态处理，再复核操作代次、待执行状态和界面有效性，随后执行既有持锁判断及 RequestLock。取放、整理、补齐堆叠和远程制作共用此修复；保留房主预留、原生锁、网络插值等待、物品与背包复核、超时取消、迟到回调释放和工作台持锁约定。没有跳过锁或强制清除其他玩家持有的锁。
- Release 编译零警告、零错误，Git 差异空白检查通过。按项目要求未编写测试、未启动游戏、未生成 ZIP。远程连续取放、左右键/手柄、实际多人/分屏、真实竞争与取消/迟到授权仍需实机确认。最新可读日志加载的是 1.5.0，包含本地分屏记录，但没有本次具体误报的运行跟踪，不能据此认定用户当前会话或实机修复结果。
- 确认游戏关闭后备份并仅替换 StorageEase 的 DLL、manifest、README；构建/dist/正式安装逐字节一致。其他 Mod、配置、存档及语言共 30 个文件摘要未变。备份：`.work/backups/storage-lock-state-20261004-235435-755245`；证据：`.work/verification/storage-lock-state-install.json`；DLL SHA-256：`c630475e66955797ec0eb8eecc9d95110bd5cc73dca95c98ec74e690789f9086`。

## GardenEase 1.8.0 篝火整理 · 2026-09-28

- 将原版篝火 `(BC)146` 的精确 Torch 类型加入整理白名单，复用单件移动、设施交换、批量框选和撤销；火把、火盆、野炊工具与自定义子类未放开。保持原对象、当前点燃状态和附属数据；点燃时预览绘制静态原版火焰，不调用有副作用的真实对象绘制。
- 只读核对本机 Torch、Object、GameLocation 与 AmbientLocationSounds：篝火属于 Torch 子类；performRemoveAction 会熄灭，placementAction 会重建对象，故均不调用。既有 MoveLights 迁移原网络光源的位置/标识及地图灯光；原生对象位置回调更新碰撞框。
- 新增当前农场 ObjectListChanged 环境声处理，各安装者按本屏当前地图处理本地及网络同步后的对象变化；按旧字典坐标先移除篝火声音，再按最终布局注册燃烧声，适用于交换、重叠批量和撤销。不参与整理且未安装新版的旁观玩家需重新进入农场刷新环境声。
- Release 编译零警告、零错误；Git 差异空白检查通过。没有编写测试、启动游戏或生成 ZIP。实际移动、交换、重叠批量、撤销、火焰视觉/声音、保存加载、联机/分屏及异常恢复尚未实机验证。
- 确认游戏退出后备份并安装，仅替换 GardenEase 的 DLL、manifest、README；构建/dist/正式安装逐字节一致，其他 Mod、配置、存档及语言共 25 个文件摘要未变。备份：`.work/backups/garden-campfires-20260928-203603-876112`；证据：`.work/verification/garden-campfires-install.json`；DLL SHA-256：`2efd952455dbae1d347412ceb9c54fd7b2d92f1e9bbbcac6fd4c19911ddc7f75`。

## SignEase 0.1.0 一目了然自动告示牌 · 2026-09-28

- 新增独立 SignEase /「一目了然」，默认开启，统一菜单提供房主开关。告示牌位于箱子正上方一格时，以箱内第一个非空且数量大于零的物品设置原版 displayItem/displayType；空箱清空牌面。支持原版木牌、石牌、金色物品牌与普通/石/巨型/祝尼魔箱子，固定地图和建筑内部；文字牌及自定义 Sign/Chest 子类不接管。
- 房主每 30 帧扫描一次，客户端不写牌面；原生网络字段负责同步，因此只需房主安装。保存时暂停，标题/加载时清理缓存。箱子持有原生使用锁、隐藏或正在移动时跳过，不获取锁、不更改库存。失去上下相邻关系后停止更新并保留最后牌面，可恢复手动用途。
- 只读核对本机 Sign.checkForAction/draw/initNetFields 和 NetFields.WriteFull；沿用原版 getOne 展示副本与五种展示类型，拒绝把原库存引用作为牌面。对脱离原库存的副本序列化比对元数据，仅内容变化或手动覆盖时更新原生字段；不写新存档对象或增加可领取物品。单个物品复制/序列化失败时保留原牌面并按类型限次记录。
- 共享菜单仅在候选末尾追加 SignEase，旧候选优先顺序不变。新 Mod 可连接现有菜单提供者，单独安装时自行提供入口；既有四个已安装 DLL 无需更新。构建/打包/安装脚本已纳入第五个 Mod，更新安装说明；本次未执行全量打包或全量安装。
- SignEase Release 编译零警告、零错误；构建脚本 bash 语法、Python 打包/安装脚本 AST 语法与 Git 差异空白检查通过。未编写测试、未启动游戏、未生成 ZIP；牌面可见性、各种物品图标、远程修改、搬移、保存加载、实际多人及分屏仍需实机确认。
- 确认游戏退出后新增正式 Mods/SignEase，DLL/manifest/README 与构建/dist 逐字节一致；无旧版 SignEase 需要覆盖。其他 Mod、配置、存档及语言文件共 25 个摘要未变。证据：`.work/verification/sign-ease-install.json`；DLL SHA-256：`1f1fadbac9049dc8730c994ac06d180de63a36ea8be88bfc3f1c369c167d1543`。

## StorageEase 1.5.0 首件物品识箱与紧凑标签 · 2026-09-28

- 列表和标签统一读取箱内按格子顺序排列的第一个非空、数量大于零的物品，用其名称和物品类型图标自动识别箱子；空箱显示淡色箱子图标与“空箱子”。预览优先读取本地原生库存，未同步地图暂用目录中的名称/图标 ID；显示不复制、移动或排序物品，图标仅使用 ItemRegistry 的静态纹理。
- 箱子列表每行新增 36 像素物品图标，名称/地点/坐标与开关文字向右留出图标位置；搜索仍匹配箱内所有物品和地点。移除手动改名入口及房主改名写入分支，旧 name 元数据不再参与显示、搜索，也不删除存档中的旧值；空列表的刷新索引同步调整。
- 顶部标签改为图标加序号，常规宽 64 像素、间隔 4 像素，按可用宽度容纳更多标签，少量箱子不再拉伸。保持原生库存位置、当前高亮、LT/RT、PageUp/PageDown、鼠标切换与全部箱子入口；悬停显示完整物品名、地点和坐标。列表按地点/坐标排序，物品变化不触发按名称重排。
- StorageEase 1.5.0 Release 编译零警告、零错误，Git 差异空白检查通过。未编写测试、未启动游戏、未生成 ZIP；图标实际观感、缩放/分屏、多人变更首件后的界面同步仍待实机验证。其他 Mod 本次无需更新；房主和参与者需统一使用相同 StorageEase 版本。
- 游戏关闭时备份并安装，仅替换 DLL、manifest、README；构建/dist/正式安装逐字节一致，其他 Mod、配置、存档、语言共 22 个文件摘要未变。备份：`.work/backups/storage-item-tabs-20260928-015406-735512`；证据：`.work/verification/storage-item-tabs-install.json`；DLL SHA-256：`aab6516fac9642fa77e9d47a0d276ca9fdc249275c03216ab00051dcfc9c83b5`。

## StorageEase 1.4.0 多人同时使用同一箱子 · 2026-09-28

- 手动开箱与远程开箱改为浏览会话，不在整个界面存续期间占用 NetMutex；保留原生 ItemGrabMenu、背包、箱子库存及快捷键/标签。对原版左右点击、整理入口和 FillOutStacks 接入操作协调，取放造成菜单重建仍视为同一浏览会话。
- 房主队列按箱子原生 mutex（包含祝尼魔共享库存）一次性预留操作所需全部容器，避免制作持有部分箱子等待其他箱子。由发起者取得短时原生锁并执行原版物品逻辑，房主不直接覆盖远端玩家背包。箱子浏览与材料预览不再因别人在查看而拒绝；制作仍复核当前材料和背包容量。
- 等待期间不提前移动物品，重复取放点击不累积。执行前核对箱子引用/位置/功能开关、菜单、视口尺寸、修饰键、背包和手持物品；点中的格子必须仍是原物品实例，数量变化使用最新值，被取空/替换/排序移位取消旧点击。整理和补齐堆叠使用锁内最新库存。
- 库存/玩家增量在解锁前强制发送；取得原生锁后等待游戏插值 ticks 再操作，房主观察解锁并等待插值缓冲后释放预留。队列有等待期限，释放消息取消未授权请求；迟到的原生锁回调仅释放本玩家取得的锁。未强制抢占仍被持有的锁，原版工作台及未安装新版的玩家仍可持有传统会话锁。
- 源码核对本机 1.6.15 的 Chest、ItemGrabMenu、InventoryMenu、MenuWithInventory、NetMutex 和 Multiplayer.UpdateLate/网络插值流程。浏览期间允许田园巧整搬箱，旧界面检测移动/移除后关闭；实际操作期间继续受共同原生锁保护。变更仅涉及 StorageEase，其他 Mod 无需重新编译。
- Release 编译零警告、零错误，Git 差异空白检查通过。未编写测试，未启动游戏，未生成 ZIP。尚未实机验证多人同时取放/拆分/制作、库存满、取消/迟到授权、断线、跨地图、工作台混用与分屏；以上并非联机运行或故障注入验证。
- 检测游戏已退出后备份并安装 StorageEase 1.4.0，仅更新 DLL、manifest、README，构建/dist/正式安装逐字节一致；其他 Mod、配置、存档及语言共 22 个文件摘要未变。备份：`.work/backups/storage-concurrent-20260928-014829-538590`；证据：`.work/verification/storage-concurrent-install.json`；DLL SHA-256：`5869abb178348289d39198c7d9e5cc175d17743eb3d72a0693a499b3b8f6eb17`。

## StorageEase 1.3.0 所有玩家共同访问箱子 · 2026-09-28

- 删除 BoxInfo 的 Owner/Shared、StorageCatalog 的 Owner/Allowed 与按玩家筛选目录逻辑。房主发布全部支持的箱子，客户端开箱、Tab 切换、制作预览及实际扣料不再检查放置者或共享标记；仍核对箱子身份、位置、功能开关、材料及原生使用锁。
- 删除界面的共享开关、私人/已共享标签和箱主专属提示；所有玩家均可给箱子改名、切换远程或取材开关，房主执行时也取消箱主限制。列表操作从六项改为五项，改名与刷新索引同步调整，空列表仍可刷新。关闭的远程/取材开关对所有玩家一致生效。
- 旧 shared 标记不再读取或写入，旧私人箱子自动纳入全部玩家的目录和取材范围；没有直接修改存档，也未改写原版 owner 或复制库存。原生锁的持有人识别、在线玩家与 Mod 版本校验、共享库存去重及关闭释放机制保留；这次没有共享菜单源码改动。
- 源码搜索确认运行时代码和 manifest 中不再残留旧的私人/共享开关、Allowed、箱主限制或权限文案。Release 编译零警告、零错误，Git 差异空白检查通过。未编写测试，未启动游戏，未生成 ZIP；跨玩家开箱、旧箱子标记兼容、设置修改、合成扣料、联机与分屏仍未实机验证。
- 游戏关闭时仅更新 StorageEase 至 1.3.0；DLL/manifest/README 在源码/构建、dist 和正式目录逐字节一致，存档、语言、配置及其他 Mod 共 22 个文件摘要未变。备份：`.work/backups/storage-all-players-20260928-013205-202875`；证据：`.work/verification/storage-all-players-install.json`；DLL SHA-256：`ef860be873c28f47d48848236642ff74b0ffb2667ee45e1a0db146ca40359588`。

## 共享菜单长按释放修复 · 2026-09-28

- 用户实机反馈 L3 长短按均打开统一菜单。检查当次 SMAPI 日志，确认已加载 TravelEase 1.3.1、GardenEase 1.7.1、FishingEase 0.3.1、StorageEase 1.2.0；配置 EnableMenuHold=true、MenuHoldMilliseconds=500、MenuButton=LeftStick，未发现长按 API 注册失败，排除未更新及配置关闭。
- 只读反编译本机 SMAPI 4.5.2 的 SInputState.TrueUpdate/DeriveStates 和 InputHelper：Suppress 把后续对游戏可见的按键状态改为释放，会在仍物理按住时产生 ButtonReleased；IsSuppressed 在实际松开前持续为 true。实际松开时覆盖才清除，通常不会再产生第二次 Released。
- 原共享菜单订阅 ButtonReleased 并直接执行短按，导致屏蔽后的下一帧提前开菜单。移除该订阅，改为 TickMenuHold 中同时检查 !IsDown && !IsSuppressed 时才结束手势；仍按住则累计时长，触发后只吞掉剩余按住时间，实际释放不再开菜单。失焦、状态变化、换地图、配置重载和各屏独立状态沿用原处理。
- 四个共享菜单副本统一更新：TravelEase 1.3.2、GardenEase 1.7.2、FishingEase 0.3.2、StorageEase 1.2.1。最终 Release 编译均零警告、零错误；Git 差异空白检查通过。未编写测试，未启动游戏，未生成 ZIP；实际手柄短按/长按及分屏仍待实机确认。
- 确认游戏退出后安装，各 Mod 的 DLL/manifest/README 在构建、dist 和正式目录逐字节一致；存档、语言、配置及其余文件共 13 个摘要未变。备份：`.work/backups/menu-hold-release-20260928-003426-636369`；证据：`.work/verification/menu-hold-release-install.json`。
- DLL SHA-256：TravelEase `1f2e91acee9e1d47804a0eb46df28fb902dd5991d68a1b50079bbe213aa1124c`；GardenEase `65c8690a8d9703578ec88a7e0b4b85f66e671f165969dffe243caa8c12af5f93`；FishingEase `5a46981deca08569f7ee85b3758acf8fea1a60bacf0a3fa1f0b17b8e841cacb0`；StorageEase `c12ea793236ae1d2469e1bd224b218254532a7d8ba1e9359437b4cc637b35eee`。

## StorageEase 1.2.0 快捷开箱与箱子锁清理 · 2026-09-28

- 统一菜单的「随取随用」改为直接打开上次箱子。按玩家元数据保存箱子 ID；先刷新目录与权限，再通过既有原生锁打开。首次使用、失去权限、已拆除或失败时回到可搜索列表；等待屏幕可取消。快捷入口退出回到游戏，统一菜单进入则返回首页。
- 新增长按共享菜单键（默认 L3）500ms 开箱，短按释放时打开原菜单；F7 仍立即开菜单，F9 快捷开箱，可配置为其他键或 None。长按按屏幕记录，仅自由操作时接管，失焦/换地图/过夜/标题/刷新配置清除；原有回家和睡觉快捷键保持其职责。可选 IFarmMenuHoldApi 扩展兼容旧菜单 API，旧提供者未支持时降级保留普通入口及键盘开箱。
- 箱子标签栏新增「全部箱子」按钮，箱子界面按 L3 / F7 打开列表；保留 LT/RT 与 PageUp/PageDown 换箱。手持物品时拒绝切换到列表。列表新增设置入口（Start / F6），提供原有范围、制作与烹饪取材选项及长按开关。
- 核对本机 1.6.15 Chest.ShowMenu、grabItemFromInventory、grabItemFromChest、updateWhenCurrentLocation、ItemGrabMenu 整理时的复制构造、IClickableMenu.exitThisMenu 和 NetMutex.Update/ReleaseLock：原生箱子并不在菜单清理时直接解锁，而依赖箱盖后续更新。新增对手动和远程箱子菜单的统一跟踪与退出回调；同箱取放/整理造成的界面重建继续持锁，切箱期间跳过源界面退出释放，目标成功后仅释放旧箱锁。下一帧仍核对菜单上下文，覆盖菜单被替换或异常关闭。
- 修正房主远程租约保活：关闭中的租约不再按全体在线玩家保活；旧租约只保护对应持锁玩家，不影响后来其他玩家的锁。关闭/过期租约不再因别人正在使用旧箱子而一直保留。客户端释放仍仅操作自己已取得的锁，延迟授权回调沿用取消代次检查；没有增加强制抢锁或按超时无条件解锁。同步超时记录刷新时间，避免立即自动重试使等待界面无法退回列表。
- 配套更新共享菜单：TravelEase 1.3.1、GardenEase 1.7.1、FishingEase 0.3.1。四个 Release 编译均零警告、零错误；Git 差异空白检查通过。仅更新 dist 文件夹，未生成 ZIP。
- 确认游戏已退出后安装四个版本，各自 DLL/manifest/README 与源码/构建及 dist 逐字节一致；配置、存档、语言及其他非替换文件共 13 个摘要未变。备份：`.work/backups/storage-quick-locks-20260928-001126-830435`；安装证据：`.work/verification/storage-quick-locks-install.json`。
- DLL SHA-256：TravelEase `366333df09636f37d5ba8bdc7f3ef86533f249cf91cd8dfb066dc913bd3a80da`；GardenEase `c8d614d89ce35d245b66d2def52518172968501dd4ed0def3cce6652241e0964`；FishingEase `c1baf7d7232384b2430d976197cfd7b95a7499d0bc3b56edb81b3e93a9c0fedc`；StorageEase `8a8945f2ebb89cb6e9cc00170a35cc67bee101bab13b7df7ae122166668b10eb`。
- 未编写测试，未启动游戏。上述为源码核对、编译和安装证据，未复现用户那一次卡锁现场；手柄短按/长按、等待取消与返回、不同缩放下标签布局、同箱重建、多人关箱后另一玩家接手、延迟授权/重试、断线、跨地图和分屏仍需实机验证。

## GardenEase 1.7.0 告示牌整理 · 2026-09-27

- 新增原版 Sign（木牌、石牌、金色告示牌）与普通 Object 文字告示牌识别，接入现有单件搬移、设施交换、批量框选和撤销。继续只移动原实例，不重建牌子或展示物品；展示物品、展示类型、原始文字与附属数据随牌保留，撤销不回退其他玩家更新的牌面内容。
- 物品牌预览只读 ItemRegistry 绘制静态图标；文字牌按 showNextIndex 显示空白/已填写外观。预览不调用实时对象的 draw 或展示物品的 drawInMenu，不改动牌子的真实位置、动画或内容。地图自带装饰路牌和特殊自定义子类不在支持范围内。
- 源码核对本机 1.6.15 Sign 的 displayItem/displayType 网络字段、Object 的 signText、文字编辑回调和牌子放置分支。沿用现有房主执行、源对象引用复核、格子保留、放置限制及批量冲突检查；房主与参与整理者须统一更新到 1.7.0。
- GardenEase Release 编译零警告、零错误，Git 差异空白检查通过；构建与 dist 的 DLL、manifest、README 逐字节一致。DLL SHA-256：`59bed3beb8755b9e3ad670c2c6da3c0c7f62c520b0fb935328e31b4e64bdc91e`。未生成 ZIP。
- 用户保存并退出游戏后，仅更新正式 GardenEase 的 DLL、manifest、README 至 1.7.0，与构建和 dist 逐字节一致；其余 Mod、配置、存档及语言文件共 22 个摘要未变。备份：`.work/backups/garden-signs-20260927-225932-574924`；安装证据：`.work/verification/garden-signs-install.json`。
- 未编写测试，未启动游戏。牌面预览、搬移/交换/整批撤销、保存加载、多人同时编辑牌面和整理，以及第三方内容兼容性尚未实机验证。

## TravelEase 1.3.0 分组目的地与解锁条件 · 2026-09-26

- 目的地分为农场周边、小镇、野外、远方，保留 7 个原有落点并新增 9 个：旅行货车、法师塔、铁匠铺/博物馆、鱼店码头、铁路/温泉、采石场、秘密森林、沙漠和姜岛码头。每个落点使用独立菜单 ID，避免同地图多个落点相互覆盖；最多一组 5 个目的地加返回，无共享菜单源码改动。
- 核对本机 1.6.15 Forest 的原木位置 (1,6) 和旅行货车边界、Mountain 桥修复条件及第 31 天铁路解封、Game1 沙漠可达性、BoatTunnel 修船与 IslandSouth 首次到访标记。未解锁条目禁用并显示条件，提交和返回旧位置再次检查；采石场按 Mountain 东侧区域区分，姜岛检查当前玩家已登岛。远方沿用免费规则，不消耗票价。
- 解包并只读解析本机 8 张原版 XNB 地图，核对新增 9 个落点有 Back 地板、无 Buildings 阻挡、无水面属性，结合门口/出口坐标调整铁匠铺落点至 (94,83)。原始资源与游戏文件未修改；结果：`.work/verification/travel-destinations-map-inspection.json`。这是静态地图检查，不是游戏运行验证。
- 安全落点额外排除无地板、踩踏触发格、未解锁区域和当天旅行货车预定占地区域（即使未进地图还未生成运行时边界）；继续避开障碍、其他玩家和出口。新增稳定地图支持返回，随机地下矿层仍不记录。
- TravelEase Release 编译零警告、零错误，Git 差异空白检查通过。游戏关闭时仅更新 TravelEase 至 1.3.0，构建/dist/正式安装的 DLL、manifest、README 逐字节一致；其他 Mod、配置、存档与语言文件共 22 个摘要未变化。没有生成 ZIP。
- 备份：`.work/backups/travel-destinations-20260926-043329-293551`；证据：`.work/verification/travel-destinations-install.json`；DLL SHA-256：`2af5aa0ee94ec923050cdaaa3c29b52d47d20acacf13c9bb90ae22928fb26ce8`。
- 未编写测试，未启动游戏。分组菜单手柄操作、运行时障碍/事件、各条件前后解锁、农场帮手状态同步与返回、分屏及第三方地图兼容性尚未实机验证。

## TravelEase 1.2.1 矿洞目的地 · 2026-09-25

- 「随心往返 → 选择目的地」新增「矿洞」，位于山上之后、森林之前。落点采用本机 1.6.15 Game1 原生矿洞传送使用的 Mine (18,12)，通过现有安全落点搜索避开障碍、其他玩家及出口格；不修改矿层或电梯解锁进度。
- Mine 入口大厅加入稳定返回点，离开大厅传送后可返回；随机地下矿层继续不记录。沿用原有传送状态限制、特殊活动限制、分屏记录和原生网络同步，无共享菜单或其他 Mod 代码改动。
- TravelEase Release 编译零警告、零错误，Git 差异空白检查通过。游戏关闭时仅更新 TravelEase 至 1.2.1，构建/dist/正式安装的 DLL、manifest、README 逐字节一致；其他 Mod、配置、存档与语言文件共 22 个摘要未变化。没有生成 ZIP。
- 备份：`.work/backups/travel-mine-20260925-215212-657762`；证据：`.work/verification/travel-mine-install.json`；DLL SHA-256：`b2b19f4c94560c88595b039deb951c1a6ef780a599d98812dab7deee3b93881a`。
- 未编写测试，未启动游戏。实际矿洞落点、进出大厅与返回、首次进入事件、多人和分屏行为尚未实机验证。

## TravelEase 1.2.0 一键睡觉 · 2026-09-25

- 在统一菜单首页与随心往返页面注册「一键睡觉」，并提供 `travelease sleep`。查找当前玩家自己的 FarmHouse/Cabin 与玩家床位，原生传送后等待淡入结束，再调用原版 `Sleep_Yes`，沿用结算、夜间事件、存档及多人 ReadyCheckDialog；不直接推进日期或替他人确认。
- 用户选定 View / Back 长按 1.2 秒睡觉，短按释放时打开原版 QuestLog。独立的分屏长按状态只在自由操作接管，压下时抑制原按键，长按显示进度且仅触发一次；短按在释放时补回任务日志。过场/菜单沿用原键，自定义菜单与回家键优先；失焦、传送、次日、返回标题或刷新配置清理状态。无需修改已有配置文件，新字段使用默认值。
- 每个分屏独立记录待处理请求，记录玩家、住宅、床位和床种，30 秒超时；到达后重新核对，遇到事件、倒下、床位变化或角色离开则取消。阻止重复请求及等待期间的传送。退出存档或次日清理请求；只识别并关闭原版 Sleep 问题，不确认其他对话或带回调的问题。
- 核对本机 1.6.15 GameLocation.startSleep/doSleep/answerDialogueAction、FarmHouse.GetPlayerBed、BedFurniture.GetBedSpot、Game1.warpFarmer 和 DialogueBox.closeDialogue 的流程；多人等待的取消和完成交给原版管理。没有新增网络协议或共享菜单源码改动。
- TravelEase Release 编译零警告、零错误；构建与 dist 的 DLL/manifest/README 一致，Git 差异空白检查通过。DLL SHA-256：`7ff7974962590ed07a9f6cf74846af3d4e6d55ed2263bbdcc2897899c26ee5a7`；构建证据：`.work/verification/travel-sleep-build.json`。
- 用户保存并退出游戏后，仅更新正式 TravelEase 至 1.2.0，DLL/manifest/README 与构建和 dist 一致；其余 Mod、配置、存档与语言文件共 22 个摘要未变。备份：`.work/backups/travel-sleep-20260925-192111-483732`；安装证据：`.work/verification/travel-sleep-install.json`。未编写测试，未启动或重启游戏；手柄短按/长按及进度显示、实际回床、对话清理、结算存档、多人等待/取消、入屋事件、分屏和 Windows 行为尚未实机验证。

## GardenEase 1.6.0 牧草整理 · 2026-09-21

- 支持原版普通牧草及蓝色牧草的单件搬移、两格交换、批量框选和撤销。搬移原 Grass 对象，保留草种、当前草量、蓝草啃食状态及附属数据；不调用收割、生长或重新创建流程。完全吃完/割除、替换或不再是可食用草时拒绝操作，撤销不会补回消耗的草。
- 设施下方允许保留牧草；框选同时带走草和上方支持的围栏/设施，单件仍优先选中上方设施。草与耕地、道路、树木不能跨类交换；批量继续拒绝选区外同层占用，任一冲突则整批不动。
- 多人描述加入草种和草量，沿用房主引用复核、选区保留及整批事务。源对象仅草量改变时按最新状态移动；目标摘要变化时要求重新确认。房主与参与整理者需统一更新到 1.6.0。
- 核对本机 1.6.15 Grass 的 OnAddedToLocation、loadSprite、setUpRandom、reduceBy 与 dayUpdate：添加只刷新季节贴图，额外 setUpRandom 只刷新位置相关外观；没有重置草量或蓝草私有啃食状态。预览只读季节贴图与草量，不临时移动原对象或使用 Game1.random。洞穴草、地图背景草地、装饰植物及自定义 Grass 子类不在支持范围。
- GardenEase Release 编译零警告、零错误，Git 差异空白检查通过。构建、dist 和正式安装的 DLL/manifest/README 逐字节一致；其余 Mod、配置、存档与语言文件共 22 个摘要未变。未生成 ZIP。
- 备份：`.work/backups/garden-grass-20260921-211939-737184`；证据：`.work/verification/garden-grass-install.json`；DLL SHA-256：`6db7865e50496cd88ce40087908d1b2e5d82f057bd5cbc4f7a38e2e9ef69580c`。
- 未编写测试，未启动游戏。草丛预览、单件/批量搬移与撤销、动物进食中整理、围栏与草一起移动、联机同步与冲突、本地分屏及 Windows 实机行为尚未验证。

## GardenEase 1.5.0 批量框选搬移

- 新增手柄 Y / 键盘 B 切换单件与批量模式，确认矩形两角选取最多 256 格。方向键、摇杆及鼠标支持整组预览；鼠标仅在实际屏幕位置变化时更新光标，避免镜头平滑滚动拖动选区。框选包括同格的设施与道路/空耕地，树与采集器作为整体；不支持的地形或对象会阻止框选并提示坐标。
- 批量计划保留原对象引用，预览使用虚拟的最终两层布局，不临时修改农场字典。支持源区/目标区重叠和平移；不交换、不覆盖选区外的对象，空洞不写入。检查源位置双层引用、目标占用、地图、角色/动物、大型地形、容器状态和最终树木间距，标出冲突格；任一规则不符则整批不执行。
- 房主先取得所有去重容器锁，再复核整批并执行全部移除、全部添加、刷新位置/邻接与光照；异常尝试逐项恢复。每批记录一个撤销，反向计划保存搬移后的源位置引用；撤销重新验证全部格子、保留当前物品/加工/植物状态。洒水器和稻草人覆盖分别求并集，预览结果按光标变化或约 100ms 更新，绘制时裁剪不可见覆盖格。
- 多人协议升级为 v2；矩形请求限制大小并附带两层内容摘要，房主按已有身份/顺序号机制逐格复核并串行执行。批量实际源格统一保留，单件与批量之间共同检查冲突。其他整理者修改任一涉及格子后，相关整批撤销失效。没有把源区预览或目标光标转换为对正常游戏交互的长期锁。
- 核对本机 GameLocation 的 IsTileOccupiedBy、IsTileBlockedBy 和 isTilePlaceable：放置属性检查不含当前占用，字典占用由虚拟布局处理；建筑、动物、角色和地图阻挡仍使用原版检查。复查重复请求、源目标重叠、地面/设施双层、树上采集器、整批锁释放与回滚、撤销及跨玩家保留路径。
- 最终 GardenEase Release 编译零警告、零错误，Git 差异空白检查通过。仅更新正式 GardenEase 至 1.5.0，构建、dist 与正式安装的 DLL/manifest/README 逐字节一致；22 个其他 Mod、配置、存档及语言文件摘要未变。未重新生成 ZIP。
- 备份：`.work/backups/garden-batch-20260920-230133-534469`；证据：`.work/verification/garden-batch-install.json`；DLL SHA-256：`d119483e94104f8e6d22badcbbba11cb7b56d480342924d2d9fcd3cb6cb80106`。房主与参与整理者须统一安装 1.5.0。
- 未编写测试，未启动游戏。框选显示、鼠标/手柄手感、大选区帧率、各类对象搬移与撤销、异常回滚、多人争用/延迟/断线、本地分屏及 Windows 实际运行尚未实机验证；编译和源码核对不等同于这些场景已通过运行验证。

## StorageEase 1.1.0 原版箱子标签

- 手动打开受支持的箱子或从仓储打开箱子后，在原版库存上方空白区域绘制箱子标签；当前箱子高亮，长名省略，悬停显示地点、坐标和总数。标签数量按宽度限制，随当前箱子滚动，窗口尺寸变化后重新布局。
- LT / RT、PageUp / PageDown 和鼠标标签/箭头切换箱子，首尾循环。原有方向导航、LB / RB、整理、颜色选择和取放物品仍由原版界面处理。适配原版取放物品重新创建 ItemGrabMenu 的行为，各分屏单独维护标签状态。
- 标签遵守原有远程开关、主人/共享权限及范围设置；物理打开不获得额外远程权限。持有物品时拒绝切换；等待目标授权和锁期间保留原箱子，失败后仍可使用原箱子。服务器允许同一访问会话在切换期间保留源箱与目标箱，成功后客户端释放源箱，祝尼魔入口共享锁不被提前释放。关闭、取消和超时继续使用原有释放路径，未完成的锁回调会阻止重入请求。
- 核对本机 Chest.ShowMenu、取放重建菜单、ItemGrabMenu 布局/控制器输入、MenuWithInventory 持有物品清理、IClickableMenu 退出路径、DiscreteColorPicker 布局及 NetMutex 的所有权和回调源码。StorageEase 最终 Release 构建零警告、零错误；Git 差异空白检查通过。
- 仅更新正式 StorageEase 至 1.1.0，构建、dist 目录与正式安装的 DLL、manifest、README 逐字节一致；其余 Mod、现有配置、存档及语言设置共 22 个文件摘要未变化。备份：`.work/backups/storage-tabs-20260920-223343-822122`；安装证据：`.work/verification/storage-tabs-install.json`。没有重新生成 ZIP。
- DLL SHA-256：`31e47a2635b3e51334f21c178ae6d543d002a67bc0ad81cb54a94b30622cc54a`。房主和使用本功能的玩家都需更新到 1.1.0。
- 未编写测试，未启动游戏。标签实际显示、不同分辨率与 UI 缩放、颜色面板/提示框重叠、手柄操作、多人切换时锁争用与断线、分屏及 Windows 实机行为尚未验证。

## GardenEase 1.4.1 整理入口简化

- 「农场随心」主菜单的「田园巧整」直接启动整理模式，移除原分类页和「开始整理农场」这一步；入口保持原有排序，农场室外限制及多人版本检查保留。`gardenease` 命令也直接启动整理。
- B / Esc / 右键在有选中对象时先取消选中，没有选中对象时直接返回「农场随心」主菜单；等待房主确认时主动返回也回到主菜单。退出仍先释放整理会话并恢复镜头；换图、活动等强制退出保留原有处理。
- 共享菜单状态命令同时列出主菜单分类和直接操作，避免遗漏田园巧整；四个 Mod 因共享源码一并重新构建。GardenEase 升级为 1.4.1，其他功能版本不变。
- 四个 Release 构建均零警告、零错误。已安装至正式 Mods，四个独立包、合集、构建及已安装文件核对一致；13 个存档、语言、配置及无关 Mod 文件摘要不变。
- 旧版及配置备份：`.work/backups/installed-20260920-205337-401778`；改动前源码包：`.work/backups/garden-direct-source-20260920-205259`；安装证据及 DLL 摘要：`.work/verification/garden-direct-install.json`。
- 未编写测试，未启动游戏。键鼠、手柄进入/返回、多人等待时退出的实际游戏行为尚未实机验证。

## StorageEase 1.0.0 随取随用

- 新增第四个独立 Mod「随取随用」，接入原有统一入口，支持独立安装。共享菜单提供者列表和构建、打包、安装脚本扩展到四个 Mod；原三个功能版本保持不变，重新编译共享入口。
- 新增仓储列表、箱名/地点/箱内物品搜索、逐箱远程/取材/共享开关及命名；用原版 ItemGrabMenu 操作原箱子。默认随行模式、制作取材开启、烹饪取材关闭，可切换农场/农舍限定的舒适模式。
- 原版 CraftingPage 配方显示加入允许使用的箱内材料。实际制作先规划所需箱子，再申请原生 NetMutex，重新核对菜单/配方、箱子身份、权限、材料数量与背包空间，调用原版制作逻辑并把成品放入背包。工作台/厨房的原有材料列表保留，箱子库存按引用去重。支持可选烹饪与调味料、同配方最多 25 次的等待批量。
- 箱子通过原版 owner 字段归属，无 owner 的历史箱子归房主；共享默认关闭。支持普通木/石箱、巨型箱和祝尼魔箱，排除礼物、NPC 箱、冰箱、出货箱、机器附件/自动装料箱、自定义子类及临时生成楼层。使用普通 modData 保存 ID、名称与开关，不转换库存、不添加自定义存档对象。
- 多人目录/设置/访问申请由房主核对；远程地图使用原生 NetRoot 初始包与增量，经同一 SMAPI 通道传输，并让客户端继续维护已订阅地图的网络状态。物品始终留在原箱子。访问会话让远程箱子锁按在线玩家维持，不因玩家离开箱子所在地图而提前释放；同箱争用拒绝，其他箱子可同时使用。
- 源码核对了本机 Chest、CraftingPage、CraftingRecipe、Workbench、Multiplayer、GameServer、FarmerCollection、FarmerTeam、NetMutex 和 MultipleMutexRequest 的相关行为。处理关闭/换图/保存、权限与位置变化、延迟授权和分屏状态；兼容原有田园巧整的原生箱子锁检查。
- 四个最终 Release 构建均零警告、零错误；脚本语法检查通过，四个独立包及合集完整性与安装文件校验通过。没有编写测试，没有启动游戏。
- 已安装至正式 Mods，旧版本及设置备份：`.work/backups/installed-20260920-165947-325864`；升级前源码包备份：`.work/backups/storage-source-20260920-165836`。正式存档、语言、共享菜单、原有 Mod 配置及无关 Mod 共 12 个受保护文件摘要保持一致。证据：`.work/verification/storage-install.json`。
- StorageEase DLL SHA-256：`0a43fee55b6c5911740010509b39cc3885d67fa1bcdb56b24129361bfefb001e`。其他三个 Mod 的新 DLL 摘要记录在安装证据中。
- 验证边界：编译/静态源码核对与文件校验不证明运行成功。新 Mod 的 SMAPI/Harmony 实际加载、远程箱子 UI、中文输入/手柄、材料扣除与成品、跨地图初始同步/增量、多人同箱竞争、断线及分屏、第三方库存/制作 Mod 兼容均未实机验证。

## GardenEase 1.4.0 更名为田园巧整

- 菜单分类、整理标题、多人提示、控制台说明、manifest 显示名称和当前使用文档统一更名为“田园巧整”。历史验证记录保留当时名称。内部 ID、命令、配置和功能不变，仅名称更新，版本保持 1.4.0。
- Release 编译零警告、零错误。已更新正式 GardenEase 的 DLL、manifest 和说明，并同步另两个 Mod 说明中提及的名称。其余 16 个受保护文件（含正式存档、语言、共享配置和其他 Mod 二进制）摘要一致。
- 旧文件备份：`.work/backups/garden-rename-20260920-161133-182914`。安装证据：`.work/verification/garden-rename-install.json`。DLL SHA-256：`d21845bc56559940a9702ac5ac72be15b04278592239f10145a74a6ee3a886a4`。
- 未编写测试，未启动游戏；当前界面名称的实际显示尚未实机查看。

## GardenEase 1.4.0 多人同时整理

- 房主端由唯一整理会话改为按玩家 ID 管理独立会话，房主、客机和本地分屏可以同时整理；每人的顺序号、重复响应缓存、已知对象快照、选择与撤销记录独立。
- 选中的源格作为该会话的临时保留范围，覆盖地形和物体两层。选取、放置、交换、撤销都检查其他玩家的选择，树与采集器仍整体处理。目标光标不提前保留格子；房主在主线程顺序执行完整提交，以原对象和目标快照校验拒绝同格冲突。
- 每次成功搬移/交换/撤销后，仅让其他玩家涉及已改动格子的撤销记录失效，保留不相关记录。防止别人移动对象后又移回时，原来的撤销记录重新生效。状态心跳刷新撤销数量，携带当前命令顺序号，忽略早于当前操作的心跳回复。
- 退出、离开、断线、超时或单个请求异常只清理对应玩家会话，保存、过夜和返回标题统一清理。沿用原生箱子锁、原对象/采集器核对、果树与碰撞检查。
- 最终 Release 构建零警告、零错误；未编写测试，未启动游戏。源码检查覆盖会话隔离、同格选取/提交、交换/撤销占用、重复请求及释放分支，不代表真实多人运行验证。
- 已仅升级正式 GardenEase 至 1.4.0，旧版及旧源码包备份：`.work/backups/garden-concurrent-20260920-160941-501264`。存档、语言设置、共享菜单及其他 Mod 共 18 个文件摘要保持一致。
- 安装 DLL SHA-256：`8fe4cda86b90a19235e5498776520a9615e7043d78683bfb9564d10dfb961620`。安装证据：`.work/verification/garden-concurrent-install.json`。房主与参与整理者都需安装 1.4.0。
- 验证边界：多人同时选取、同格争用、连续交换/撤销、心跳延迟、断线释放及本地分屏均尚未实机验证。

## GardenEase 1.3.0 树木整理

- 新增原版 Tree / FruitTree 的种子、幼树、成树及普通树桩搬移、树木之间交换和撤销。移动原实例，保留树种、生长阶段、树龄、果实、苔藓、肥料和雷击等状态；不调用工具砍伐或重新种植流程。
- 普通树上的原版采集器/重型采集器与树作为整体移动，保留产物和剩余加工时间；预览读取纹理绘制树形与附件，不改写真实树的位置。采集器附属容器仍走现有互斥锁流程。
- 校验地图禁种区域、目标占用、果树间距与未成熟果树周围八格。按最终布局计算源格和目标格，避免把即将搬走的树或采集器当作障碍。拒绝正在倒下、销毁、隐藏的树；大树桩/大木头等资源块与自定义子类不纳入支持。
- 多人描述包含树种与采集器类型，房主核对原树/附件引用；中途拆装采集器或替换树木会拒绝搬移和撤销。异常恢复分别处理地形与附件两层，覆盖部分搬移失败的恢复路径。
- 源码核对依据本机 1.6.15 的 Tree、FruitTree、Object、GameLocation，确认树木字段、绘制矩形、果树间距/生长规则与地形字典回调。最终 Release 构建零警告、零错误。没有编写测试，没有启动游戏。
- 仅升级正式目录中的 GardenEase 至 1.3.0。旧版及升级前源码包备份：`.work/backups/garden-trees-20260920-155615-826246`。存档、语言设置、共享菜单及其他 Mod 共 18 个文件摘要保持一致。
- 安装 DLL SHA-256：`a27dd2b3e73e08cc8c440380e86757c498af117457365aeea9b4b5bd31cac7a6`。安装证据：`.work/verification/garden-trees-install.json`。房主和参与整理的玩家都需升级到 1.3.0。
- 验证边界：编译及安装校验不等于实机通过。树形预览、带采集器交换/连续撤销、果实和加工状态的保存加载、多人同步与异常恢复均尚未启动游戏验证。

## 多人适配：TravelEase 1.1.0 / GardenEase 1.2.0 / FishingEase 0.3.0

- 统一菜单移除全局单人限制，保留存档、对话、活动、工具等操作条件；快捷入口与原生芽苗标签均可用于多人。
- TravelEase 按分屏保存长按状态与返回位置，使用当前玩家住宅的 getFrontDoorSpot，并按目标 isStructure 调用原生传送；落脚检查避开其他玩家。传送不需要房主安装此 Mod。
- FishingEase 保持每个 BobberBar 独立状态，补充每屏当前配置；本机配置文件仍作为下次启动默认值。客户端只改变本机玩家的钓鱼小游戏，不要求房主安装。
- GardenEase 增加房主执行的请求协议。房主和参与整理者版本必须一致；一个农场会话同一时间由一名玩家使用，客户端只做预览和发送坐标/对象描述，不直接更改布局、物品或机器状态。
- 房主记录对象引用并核对格子变化、玩家身份/地图和放置条件，搬移前对箱子及嵌套附件容器取得原生 NetMutex。撤销沿用原对象，只恢复布局；不中断或回退其他玩家产生的库存、收获和加工变化。
- 会话通过随机 ID 和顺序号识别，重复命令返回原响应。心跳间隔 5 秒、无心跳 30 秒释放会话、操作 15 秒无响应关闭客户端整理。退出、离开地图、断线、保存过夜清理房主会话。
- 核对本机 SMAPI 4.5.2 的定向消息/分屏路由，以及游戏 NetMutex 的主线程请求与轮询行为。对未取得的锁停止操作并清理自身请求，不清除其他玩家的锁。
- 三个最终 Release 构建均为零警告、零错误；独立包、合集和源码包更新。没有编写测试，没有启动游戏。
- 已安装三个新版到正式 Mods，原版本和配置备份在 `.work/backups/installed-20260920-154549-576200`。正式存档、语言设置、共享菜单及 Mod 配置共 8 个文件摘要保持一致。
- 安装 manifest 版本和 DLL 均与 dist、Release 产物一致。SHA-256：TravelEase `e4db06279518d66592bc712e0a21199ec4d9c63f006f0248db8ce6fc4d5e240f`；GardenEase `41f3dca06fcecfa1e4f6c8ae7130f3c24adc8e10bf03c7d44831481fb779d292`；FishingEase `5137550cad538baeded03d8eeba90e7552404e1d07d3d11d623daf6fa17ba220`。安装证据：`.work/verification/multiplayer-install.json`。
- 验证边界：以上证明代码可编译、安装内容正确，不代表多人运行验证通过。房主/客机真实联机、本地分屏、网络高延迟/重试、掉线重连、多人箱子争用和保存加载均尚未实机验证。

## GardenEase 1.1.0 农场设施整理

- 新增洒水器、稻草人、道路/地板、围栏/大门、玩家箱子及普通地面加工机器的搬移、交换、预览和撤销；入口改为“开始整理农场”。物体优先于地面选中，搬移物体保留下方道路或空耕地。
- 复用原有对象，保留箱子库存/颜色等状态、洒水器附件、机器投入物/产出物、剩余加工时间与就绪状态。未调用敲除、复制、placementAction 或重新加工入口。正在使用的箱子及附属容器禁止搬移。
- 覆盖范围通过 GetSprinklerTiles 和 GetRadiusForScarecrow 获取；稻草人使用与游戏驱鸦一致的严格距离判断，洒水器计算喷头加成并过滤禁止洒水格。预览只平移覆盖格和绘制图像，不临时改写真实物体的位置。交换时显示另一件设施的拟覆盖范围。
- 核对了本机 1.6.15 Object、Chest、Fence、Flooring、LightSource、Farm/GameLocation 的相关源码：对象字典回调更新位置，地形回调更新道路连接，围栏绘制时按现有位置计算连接。实现额外迁移物体和围栏附件的灯光位置/标识；失败时尝试恢复双方及灯光。
- 最终 Release 构建成功，零警告、零错误。没有编写测试，没有启动游戏；覆盖范围视觉表现、实际交换/连续撤销、带物品箱子、加工中的机器和异常恢复均尚未实机验证。
- 已安装到 `/Applications/Stardew Valley.app/Contents/MacOS/Mods/GardenEase`，版本读回 1.1.0，DLL 与 Release、dist 一致，SHA-256 为 `ecf0fb8b53109b0b5b9506de9852c78d2e5b6f83c5f5c43211e9bd0a40624263`。
- 旧版备份：`.work/backups/garden-facilities-20260920-151040-815244`。安装前后正式存档、语言设置、共享菜单设置、TravelEase 和 FishingEase 共 14 个文件摘要一致。证据：`.work/verification/garden-facilities-install.json`。
- 范围仍限单人农场室外；特殊自定义类型、蟹笼、树上采集器、特殊房间设备、家具和建筑等未纳入此次支持。

## GardenEase 1.0.2 作物与耕地交换

- 目标已有普通耕地或作物时支持整格交换，保留双方原有 HoeDirt/Crop 对象；目标空地仍走普通搬移。界面以金色目标框及“可以交换”提示区分交换，避免在目标作物上叠加另一株作物预览。
- 交换前对两边分别检查可耕种区域、障碍和角色碰撞；棚架作物不能换到角色脚下。忽略被换出的耕地碰撞时，仍单独检查巨型作物等大型地形。
- 交换作为一条撤销记录保存双方对象；撤销前核对两格对象并重新检查两边的放置条件。搬移失败时尝试恢复双方，分别记录恢复异常。
- Release 编译通过，零警告、零错误。源码检查参照本机 1.6.15 游戏的占用检查及地形增删回调。
- 已安装到 `/Applications/Stardew Valley.app/Contents/MacOS/Mods/GardenEase`，读回版本为 1.0.2；安装 DLL 与 Release、dist 一致，SHA-256 为 `767b422fb5a34094cb0c717c0b1759319616492b91229c35040edb6499807537`。
- 原版本备份：`.work/backups/garden-swap-20260920-145920-324564`。安装前后存档、语言设置、共享菜单设置、TravelEase 和 FishingEase 文件摘要一致。证据：`.work/verification/garden-swap-install.json`。
- 沿用用户要求，没有编写测试，没有启动游戏；实际交换、连续撤销、异常恢复与界面效果尚未实机验证。

## GardenEase 1.0.1 镜头调整

- 整理模式进入时保留现有视角；选格在中央区域移动时镜头保持固定，越过边缘区域才平滑跟随。上下提示面板按 UI 与世界缩放换算预留空间，并限制镜头在地图范围内。选格、搬移和撤销采用同一套跟随规则。
- GardenEase 1.0.1 Release 编译通过，零警告、零错误；独立安装包和合集已更新。
- 仅更新正式目录 `/Applications/Stardew Valley.app/Contents/MacOS/Mods/GardenEase`，旧版本备份在 `.work/backups/garden-camera-20260920-144841-232698`。
- 安装后的 manifest 为 1.0.1；DLL 与 Release 构建和 dist 文件的 SHA-256 一致：`e4b1efcbed6e4fa507f5ecc4326d7556696033a7d1381fcaec5944397ae8edee`。
- 安装前后正式存档、语言设置、共享菜单设置、TravelEase 和 FishingEase 文件摘要一致。安装证据：`.work/verification/garden-camera-install.json`。
- 按用户要求，没有启动游戏，没有编写测试。实际镜头手感、窗口与缩放变化、地图边界及面板附近的运行表现尚未实机验证。

## 已完成

- TravelEase 1.0.0、GardenEase 1.0.0、FishingEase 0.2.0 的 Release 编译均为零警告、零错误。
- 在独立配置及存档副本目录中，SMAPI 成功同时加载三个 Mod，并通过跨 Mod API 注册「随心往返、田园随心、从容钓鱼」三个分类；菜单提供者为 TravelEase。
- 只加载 FishingEase 时，SMAPI 成功加载并由 FishingEase 自行提供统一菜单，仅注册「从容钓鱼」分类。
- 开发目录内走通安装迁移：旧 FarmEase 移出 Mods，回家设置迁入 TravelEase，菜单设置迁入共享 JSON，现有钓鱼配置逐字节保留。
- 三个独立安装包及合集 zip 完整性检查通过。安装包仅包含各 Mod 的 DLL、manifest 和说明，不包含游戏二进制、存档或用户配置。
- 安装脚本语法及构建脚本语法检查通过。
- 经用户授权重启后，已将三个 Mod 安装到正式游戏目录，旧 FarmEase 移出 Mods 并备份到 `.work/backups/installed-20260920-005659-701094`。
- 安装前后逐文件摘要一致：正式存档、语言设置、钓鱼配置。菜单和回家设置分别迁移，数值保留。
- 正式游戏重新启动成功，SMAPI 从 `/Applications/Stardew Valley.app/Contents/MacOS/Mods` 加载三个新 Mod；语言日志为 `zh-CN`，存档路径为 `/Users/zzz/.config/StardewValley/Saves`。
- 正式运行的 `farmease status` 确认菜单分类为「随心往返、田园随心、从容钓鱼」，由 TravelEase 提供统一入口。记录时仍在标题界面，尚未完成新版菜单实操。

加载证据保存在 `.work/verification` 及对应 `.work/*/config/StardewValley/ErrorLogs` 中。
游戏自身记录了 Galaxy 服务不可用和音频设备提示；上述加载过程中没有这三个 Mod 的异常。

## 尚未验证

- 新版菜单的完整点击、手柄导航、窗口缩放及功能往返。自动操作工具无法触发游戏按钮；开发窗口切换打断了人工检查，不能把成功注册分类当作界面验证通过。
- TravelEase、GardenEase 分别单独运行，以及仅安装其中两个的组合。
- 拆分后的实际传送、搬移和撤销流程；这些功能迁自旧 FarmEase，编译通过不能替代实机验证。
- 钓鱼完整实钓手感、各鱼种和特殊鱼饵场景；本次主要变更是统一菜单及预设保存入口。
- 联机、分屏、第三方同类 Mod 兼容性。

没有编写测试文件、测试项目或测试依赖。
