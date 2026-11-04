namespace Game.Sim;

// buildings.csv, "behavior": what the sim does with a building beyond the common columns. Code reacts
// to the behaviour, never to the building id, so a faction's own drill or headquarters is just a row.
// Producing (produces), shooting (damage) and stock (supply) follow from their own columns.
public enum BuildingBehavior : byte
{
    None,
    Headquarters,
    Drill,
    Conveyor,
    Bridge,
    Storage,
    Lighthouse,
}
