using System;

namespace Game.Sim;

// Per-player resources. There is no team wallet: income goes to whoever's
// headquarters accepted the resource.
public sealed class Wallet
{
    private readonly int[] _amounts;

    internal Wallet(int resourceTypeCount)
    {
        _amounts = new int[resourceTypeCount];
    }

    public int ResourceTypeCount => _amounts.Length;

    public int Get(int resourceType) => _amounts[CheckType(resourceType)];

    internal void Add(int resourceType, int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Use TrySpend to take resources.");
        _amounts[CheckType(resourceType)] = checked(_amounts[resourceType] + amount);
    }

    // All-or-nothing: on false the wallet is unchanged.
    internal bool TrySpend(int resourceType, int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Must not be negative.");
        if (_amounts[CheckType(resourceType)] < amount)
            return false;
        _amounts[resourceType] -= amount;
        return true;
    }

    private int CheckType(int resourceType)
    {
        if ((uint)resourceType >= (uint)_amounts.Length)
            throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Unknown resource type.");
        return resourceType;
    }
}
