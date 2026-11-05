using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Game.Net;
using Game.Sim;

// Usage: dotnet run --project Tools/NetBot -- [host] [port] [faction] [seconds]
// Joins the host, picks the faction, plays the match for the given seconds (its builder walks
// around), prints StateHash every 5 s and any notice. Exit code 1 on desync or lost host.
string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : LockstepSettings.DefaultPort;
string faction = args.Length > 2 ? args[2] : "red_lightning";
int seconds = args.Length > 3 ? int.Parse(args[3]) : 60;

string root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "PLAN.md")))
    root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar)) ?? throw new DirectoryNotFoundException("PLAN.md");
string data = Path.Combine(root, "Assets", "Data");
string Read(string name) => File.ReadAllText(Path.Combine(data, name));

// The same texts in the same order as SimRunner.ContentTexts.
string[] tables = { "units", "buildings", "creep_camps", "rules", "legion", "waves", "factions", "abilities", "hero_levels", "hero_paths", "missions" };
var texts = tables.Select(t => Read(t + ".json")).ToList();
var maps = Directory.GetFiles(Path.Combine(data, "maps"), "*.json").Select(Path.GetFileNameWithoutExtension).ToList();
maps.Sort(string.CompareOrdinal);
texts.AddRange(maps.Select(m => m + "\n" + Read(Path.Combine("maps", m + ".json"))));
ulong content = ContentHash.Of(texts);

GameData gameData = GameData.Parse(new GameDataSources
{
    Units = Read("units.json"), Buildings = Read("buildings.json"), CreepCamps = Read("creep_camps.json"),
    Rules = Read("rules.json"), Legion = Read("legion.json"), Waves = Read("waves.json"), Factions = Read("factions.json"),
    Abilities = Read("abilities.json"), HeroLevels = Read("hero_levels.json"), HeroPaths = Read("hero_paths.json"),
});
MissionCatalog catalog = MissionCatalog.Parse(Read("missions.json"));

var connector = new TcpConnector(host, port);
var watch = Stopwatch.StartNew();
while (!connector.IsDone && watch.Elapsed.TotalSeconds < 10)
    Thread.Sleep(10);
IConnection? connection = connector.Connection;
if (connection == null)
{
    Console.WriteLine($"Не подключился к {host}:{port}: {connector.Error ?? "нет ответа"}");
    return 1;
}
var client = new LockstepClient(connection, "Бот", content, GameCommands.CreateCodec());
bool picked = false;
Console.WriteLine($"Подключён к {host}:{port}, жду лобби и старта…");

int Report()
{
    foreach (NetNotice n in client.TakeNotices())
    {
        Console.WriteLine($"[{n.Kind}] place {n.Slot} tick {n.Tick} {n.Text}");
        if (n.Kind == NetNoticeKind.Desync || n.Kind == NetNoticeKind.HostLost || n.Kind == NetNoticeKind.Rejected)
            return 1;
    }
    return 0;
}

while (client.Start == null)
{
    client.Poll();
    if (Report() != 0 || client.Disconnected)
        return 1;
    if (!picked && client.Slot > 0)
    {
        client.PickFaction(faction);
        picked = true;
        Console.WriteLine($"Место {client.Slot}, фракция {faction}");
    }
    Thread.Sleep(10);
}

MatchStart start = client.Start;
MissionStats mission = catalog.Find(start.MissionId)!;
MatchSetup setup = MissionLauncher.CreateSetup(gameData, mission, Read(Path.Combine("maps", mission.Map + ".json")), start.Seed, start.Factions);
// The host may go on from a save: every machine restores it up to its tick (save-load.md).
var sim = start.Save != null ? SaveGame.Read(start.Save).Restore(setup, GameCommands.CreateCodec()) : new Simulation(setup);
Console.WriteLine($"Старт: {start.MissionId}, seed {start.Seed}, фракции {string.Join(", ", start.Factions)}, с тика {sim.State.Tick}");

var clock = Stopwatch.StartNew();
long next = 0;
int me = client.Slot;
int lastOrder = -100;
while (clock.Elapsed.TotalSeconds < seconds)
{
    client.Poll();
    if (Report() != 0)
        return 1;
    if (client.Disconnected)
        return 1;
    while (clock.ElapsedMilliseconds >= next && client.TryStep(sim))
    {
        next += 1000 / Simulation.TicksPerSecond;
        int tick = sim.State.Tick;
        if (tick - lastOrder >= 5 * Simulation.TicksPerSecond)
        {
            lastOrder = tick;
            Unit? builder = sim.State.FindBuilder(me);
            if (builder != null)
            {
                int step = (tick / 100 % 2 == 0 ? 1 : -1) * 3000;
                client.Submit(new MoveCommand(me, 0, new[] { builder.Id }, builder.X + step, builder.Y + step / 2));
            }
            Console.WriteLine($"тик {tick} hash {sim.StateHash:X16}");
        }
    }
    Thread.Sleep(2);
}
Console.WriteLine($"Готово: тик {sim.State.Tick}, рассинхрона нет");
client.Leave();
return 0;
