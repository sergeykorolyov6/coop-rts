# Глоссарий: дизайн → код

Как термин из спек называется в коде. Раньше таблица жила в `CLAUDE.md`; строки перенесены без изменений и разложены по темам.

Как вести:

- Новый термин из спеки — добавь сюда, прежде чем называть класс.
- Строка — термин и его имена в коде. Как механика работает, написано в её спеке (`mechanics/README.md` — указатель), а не здесь.
- Имя в коде поменялось — поправь строку в том же коммите.

## Игроки и владение

| В спеках | В коде |
|---|---|
| владелец, союзник | `OwnerId` (`PlayerId`), `Relation.Ally` |
| кошелёк ресурсов игрока | `Wallet` (`Player.Wallet`) |
| отношения игроков (союзник/враг/нейтрал) | `Relations`, `Relation` |
| метка для союзника, передача ресурса (`coop-tools.md`) | `PingCommand`, событие `Pinged`; `GiveResourcesCommand`, событие `ResourcesGiven` |

## Экономика: добыча и ленты

| В спеках | В коде |
|---|---|
| месторождение, клетка руды | `Deposit`, `OreCell` |
| бур | `Drill` |
| буфер бура | `Drill.Buffer` |
| рабочий, ручная добыча | `Worker`, `ManualMining` (у игрока рабочего делает строитель; `Worker` — компонент «строит и копает») |
| груз рабочего | `Worker.Cargo` |
| конвейер, лента | `Conveyor` |
| мост (конвейерный) | `ConveyorBridge` |
| склад | `Storage` |
| врезка лент, входы клетки, очередь входов (`belt-merge.md`, U2) | `Conveyor.LastFeeder` (`FromBack`, `FromLeft`, `FromRight`), `PlacementPreview.BeltInputs` |
| умная линия, изгиб, пролёт, прилипание конца (`smart-line.md`, U3) | `LinePlanner.Plan`, `LinePlan` (`Stop` — `LineStop`, `BrokenSpan`, `StuckToId`, `MergesIntoBelt`), `LineCell`, `LineCellKind` |
| перекладка ленты; снос рамкой (`relay-and-demolish.md`, U4) | `Construction.RelayableBelt`, `Building.Direction` меняется; `DemolishAreaCommand` (№ 18; что берёт рамка — `Takes`, тем же спрашивает превью); режим сноса — `PlacementController.BeginDemolish` |
| буры рамкой, сдвиг сетки (`drill-fill.md`, U9) | `DrillFill.Plan` → `DrillFillPlan` (`DrillFillCell`, `DrillFillCellKind`), правило `drill_fill_min_ore_pct` (`GameRules.DrillFillMinOrePercent`) |

## Стройка и строитель

| В спеках | В коде |
|---|---|
| стройка, совместная стройка, предел строителей | `Construction`, builders, `MaxBuilders` |
| превью размещения (силуэт до клика) | `PlacementPreview`, `DrillPreview` |
| ремонт | `Repair` |
| строитель игрока, возрождение | id `builder` (тег `builder`), `Player.BuilderRespawnTick`, события `BuilderDied`, `BuilderRespawned` |
| ремонтник, авторемонт | id `repairer` (тег `repairer`), `AutoRepair` |
| самоуничтожение (снос) | `DemolishCommand`, `Building.DemolishTicksLeft`, `DemolishSystem` |
| стройка без выделения строителя, режим размещения (`build-mode.md`, U1) | только Presentation: `BuildMenu`, `PlacementController`; строитель — `SimState.FindBuilder` |
| дальность стройки (луч), стройка на ходу, ожидание ресурса, нехватка при размещении (`builder-beam.md`, U7) | правило `build_range` (`GameRules.BuildRangeMilli`), `Worker.WaitingForResources`, `Worker.CurrentPlan`, событие `ConstructionWaiting`, `PlacementPreview.Shortfall`; луч — `BuildBeamView` |
| призрак разрушенного, восстановить (`rebuild-ghosts.md`, U5) | `Ghost`, `SimState.Ghosts`, `GhostRules` (`Leave`, `NearestFor`), `RebuildCommand` (№ 20), `PlannedBuild.OwnerId`, `UnitOrderKind.Rebuild`, `Construction.TickRebuild`, правило `ghost_limit`; вид — `GhostView` |

## Здания и производство

| В спеках | В коде |
|---|---|
| штаб | `Headquarters` (один на игрока одновременно, разрушенный можно отстроить; у ИИ — несколько) |
| завод, очередь производства | `Factory`, `Factory.Queue` |
| точка сбора завода, склад завода | `Factory.HasRally`, `SetRallyCommand`; `Factory.Stock`, колонка `supply` |
| повтор производства, неоплаченное место очереди; проходной склад, нужный ресурс (`factory-auto.md`, U6) | `Factory.Repeat`, `Factory.Paid`, `SetRepeatCommand` (№ 19); `Logistics.TryGive` → `BuildingStats.Needs` (считается при загрузке данных), `PassOn`, `Building.PassCursor`; кто кому отдаёт — `Logistics.Connects`; для стрелок у здания — `PlacementPreview.StockLinks`, `StockLinksView` |
| турель | `Turret` |
| поведение здания (штаб, бур, лента, мост, склад, маяк) | `BuildingStats.Behavior` (`BuildingBehavior`, колонка `behavior`). Логика смотрит на поведение, а не на id здания; id — только для арта (`BuildingIds` в Presentation) |

