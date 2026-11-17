# CoopRTS — правила проекта для Claude Code

Кооперативная PvE-RTS для 1–4 друзей на Unity 6 / C#. Экономика по мотивам Mindustry (буры на месторождениях работают сами, конвейеры везут ресурс в штаб), ядро — точки интереса, у каждого игрока своя база, свои ресурсы и свой цвет. Туман войны общий для команды.

Источники правды:
- `PLAN.md` — дорожная карта, вехи, скоуп MVP. Вех в работе две: «T — Большая карта и рельеф» (код готов, остался плейтест T5; `Docs/Design/plan-terrain-and-factions.md`) и «U — Удобство управления» (код всех этапов U1–U9 готов 17.11.2026, остался плейтест; `Docs/Design/plan-convenience.md`). **Не добавляй фичи, фракции, юниты и механики, которых нет в этих вехах.** Если задача выходит за скоуп — скажи об этом, не делай молча.
- `Docs/Design/vision.md` — видение (портрет игрока, JTBD, фракции).
- `Docs/Design/mechanics/*.md` — спецификации механик. Если спека и `PLAN.md` расходятся, права спека.
- `Docs/Design/open-questions.md` — вопросы и решения геймдизайна.

**Приоритет источников геймдизайна: ответы брата → «CoopRTS v0.3.pdf» (`vision.md`) → решение Claude.** Мелкие вопросы решай сам по референсам (Mindustry, SC2, другие RTS), помечай «решение Claude» и записывай в `open-questions.md` (раздел «Решено Claude»). Не задавай вопросов, ответ на которые следует из ответов брата или PDF. Брату уходит вопрос, только если без него никак: новая механика или фракция, изменение скоупа, противоречие между ответами брата и PDF. То, что есть в `PLAN.md`, но нет ни в ответах брата, ни в PDF, — придумано Claude, это можно менять без вопросов.

## Архитектура (не нарушать)

Три сборки (Assembly Definitions):

- `Game.Sim` — **чистый C#, без `using UnityEngine`**. Вся игровая логика: карта, юниты, здания, ресурсы, точки интереса, бой, pathfinding, ИИ врага. Детерминированная, покрыта тестами.
- `Game.Presentation` — Unity: рендер, камера, ввод, UI (UI Toolkit). Читает состояние `Game.Sim`, никогда не мутирует его напрямую.
- `Game.Net` — сеть: сбор команд игроков, lockstep, рассылка (`mechanics/network.md`). Чистый C# (`noEngineReferences`), зависит от `Game.Sim`, не от `Game.Presentation`; собирается для `dotnet` через `Game.Net.Dotnet/Game.Net.csproj`, тесты — в `Game.Sim.Tests/Net`. Presentation отдаёт команды только через `SimRunner.Submit`.

