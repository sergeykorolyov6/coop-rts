using System.Collections.Generic;
using System.IO;

namespace Game.Sim;

// The only way into the simulation. Commands are logged and serialized: the network,
// replays and saves are all built on the command log.
public interface ICommand
{
    int PlayerId { get; }

    // The tick on which the command executes.
    int Tick { get; }

    // Stable id in the serialized log. Never reuse an id after its command is removed.
    int TypeId { get; }

    // Everything except PlayerId, Tick and TypeId, which CommandCodec writes itself.
    void WritePayload(BinaryWriter writer);

    // Runs inside Simulation.Tick. Validates against the state at execution time, not
    // at issue time: the world may have changed while the command travelled. A rejected
    // command reports through events instead of throwing.
    void Execute(SimState state, List<SimEvent> events);
}
