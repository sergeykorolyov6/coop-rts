# Работа с Claude Code: задачи, инструменты, материалы

Правила, которым Claude следует в каждой сессии, — в `CLAUDE.md`. Здесь — для человека: как ставить задачи, что подключено и зачем. Состояние на 17.11.2026; до этого раздел жил в `PLAN.md` (разделы 8–10).

## Как ставить задачи

- Задачи — по вехам, по одной: «U3: реализовать умную линию по `smart-line.md`».
- **Механику — только по спеке.** Первым шагом Claude переводит строки Given/When/Then и Edge cases в тесты и показывает их, потом пишет реализацию. Мелкие вопросы спеки Claude решает сам по порядку «ответы брата → PDF → референсы» и помечает «решение Claude».
- Сим-логику и тесты делегировать полностью. `Game.Sim` — обычный .NET-проект, Claude Code гоняет `dotnet test` без Unity: цикл секунды, не минуты.
- Presentation и сцены — через Unity MCP. То, что осталось проверить глазами или мышью, Claude записывает в `PENDING.md`.
- Крупные геймдизайн-решения (новая механика, фракция, скоуп) — за братом. Мелкие Claude решает сам и записывает в `Docs/Design/decisions.md`; брат может поправить.
- Баланс — через данные, не через код.
- Раз в веху: ревью архитектуры Sim на утечки `float` и недетерминизма; ревью контекста (`/context`) — скиллы и `CLAUDE.md` не должны съедать бюджет; сверка `PLAN.md` и `PENDING.md` с журналом вех.

## Что подключено

### Unity MCP

Без него Claude Code видит только файлы на диске. С ним — сцену, иерархию, компоненты, консоль, компиляцию, тесты Unity.

Стоит **CoplayDev/unity-mcp** (сообщество, MIT, бесплатно; при установке 31.10.2026 — v10.3.0). Пакет в проекте, `uv`, регистрация — `.mcp.json` в корне: HTTP `127.0.0.1:8080/mcp`. Сервер поднимает сама Unity (Window → MCP for Unity, Start Server; включён Auto-Start). Кнопку **Configure** для Claude Code в этой панели не нажимать — почему, сказано в `CLAUDE.md`.

Альтернатива — официальный Unity MCP в пакете `com.unity.ai.assistant`: нужна подписка Unity AI и проект в Unity Cloud. Не используем.

Что помнить при проверке через MCP:

- Если Unity не в фокусе, Play стоит (`Run In Background` выключен). На время сеанса включается `Application.runInBackground = true`; в редакторе это пишется в `ProjectSettings`, поэтому после проверки вернуть `PlayerSettings.runInBackground = false`. Настройки проекта в git не идут.
- Кадры в секунду через MCP не мерить: редактор без фокуса тормозит кадры.
- Живую мышь MCP не заменяет: тестовая мышь сбрасывается между вызовами. Такие проверки — в `PENDING.md`.
- Скриншоты MCP — в `Temp/Screenshots`.

### Скиллы Unity

Проблема, которую они решают: Claude уверенно пишет Unity-код под устаревшие API, который компилируется и выглядит правильно. Скиллы — выжимка актуальной документации с паттернами «неправильно/правильно».

Стоит плагин **Nice-Wolf-Studio/unity-claude-skills** (v1.4.0, `unity@nice-wolf-local`, scope user), скиллы `unity:*`. В его репозитории нет `marketplace.json`, поэтому установка такая: клон в `~/claude-plugins/unity-claude-skills`, свой `.claude-plugin/marketplace.json` рядом, затем `claude plugin marketplace add` и `claude plugin install`.

### Скиллы проекта

Скилл проекта — повторяемый многошаговый workflow. Лежат в `.claude/skills/<name>/SKILL.md` (в другом месте Claude Code их не видит). Пишем, когда workflow повторился дважды.

- `add-unit` — **есть.** Из строки в `Docs/Design/units.csv`: данные в Sim, тест, модель и UI.
- `implement-mechanic` — из спеки в `Docs/Design/mechanics/`: проверить, что нет открытых вопросов → тесты из Given/When/Then и Edge cases → реализация в Sim → `SimEvent` для уведомлений → список UI-требований.
- `add-poi` — то же для точки интереса (вероятно, частный случай `implement-mechanic`).
- `desync-hunt` — прогнать реплей на двух сборках, найти первый тик с расхождением `StateHash`, сравнить состояние, найти виновника (`float`, порядок итерации, рандом вне RNG).
- `scene-check` — после сборки сцены через MCP: объекты без компонентов, ошибки консоли, отсутствующие ссылки.
- `balance-pass` — брат меняет данные → Claude запускает симуляцию N волн без графики и выдаёт таблицу: сколько прожила база при разных составах.

### Хуки

`.claude/settings.json` → `.claude/hooks/sim-check.sh`, после каждой правки файла:

- правка в `Game.Sim`, `Game.Net` или тестах → поиск запрещённого для детерминизма (`float`, `double`, `System.Random`, `DateTime`, `Time.`, `UnityEngine`, `Dictionary`, `HashSet`) и `dotnet test Game.Sim.Tests`;
- правка CSV, карт, скриптов `Tools/` или `Assets/Data/*.json` → `python3 Tools/csv2json.py --check` (JSON должен совпадать с CSV) и тесты конвертера.

Красный хук чинится сразу. Детерминизм — как линтер, а не как надежда.

### Генерация арта

В начале проекта генераторы считались ненужными: графика геометрическая. С 15.11.2026 по просьбе Сергея ими сделаны рельеф, меню и иконки; юниты и здания остаются на примитивах.

- Рельеф: fal, эскиз FLUX 2 Pro → модель Trellis 2; модели упрощает `Tools/art/bake.py`, бесшовные текстуры земли делает `Tools/art/tile.py`. Сырьё лежит в `ArtSource/` и в git не идёт.
- Иконки: `Tools/ui_icons.csv` → fal `flux-2/flash` → `Tools/ui_icons.py`.

### Облачные сессии

В облачной сессии Claude Code dotnet ставится через apt (`dotnet-sdk-8.0`), Unity там нет. Sim и тесты делаются полностью, Presentation пишется без редактора и проверяется потом в локальной сессии с Unity MCP.

### Что не нужно

- GitHub MCP — Claude Code работает с git напрямую.
- Photon Quantum — готовый детерминированный движок для Unity; смотреть, только если свой lockstep не выдержит живую сеть.

## Материалы

- Unity Learn — Junior Programmer pathway (основы, бесплатно).
- Catlike Coding — туториалы по Unity/C#, в т. ч. hex map (база для сетки и fog of war).
- Red Blob Games — pathfinding, flow fields, интерактивно.
- GDC-статья «1500 Archers on a 28.8» (Age of Empires) — каноническое описание lockstep.
- Robert Nystrom, «Game Programming Patterns» — бесплатно онлайн; главы Command, Game Loop, Update Method, Event Queue — ровно про эту архитектуру.
- Mindustry — поиграть вдвоём пару вечеров: главный референс экономики (буры, буферы, логистика).
- Kenney.nl — CC0 ассеты для заглушек.