Правила симуляции:
- Фиксированный тик: 20 тиков/сек. Симуляция продвигается только через `Simulation.Tick()` (класс не называется `Sim`, чтобы не конфликтовать с пространством имён `Game.Sim`).
- Единственный вход в симуляцию — команды: `ICommand { int PlayerId; int Tick; }` (MoveCommand, BuildCommand, AttackCommand …). Никаких прямых вызовов вроде `unit.MoveTo()` из Presentation. Команды сериализуемые: на журнале команд строятся сеть, реплеи и будущие сохранения.
- Единственный выход для UI, кроме состояния, — события сима (`SimEvent`: «место занято», «бур атакован», «стройка заморожена» …). Сим не знает про UI, Presentation не выводит события из состояния сам.
- **Многопользовательский с первого дня.** У каждой сущности есть владелец (`PlayerId`), у каждого игрока свой кошелёк ресурсов, отношения игроков (союзник/враг/нейтрал) — в состоянии матча. Общего командного кошелька нет. Ресурс зачисляется владельцу штаба, который его принял, — не владельцу бура или ленты. Кооп-сценарии тестируются в Sim несколькими игроками без сети.
- Конвейеры — очереди ресурсов на сегментах ленты с целочисленными позициями, не объект на каждую единицу ресурса.
- **Запрещено в `Game.Sim`:** `float`, `double`, `System.Random`, `DateTime`, `Time.*`, `Dictionary` с итерацией порядка (используй `List` / `SortedDictionary`), LINQ в горячих путях.
- Координаты — `int` в милли-клетках (1000 = одна клетка). Углы, расстояния — целочисленные (`IntMath.Sqrt`, таблицы). Fixed-point хелперы лежат в `Game.Sim/Math/`.
- Дробные скорости (добыча 0,25 ед./с и т.п.) — через целочисленные аккумуляторы, не через дроби. Пример — приложение к `Docs/Design/mechanics/drill.md`.
- Когда в спеке «кто первый» может случиться в один тик — порядок детерминированный (по id сущности), никогда не по порядку прихода команд по сети.
- Рандом — только `DeterministicRng` с seed из состояния матча.
- Каждый тик считается `StateHash` (xxHash по ключевым полям) — используется для отлова десинка в тестах и в сети.
- Всё изменяемое состояние матча пишется ещё и в `Snapshot` (сохранения, `mechanics/save-load.md`, T5): новое поле — и в `StateHasher`, и в `Snapshot`. Забытое ловят `SnapshotTests`: восстановленный матч должен идти шаг в шаг с оригиналом.
- Новая фракция или здание с уже известным поведением — строка в `factions.csv` / `buildings.csv`, без правки кода. В Sim не сравнивать `Stats.Id` с константами: поведение — колонка `behavior`, умения юнита — теги.
- Всё, что балансируется (статы, стоимость, тайминги, волны), лежит в данных. Брат пишет числа в человеческих единицах (секунды, ед./с, клетки) в `Docs/Design/*.csv`, конвертер `python3 Tools/csv2json.py` переводит их в тики и милли-единицы → `Assets/Data/*.json` (дробь после перевода — ошибка, не округление), Sim загружает только целые через `GameData.Parse`. Новая колонка CSV — сначала в схему конвертера. Конвертер же проверяет карты (достижимость стартов, руды и лагерей, рампы, здания на одном уровне) — карту с ошибкой он не пропустит. JSON не правят руками: хук сверяет его с CSV (`--check`). Не хардкодить числа в логике.

## Механики — только по спеке

- Реализуй механику, только если есть спека в `Docs/Design/mechanics/` (формат — `_TEMPLATE.md`).
- Порядок: сначала тесты — по одному на каждую строку Given/When/Then и Edge cases, имя теста начинается с её номера (`GWT1_…`, `Edge_PartialOre_…`); покажи их; потом реализация.
- Пункт спеки помечен ❓ (крупный вопрос к брату) — не реализуй его, сделай остальное и напиши, что ждёт ответа. Пункты с пометкой «решение Claude» реализуй как написано.
- Раздел «Что нужно от UI и арта» — чек-лист для Presentation; в отчёте отметь, что сделано, а что нет.

## Тесты

- `Game.Sim.Tests` — NUnit, лежит в корне репозитория, **вне `Assets/`**: Unity не должен видеть `bin/obj`. Код Sim — только в `Assets/Game.Sim/`; `Game.Sim.Dotnet/Game.Sim.csproj` (netstandard2.1, API как у Unity) подключает его для `dotnet`. Тесты обращаются к `internal` через `InternalsVisibleTo`. Любая логика в Sim добавляется вместе с тестом.
- Обязательный тест на детерминизм: один и тот же seed + одна и та же последовательность команд → одинаковый `StateHash` на каждом тике, дважды подряд.
- Запуск: `dotnet test Game.Sim.Tests` (сборка Sim — обычный .NET-проект, Unity не нужен). Перед тем как сказать «готово», прогони тесты.

## Стиль кода

