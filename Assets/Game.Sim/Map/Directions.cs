namespace Game.Sim;

public static class Directions
{
    public static int Dx(this Direction d) => d == Direction.Right ? 1 : d == Direction.Left ? -1 : 0;
    public static int Dy(this Direction d) => d == Direction.Up ? 1 : d == Direction.Down ? -1 : 0;
}
