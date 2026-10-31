using System.Runtime.CompilerServices;

// Tests mutate state through the same internal API that commands use;
// Presentation and Net get read-only access.
[assembly: InternalsVisibleTo("Game.Sim.Tests")]
