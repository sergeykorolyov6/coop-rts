using System;

namespace Game.Sim;

// Per-player resources. There is no team wallet: income goes to whoever's
// headquarters accepted the resource.
public sealed class Wallet
{
    private readonly int[] _amounts;
    private readonly int[] _earned;
    private readonly int[] _incomeRemainder;

    internal Wallet(int resourceTypeCount)
    {
        _amounts = new int[resourceTypeCount];
        _earned = new int[resourceTypeCount];
        _incomeRemainder = new int[resourceTypeCount];
    }

    // legion-ai.md: the Black Legion's headquarters credits income_pct of what it accepts;
    // 100 for everyone else.
    public int IncomePercent { get; internal set; } = 100;

    // Hundredths of a unit of income not credited yet.
    internal int IncomeRemainder(int resourceType) => _incomeRemainder[CheckType(resourceType)];

    public int ResourceTypeCount => _amounts.Length;

    public int Get(int resourceType) => _amounts[CheckType(resourceType)];

    // Everything a headquarters credited since the start (the HUD's income per minute):
    // not the start money, not refunds; spending never lowers it.
    public int Earned(int resourceType) => _earned[CheckType(resourceType)];

    internal void Add(int resourceType, int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Use TrySpend to take resources.");
        _amounts[CheckType(resourceType)] = checked(_amounts[resourceType] + amount);
    }

    // Income accepted by a headquarters (answers 2, 15).
    internal void Credit(int resourceType, int amount)
    {
        if (IncomePercent != 100)
        {
            int total = checked(_incomeRemainder[CheckType(resourceType)] + amount * IncomePercent);
            amount = total / 100;
            _incomeRemainder[resourceType] = total % 100;
        }
        Add(resourceType, amount);
        _earned[resourceType] = checked(_earned[resourceType] + amount);
    }

    // A price in both resources; a second resource this match does not have cannot be paid.
    public bool CanPay(int primary, int secondary) =>
        Get(Resources.Primary) >= primary
        && (secondary <= 0 || (ResourceTypeCount > Resources.Secondary && Get(Resources.Secondary) >= secondary));

    // What it holds of the second resource, 0 in a match without one.
    public int Secondary => ResourceTypeCount > Resources.Secondary ? _amounts[Resources.Secondary] : 0;

    // All-or-nothing over both resources: on false the wallet is unchanged.
    internal bool TryPay(int primary, int secondary)
    {
        if (!CanPay(primary, secondary))
            return false;
        _amounts[Resources.Primary] -= primary;
        if (secondary > 0)
            _amounts[Resources.Secondary] -= secondary;
        return true;
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
