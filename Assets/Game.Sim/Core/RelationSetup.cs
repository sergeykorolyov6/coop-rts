namespace Game.Sim;

// One symmetric relation between two players at match start.
public readonly struct RelationSetup
{
    public RelationSetup(int playerA, int playerB, Relation relation)
    {
        PlayerA = playerA;
        PlayerB = playerB;
        Relation = relation;
    }

    public int PlayerA { get; }
    public int PlayerB { get; }
    public Relation Relation { get; }
}