- C# 10 (`Assets/csc.rsp`: `-langversion:10 -nullable:enable`; Unity 6 официально — C# 9), `nullable enable`, файловые `namespace`. **Исключение:** файлы с `MonoBehaviour` / `ScriptableObject` — только блочный `namespace X { }`: с файловым Unity 6000.5 не связывает скрипт с классом (`MonoScript.GetClass()` = null, в сцене «Missing Script»). Не использовать `record` и `init` — в netstandard2.1 нет `IsExternalInit`.
- Пространство имён: вся сборка Sim — `Game.Sim` (без подпространств по папкам: `Game.Sim.Math` перекрыл бы `System.Math`).
- Один публичный класс — один файл. Имена: `PascalCase` типы и методы, `_camelCase` приватные поля.
- Без «умных» абстракций на будущее. Интерфейс — только когда есть ≥2 реализации.
- Комментарии — зачем, а не что. По-русски или по-английски — не важно, но единообразно внутри файла.

## Глоссарий (дизайн → код)

| В спеках | В коде |
|---|---|
| месторождение, клетка руды | `Deposit`, `OreCell` |
| бур | `Drill` |
| буфер бура | `Drill.Buffer` |
| рабочий, ручная добыча | `Worker`, `ManualMining` (у игрока рабочего делает строитель; `Worker` — компонент «строит и копает») |
| строитель игрока, возрождение | id `builder` (тег `builder`), `Player.BuilderRespawnTick`, события `BuilderDied`, `BuilderRespawned` |
| ремонтник, авторемонт | id `repairer` (тег `repairer`), `AutoRepair` |
| точка сбора завода, склад завода | `Factory.HasRally`, `SetRallyCommand`; `Factory.Stock`, колонка `supply` |
| нейтральное здание, захват, пещера, маяк | `NeutralPlayerId`, `Capture`, `CaptureProgress`, `BuildingCaptured`; `cave`; `lighthouse`, `LighthouseLit` |
| самоуничтожение (снос) | `DemolishCommand`, `Building.DemolishTicksLeft`, `DemolishSystem` |
| юнит атакован | `UnitAttacked` |
| миссия (данные), цель, босс | `MissionStats`, `MissionCatalog`, `MissionLauncher`, `MissionGoal` (`DestroyLegion`, `KillBoss`), тег `boss` |
| сохранение | `SaveGame`, `Simulation.CommandLog` |
| стройка, совместная стройка, предел строителей | `Construction`, builders, `MaxBuilders` |
| превью размещения (силуэт до клика) | `PlacementPreview`, `DrillPreview` |
| очередь приказов (Shift) | `Unit.OrderQueue`, `QueuedOrder`, `Queued` у команд |
| ремонт | `Repair` |
| конвейер, лента | `Conveyor` |
| мост (конвейерный) | `ConveyorBridge` |
| груз рабочего | `Worker.Cargo` |
| штаб | `Headquarters` (один на игрока одновременно, разрушенный можно отстроить; у ИИ — несколько) |
| завод, очередь производства | `Factory`, `Factory.Queue` |
| бой, удар, урон, тревога «здание атаковано» | `Combat`, `PendingHit` (событие удара — `HitLanded`), `DamageRules`, `BuildingAttacked` |
| юниты MVP: разведчик, пехотинец, стрелок, тяжёлый, осадный | id в `units.csv`: `scout`, `infantry`, `shooter`, `heavy`, `siege` |
| пещерные юниты: зверь, плевун; босс | `cave_beast`, `cave_spitter`; `creep_boss` |
| юниты Легиона: рабочий, рейдер, стрелок, громила; крипы: малый, большой | `legion_worker`, `legion_raider`, `legion_gunner`, `legion_brute`; `creep_small`, `creep_big` |
| склад | `Storage` |
| ИИ Легиона, слот базы, точка сбора, волна, предупреждение о волне, атаковать с движением | `LegionAi`, `BaseSlot`, `Rally`, `Wave`, `WaveIncoming`, `UnitOrderKind.AttackMove` |
| завод Легиона | `legion_factory` |
| миссия, победа, поражение | `MissionOutcome` (`Victory`, `Defeat`), событие `MissionEnded` |
| турель | `Turret` |
| точка интереса | `PointOfInterest` |
| лагерь крипов, крип, радиус агрессии, поводок, трофей | `CreepCamp`, `Creep`, `AggroRadius`, `LeashRadius`, `Trophy` |
| владелец, союзник | `OwnerId` (`PlayerId`), `Relation.Ally` |
| кошелёк ресурсов игрока | `Wallet` (`Player.Wallet`) |
| отношения игроков (союзник/враг/нейтрал) | `Relations`, `Relation` |
| фракция игрока, что строит фракция, роль фракции | `FactionStats` (`factions.csv`), `Player.Faction`, `PlayerFaction`, `FactionRole` |
| поведение здания (штаб, бур, лента, мост, склад, маяк) | `BuildingStats.Behavior` (`BuildingBehavior`, колонка `behavior`). Логика смотрит на поведение, а не на id здания; id — только для арта (`BuildingIds` в Presentation) |
| способность, Рывок, Перегрузка, Жажда; черты фракции (первый удар, регенерация, самопочинка, мародёрство) | `AbilityStats` (`abilities.csv`), `UseAbilityCommand`, `AbilitySystem`, `rl_blink`, `rl_overdrive`, `legion_frenzy`; `FactionTraits`, колонки `factions.csv` |
| юниты и здания Красных Молний: Искра, Бегун, Разрядник, Копейщик, Дуговик; Узел молний, Кузня, Тесла | `rl_builder`, `rl_runner`, `rl_striker`, `rl_lancer`, `rl_arc`; `rl_hq`, `rl_factory`, `rl_tesla` |
| стена (Синие Стены); Цитадель и Шип Легиона | `wall`; `legion_hq`, `legion_turret` |
| Мусорщики: Старьёвщик, Лом, Дробовик, Мортира, Жестянщик; Свалка, Мастерская, Малый бур, Смазчик, Ломомёт; склад здания, боеприпасы, ускорение буров | `bs_builder`, `bs_scrapper`, `bs_shotgun`, `bs_mortar`, `bs_repairbot`; `bs_hq`, `bs_factory`, `bs_drill_small`, `bs_pump`, `bs_scrap_gun`; `Building.Stock`, колонка `ammo`, поведение `booster` (`boost_pct`) |
| Жёлтая Звезда: герой (Звёздный рыцарь), опыт, уровень, аура, Звездопад, Воодушевление; Подмастерье, Оруженосец, Лучник, Послушник, Катапульта; Святилище, Казарма, Сторожевая башня | тег `hero`, `ys_hero`, `HeroSystem`, `Player.HeroXp`/`HeroLevel`/`HeroRespawnTick`, `hero_levels.csv`, события `HeroDied`/`HeroRespawned`/`HeroLevelUp`; `ys_nova` (nova), `ys_inspire` (inspire); `ys_builder`, `ys_squire`, `ys_archer`, `ys_acolyte`, `ys_catapult`; `ys_hq`, `ys_barracks`, `ys_tower` |
| путь героя (Воин, Маг, Покровитель); способность сработала; описание, снаряд, эффект | `HeroPathStats` (`hero_paths.csv`), `ChooseHeroPathCommand`, `Player.HeroPathId`; событие `AbilityUsed`; колонки `description`, `projectile` (юниты, здания), `effect` (способности) — только для UI |
| источники данных для загрузки | `GameDataSources` → `GameData.Parse` |
| замер стоимости тика, стенд масштаба | `TickCostProbe` (`dotnet test Game.Sim.Tests -c Release --filter TickCostProbe`); миссия `bench256` — карта из `Tools/make_bench_map.py`, не игровая; миссия `heights` — проверочная карта высот |
| здания по корзинам карты (поиск цели) | `BuildingIndex`, `SimState.BuildingListVersion` |
| уровень высоты, рампа; вид местности, порода (данные карты, T2 — на ходьбу, обзор и бой пока не влияют) | `MapState.LevelAt`, `MapState.IsRamp`, `MapSetup.MaxLevel`; `TerrainStats` (`terrain.csv`), `MapState.TerrainAt`, `GameData.TerrainBySymbol` |
| высоты (`mechanics/heights.md`): шаг через обрыв, рядом по высоте, уровень стояния и глаз, кто кого видит; раскрытие выстрелом; преимущество высоты; отказы стройки и способностей; высота земли в Presentation | `MapState.CanStep`, `MapState.LevelsJoin`, `MapState.EyeLevel`, `Sight` (`StandLevel`, `EyeLevel`, `LevelAdjacent`, `Sees`, `HighGroundBonus`); `Entity.RevealedUntilTick`, `RevealedToPlayerId`, правило `attack_reveal`; тег `ranged`, правило `high_ground_range_bonus`; `RejectReason.UnevenGround`, `OnRamp`, `NoVision`; `Ground` (`Y`, `Lift`, `LevelHeight`) |
| порода, шахтёр, копать, ход (`mechanics/terrain.md`) | `MapState.RockHp`, `IsDestructibleRock`, `IsOpaque`, `DamageRock`, `RockChecksum`; `TerrainStats.Hp`, `OreType`; id `miner`, тег `digger`, колонка `dig_rate` (`UnitStats.DigPerTick`); `DigCommand`, `UnitOrderKind.Dig`, `Digging.PlanTunnel`, `TickDig`; событие `RockDestroyed`; отказы `NoDigger`, `NothingToDig`, `CannotDig`; линия обзора `Sight.LineParent`, `LineOfSight` |
| сетевой кооп: хост, игрок, ход, задержка ввода, лобби, место, рассинхронизация (`network.md`) | `Relay` (хост: лобби, сбор ходов, сверка хешей), `LockstepClient`, `LockstepSettings` (`TurnTicks`, `DelayTurns`), `LobbyEntry`, `MatchStart`, `NetNotice` (`Desync`, `PlayerLeft`, `HostLost`); транспорт `IConnection` (`TcpConnection`, `LoopbackConnection`), `TcpServer`, `TcpConnector`; в Presentation `NetSession`, `SimRunner.Submit` |
| играбельные фракции: Синие Стены, Красные Молнии, Коричневые Мусорщики, Жёлтая Звезда | `blue_walls`, `red_lightning`, `brown_scavengers`, `yellow_star` (все четыре есть) |
| неиграбельные фракции: Серые Существа (нейтральные крипы), Чёрный Легион (враг-ИИ) | `grey_creatures`, `black_legion` |
| несколько баз Легиона, игроки базы, спящая база, расчётное время прихода волны (`legion-ai.md`, T5) | `MapSetup.LegionBases` (`LegionBaseSetup`), `MatchSetup.LegionPlayerIds`, `SimState.Legions`, `SimState.IsLegionPlayer`, `MissionLauncher.LegionPlayerIdFor` (9, 12, 13, …); `LegionAi.TargetPlayerIds`, `IsDormant`; `LegionAi.NextWaveArrivalTick`, `WaveIncoming.ArrivalTick`, `WaveLaunched.ArrivalTick`, `LegionPlayerId` в событиях волн |
| снимок состояния (`save-load.md`, T5) | `Snapshot`, `Simulation.WriteSnapshot`, `Simulation.FromSnapshot`, `SaveGame.Snapshot`, `SaveGame.TryRestoreSnapshot`; `MapState.LayoutChecksum`, `SimState.Seed` |
| метка для союзника, передача ресурса (`coop-tools.md`) | `PingCommand`, событие `Pinged`; `GiveResourcesCommand`, событие `ResourcesGiven` |
| карта «Каньоны» (первая большая, T5) | миссии `canyons2`, `canyons4`; `maps/canyons.txt` из `Tools/make_canyons_map.py` (заготовка: после первой ручной правки карты генератор удалить) |
| стройка без выделения строителя, режим размещения (`build-mode.md`, U1) | только Presentation: `BuildMenu`, `PlacementController`; строитель — `SimState.FindBuilder` |
| дальность стройки (луч), стройка на ходу, ожидание ресурса, нехватка при размещении (`builder-beam.md`, U7) | правило `build_range` (`GameRules.BuildRangeMilli`), `Worker.WaitingForResources`, `Worker.CurrentPlan`, событие `ConstructionWaiting`, `PlacementPreview.Shortfall`; луч — `BuildBeamView` |
| перекладка ленты; снос рамкой (`relay-and-demolish.md`, U4) | `Construction.RelayableBelt`, `Building.Direction` меняется; `DemolishAreaCommand` (№ 18; что берёт рамка — `Takes`, тем же спрашивает превью); режим сноса — `PlacementController.BeginDemolish` |
| умная линия, изгиб, пролёт, прилипание конца (`smart-line.md`, U3) | `LinePlanner.Plan`, `LinePlan` (`Stop` — `LineStop`, `BrokenSpan`, `StuckToId`, `MergesIntoBelt`), `LineCell`, `LineCellKind` |
| буры рамкой, сдвиг сетки (`drill-fill.md`, U9) | `DrillFill.Plan` → `DrillFillPlan` (`DrillFillCell`, `DrillFillCellKind`), правило `drill_fill_min_ore_pct` (`GameRules.DrillFillMinOrePercent`) |
| идти с боем; идти, не отвлекаясь (`attack-move.md`, U8) | `AttackMoveCommand` (№ 17, приказ `UnitOrderKind.AttackMove`); `MoveCommand`; тег `scout` — `UnitTags.Scout` |
| врезка лент, входы клетки, очередь входов (`belt-merge.md`, U2) | `Conveyor.LastFeeder` (`FromBack`, `FromLeft`, `FromRight`), `PlacementPreview.BeltInputs` |
| повтор производства, неоплаченное место очереди; проходной склад, нужный ресурс (`factory-auto.md`, U6) | `Factory.Repeat`, `Factory.Paid`, `SetRepeatCommand` (№ 19); `Logistics.TryGive` → `BuildingStats.Needs` (считается при загрузке данных), `PassOn`, `Building.PassCursor` |
| призрак разрушенного, восстановить (`rebuild-ghosts.md`, U5) | `Ghost`, `SimState.Ghosts`, `GhostRules` (`Leave`, `NearestFor`), `RebuildCommand` (№ 20), `PlannedBuild.OwnerId`, `UnitOrderKind.Rebuild`, `Construction.TickRebuild`, правило `ghost_limit`; вид — `GhostView` |