## Бой и приказы

| В спеках | В коде |
|---|---|
| бой, удар, урон, тревога «здание атаковано» | `Combat`, `PendingHit` (событие удара — `HitLanded`), `DamageRules`, `BuildingAttacked` |
| юнит атакован | `UnitAttacked` |
| очередь приказов (Shift) | `Unit.OrderQueue`, `QueuedOrder`, `Queued` у команд |
| идти с боем; идти, не отвлекаясь (`attack-move.md`, U8) | `AttackMoveCommand` (№ 17, приказ `UnitOrderKind.AttackMove`); `MoveCommand`; тег `scout` — `UnitTags.Scout` |
| юниты MVP: разведчик, пехотинец, стрелок, тяжёлый, осадный | id в `units.csv`: `scout`, `infantry`, `shooter`, `heavy`, `siege` |

## Карта, рельеф, точки интереса

| В спеках | В коде |
|---|---|
| точка интереса | `PointOfInterest` |
| лагерь крипов, крип, радиус агрессии, поводок, трофей | `CreepCamp`, `Creep`, `AggroRadius`, `LeashRadius`, `Trophy` |
| нейтральное здание, захват, пещера, маяк | `NeutralPlayerId`, `Capture`, `CaptureProgress`, `BuildingCaptured`; `cave`; `lighthouse`, `LighthouseLit` |
| уровень высоты, рампа; вид местности, порода (данные карты, T2 — на ходьбу, обзор и бой пока не влияют) | `MapState.LevelAt`, `MapState.IsRamp`, `MapSetup.MaxLevel`; `TerrainStats` (`terrain.csv`), `MapState.TerrainAt`, `GameData.TerrainBySymbol` |
| высоты (`mechanics/heights.md`): шаг через обрыв, рядом по высоте, уровень стояния и глаз, кто кого видит; раскрытие выстрелом; преимущество высоты; отказы стройки и способностей; высота земли в Presentation | `MapState.CanStep`, `MapState.LevelsJoin`, `MapState.EyeLevel`, `Sight` (`StandLevel`, `EyeLevel`, `LevelAdjacent`, `Sees`, `HighGroundBonus`); `Entity.RevealedUntilTick`, `RevealedToPlayerId`, правило `attack_reveal`; тег `ranged`, правило `high_ground_range_bonus`; `RejectReason.UnevenGround`, `OnRamp`, `NoVision`; `Ground` (`Y`, `Lift`, `LevelHeight`) |
| порода, шахтёр, копать, ход (`mechanics/terrain.md`) | `MapState.RockHp`, `IsDestructibleRock`, `IsOpaque`, `DamageRock`, `RockChecksum`; `TerrainStats.Hp`, `OreType`; id `miner`, тег `digger`, колонка `dig_rate` (`UnitStats.DigPerTick`); `DigCommand`, `UnitOrderKind.Dig`, `Digging.PlanTunnel`, `TickDig`; событие `RockDestroyed`; отказы `NoDigger`, `NothingToDig`, `CannotDig`; линия обзора `Sight.LineParent`, `LineOfSight` |
| здания по корзинам карты (поиск цели) | `BuildingIndex`, `SimState.BuildingListVersion` |
| карта «Каньоны» (первая большая, T5) | миссии `canyons2`, `canyons4`; `maps/canyons.txt` из `Tools/make_canyons_map.py` (заготовка: после первой ручной правки карты генератор удалить) |

## Враг: Чёрный Легион и крипы

| В спеках | В коде |
|---|---|
| ИИ Легиона, слот базы, точка сбора, волна, предупреждение о волне, атаковать с движением | `LegionAi`, `BaseSlot`, `Rally`, `Wave`, `WaveIncoming`, `UnitOrderKind.AttackMove` |
| завод Легиона | `legion_factory` |
| несколько баз Легиона, игроки базы, спящая база, расчётное время прихода волны (`legion-ai.md`, T5) | `MapSetup.LegionBases` (`LegionBaseSetup`), `MatchSetup.LegionPlayerIds`, `SimState.Legions`, `SimState.IsLegionPlayer`, `MissionLauncher.LegionPlayerIdFor` (9, 12, 13, …); `LegionAi.TargetPlayerIds`, `IsDormant`; `LegionAi.NextWaveArrivalTick`, `WaveIncoming.ArrivalTick`, `WaveLaunched.ArrivalTick`, `LegionPlayerId` в событиях волн |
| юниты Легиона: рабочий, рейдер, стрелок, громила; крипы: малый, большой | `legion_worker`, `legion_raider`, `legion_gunner`, `legion_brute`; `creep_small`, `creep_big` |
| пещерные юниты: зверь, плевун; босс | `cave_beast`, `cave_spitter`; `creep_boss` |

