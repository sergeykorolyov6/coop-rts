namespace Game.Net;

// First byte of every message (network.md). Numbers are part of the protocol version.
public enum MessageType : byte
{
    Hello = 1,          // player → host: protocol version, content hash, name
    Reject = 2,         // host → player: why not, then the host closes
    Lobby = 3,          // host → player: your slot, mission, everyone's slot, name, faction
    PickFaction = 4,    // player → host
    PickMission = 5,    // slot 1 → host
    StartRequest = 6,   // slot 1 → host: seed
    Start = 7,          // host → all: mission, seed, faction per slot
    Input = 8,          // player → host: turn, own commands
    Turn = 9,           // host → all: turn, everyone's commands with their slot
    Hash = 10,          // player → host: tick, StateHash
    Desync = 11,        // host → all: tick where the hashes differed
    PlayerLeft = 12,    // host → all: slot
}