Новый термин из спеки — добавь сюда, прежде чем называть класс.

## Рабочий процесс

- Перед изменением: прочитай `PLAN.md`, найди текущую веху, работай только по ней.
- Маленькие атомарные изменения. Одна задача — один коммит с понятным сообщением.
- После изменения кода Sim — прогон тестов. После изменения Presentation — опиши, что нужно проверить руками в редакторе.
- Сцены, префабы, компоненты — через Unity MCP (MCP for Unity от CoplayDev, тулы `mcp__UnityMCP__*`). Подключение — `.mcp.json` в корне: HTTP `127.0.0.1:8080/mcp`, сервер поднимает сама Unity (Window → MCP for Unity, Start Server). Кнопку **Configure** для Claude Code в этой панели не нажимать: она удаляет `.mcp.json`-регистрацию и прописывает сервер локально по пути папки, а сессии Claude работают в worktree с другим путём. После любой правки сцены: прочитай консоль Unity, дождись компиляции, перечисли, что создал. **Если MCP недоступен** — не притворяйся: напиши пошаговую инструкцию для человека («создай GameObject X, повесь компонент Y, выставь поле Z»).
- Горячие клавиши — `Docs/Design/hotkeys.md`. W, A, S, D, Q, E — только камера; новую команду туда не вешать.
- Цвет объектов в Presentation — цвет владельца (все объекты игрока, включая недостроенные), форма — по типу. Не делай материалы по фракциям.
- Перед Unity-кодом используй скиллы `unity:*` (плагин unity-claude-skills) — они актуальнее твоей памяти по API Unity 6. Если скилл и память расходятся, прав скилл.
- Повторяемые workflow — в `.claude/skills/<name>/SKILL.md`: есть `add-unit`; планируются `implement-mechanic`, `add-poi`, `desync-hunt`, `scene-check`, `balance-pass`. Если делаешь одно и то же второй раз — предложи оформить скилл.
- Хуки (`.claude/settings.json` → `.claude/hooks/sim-check.sh`) запускают `dotnet test` и проверку на `float`/`double`/`Random`/`DateTime`/`Time.`/`UnityEngine`/`Dictionary`/`HashSet` в `Game.Sim` после каждой правки. Эти слова не пишем в Sim даже в комментариях. Красный хук — чинить сразу, не откладывать.
- Не трогай `ProjectSettings/`, `Packages/manifest.json` без явной просьбы.
- Если непонятно, как должна работать механика, — реши по приоритету источников (см. выше) и запиши решение. Спрашивай, только если это крупный вопрос.

