using System;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class WalletTests
{
    [Test]
    public void NewWallet_IsEmpty()
    {
        var wallet = new Wallet(2);
        Assert.That(wallet.Get(Setups.Primary), Is.Zero);
        Assert.That(wallet.Get(Setups.Secondary), Is.Zero);
    }

    [Test]
    public void AddAndSpend_PerResourceType()
    {
        var wallet = new Wallet(2);
        wallet.Add(Setups.Primary, 100);
        wallet.Add(Setups.Secondary, 5);

        Assert.That(wallet.TrySpend(Setups.Primary, 30), Is.True);
        Assert.That(wallet.Get(Setups.Primary), Is.EqualTo(70));
        Assert.That(wallet.Get(Setups.Secondary), Is.EqualTo(5));
    }

    [Test]
    public void TrySpend_NotEnough_LeavesWalletUnchanged()
    {
        var wallet = new Wallet(2);
        wallet.Add(Setups.Primary, 29);

        Assert.That(wallet.TrySpend(Setups.Primary, 30), Is.False);
        Assert.That(wallet.Get(Setups.Primary), Is.EqualTo(29));
    }

    [Test]
    public void TrySpend_ExactAmount_EmptiesWallet()
    {
        var wallet = new Wallet(1);
        wallet.Add(Setups.Primary, 30);

        Assert.That(wallet.TrySpend(Setups.Primary, 30), Is.True);
        Assert.That(wallet.Get(Setups.Primary), Is.Zero);
    }

    [Test]
    public void InvalidArguments_Throw()
    {
        var wallet = new Wallet(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Get(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Get(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Add(Setups.Primary, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.TrySpend(Setups.Primary, -1));
    }

    [Test]
    public void Add_Overflow_Throws()
    {
        var wallet = new Wallet(1);
        wallet.Add(Setups.Primary, int.MaxValue);
        Assert.Throws<OverflowException>(() => wallet.Add(Setups.Primary, 1));
    }

    [Test]
    public void EachPlayer_HasOwnWallet()
    {
        var state = new SimState(Setups.TwoAlliesAndEnemy());
        state.GetPlayer(1).Wallet.Add(Setups.Primary, 100);

        Assert.That(state.GetPlayer(1).Wallet.Get(Setups.Primary), Is.EqualTo(100));
        Assert.That(state.GetPlayer(2).Wallet.Get(Setups.Primary), Is.Zero);
        Assert.That(state.GetPlayer(9).Wallet.Get(Setups.Primary), Is.Zero);
    }
}