## Фракции и способности

| В спеках | В коде |
|---|---|
| фракция игрока, что строит фракция, роль фракции | `FactionStats` (`factions.csv`), `Player.Faction`, `PlayerFaction`, `FactionRole` |
| играбельные фракции: Синие Стены, Красные Молнии, Коричневые Мусорщики, Жёлтая Звезда | `blue_walls`, `red_lightning`, `brown_scavengers`, `yellow_star` (все четыре есть) |
| неиграбельные фракции: Серые Существа (нейтральные крипы), Чёрный Легион (враг-ИИ) | `grey_creatures`, `black_legion` |
| способность, Рывок, Перегрузка, Жажда; черты фракции (первый удар, регенерация, самопочинка, мародёрство) | `AbilityStats` (`abilities.csv`), `UseAbilityCommand`, `AbilitySystem`, `rl_blink`, `rl_overdrive`, `legion_frenzy`; `FactionTraits`, колонки `factions.csv` |
| юниты и здания Красных Молний: Искра, Бегун, Разрядник, Копейщик, Дуговик; Узел молний, Кузня, Тесла | `rl_builder`, `rl_runner`, `rl_striker`, `rl_lancer`, `rl_arc`; `rl_hq`, `rl_factory`, `rl_tesla` |
| стена (Синие Стены); Цитадель и Шип Легиона | `wall`; `legion_hq`, `legion_turret` |
| Мусорщики: Старьёвщик, Лом, Дробовик, Мортира, Жестянщик; Свалка, Мастерская, Малый бур, Смазчик, Ломомёт; склад здания, боеприпасы, ускорение буров | `bs_builder`, `bs_scrapper`, `bs_shotgun`, `bs_mortar`, `bs_repairbot`; `bs_hq`, `bs_factory`, `bs_drill_small`, `bs_pump`, `bs_scrap_gun`; `Building.Stock`, колонка `ammo`, поведение `booster` (`boost_pct`) |
| Жёлтая Звезда: герой (Звёздный рыцарь), опыт, уровень, аура, Звездопад, Воодушевление; Подмастерье, Оруженосец, Лучник, Послушник, Катапульта; Святилище, Казарма, Сторожевая башня | тег `hero`, `ys_hero`, `HeroSystem`, `Player.HeroXp`/`HeroLevel`/`HeroRespawnTick`, `hero_levels.csv`, события `HeroDied`/`HeroRespawned`/`HeroLevelUp`; `ys_nova` (nova), `ys_inspire` (inspire); `ys_builder`, `ys_squire`, `ys_archer`, `ys_acolyte`, `ys_catapult`; `ys_hq`, `ys_barracks`, `ys_tower` |
| путь героя (Воин, Маг, Покровитель); способность сработала; описание, снаряд, эффект | `HeroPathStats` (`hero_paths.csv`), `ChooseHeroPathCommand`, `Player.HeroPathId`; событие `AbilityUsed`; колонки `description`, `projectile` (юниты, здания), `effect` (способности) — только для UI |

## Миссия, сохранения, сеть

| В спеках | В коде |
|---|---|
| миссия (данные), цель, босс | `MissionStats`, `MissionCatalog`, `MissionLauncher`, `MissionGoal` (`DestroyLegion`, `KillBoss`), тег `boss` |
| миссия, победа, поражение | `MissionOutcome` (`Victory`, `Defeat`), событие `MissionEnded` |
| сохранение | `SaveGame`, `Simulation.CommandLog` |
| снимок состояния (`save-load.md`, T5) | `Snapshot`, `Simulation.WriteSnapshot`, `Simulation.FromSnapshot`, `SaveGame.Snapshot`, `SaveGame.TryRestoreSnapshot`; `MapState.LayoutChecksum`, `SimState.Seed` |
| сетевой кооп: хост, игрок, ход, задержка ввода, лобби, место, рассинхронизация (`network.md`) | `Relay` (хост: лобби, сбор ходов, сверка хешей), `LockstepClient`, `LockstepSettings` (`TurnTicks`, `DelayTurns`), `LobbyEntry`, `MatchStart`, `NetNotice` (`Desync`, `PlayerLeft`, `HostLost`); транспорт `IConnection` (`TcpConnection`, `LoopbackConnection`), `TcpServer`, `TcpConnector`; в Presentation `NetSession`, `SimRunner.Submit` |

## Данные и замеры

| В спеках | В коде |
|---|---|
| источники данных для загрузки | `GameDataSources` → `GameData.Parse` |
| замер стоимости тика, стенд масштаба | `TickCostProbe` (`dotnet test Game.Sim.Tests -c Release --filter TickCostProbe`); миссия `bench256` — карта из `Tools/make_bench_map.py`, не игровая; миссия `heights` — проверочная карта высот |