## Структура репозитория

```
CLAUDE.md
PLAN.md
.claude/
  skills/              скиллы проекта (add-unit, …)
  settings.json        хуки
  hooks/               скрипты хуков (sim-check.sh)
Assets/
  csc.rsp              -langversion:10 -nullable:enable для всех сборок
  Game.Sim/            чистый C# (asmdef без Unity-зависимостей)
    Core/              Simulation, Tick loop, Commands, SimEvents, StateHash, Players
    Map/               Grid, Tiles, Deposits, PointsOfInterest, FlowField, FogOfWar
    Entities/          Units, Buildings, Construction, Repair, Components
    Economy/           Wallets, Drills, Conveyors, ManualMining, Production
    Combat/            Damage, Targeting
    AI/                EnemyAI, Waves
    Math/              IntMath, FixedVector, DeterministicRng
    Balance/           GameData и статы из Assets/Data/*.json, строгий JSON-парсер
  Game.Presentation/   Unity-слой
  Game.Net/
  Data/                json с балансом (генерируется из Docs/Design/*.csv)
  Resources/Terrain/   арт рельефа: текстуры земли, шейдер Ground, модели породы, руды и декора (имена файлов — см. TerrainView)
Game.Sim.Dotnet/       Game.Sim.csproj — собирает Assets/Game.Sim/**/*.cs для dotnet
Game.Sim.Tests/        NUnit-проект (вне Assets/, Unity его не видит)
  Mechanics/           тесты по спекам (DrillTests …)
Docs/
  Design/
    vision.md          видение
    open-questions.md  вопросы и решения геймдизайна
    mechanics/         спеки механик (_TEMPLATE.md, drill.md, …)
    *.csv              таблицы чисел в человеческих единицах (rules.csv — общие числа механик)
    maps/*.txt         карты текстом (легенда в начале файла) → Assets/Data/maps/*.json
    maps/*.tmj         или карта из редактора Tiled (Tools/tiled.py; набор тайлов cooprts.tsj/.png) — у карты один файл
Tools/                 csv2json и прочие скрипты
  art/                 обработка сгенерированного арта: tile.py (бесшовная текстура земли), bake.py (GLB → упрощённая модель + текстура)
ArtSource/             сырьё генерации (эскизы, GLB), не в git
```
