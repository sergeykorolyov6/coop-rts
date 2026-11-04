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
    public void TryPay_BothResourcesOrNothing()
    {
        var wallet = new Wallet(2);
        wallet.Add(Setups.Primary, 50);
        wallet.Add(Setups.Secondary, 10);

        Assert.That(wallet.CanPay(50, 11), Is.False);
        Assert.That(wallet.TryPay(50, 11), Is.False);
        Assert.That((wallet.Get(Setups.Primary), wallet.Secondary), Is.EqualTo((50, 10)));
        Assert.That(wallet.TryPay(40, 10), Is.True);
        Assert.That((wallet.Get(Setups.Primary), wallet.Secondary), Is.EqualTo((10, 0)));
    }

    [Test]
    public void OneResourceMatch_CannotPayTheSecond()
    {
        var wallet = new Wallet(1);
        wallet.Add(Setups.Primary, 50);
        Assert.That(wallet.CanPay(10, 0), Is.True);
        Assert.That(wallet.CanPay(10, 1), Is.False);
        Assert.That(wallet.Secondary, Is.Zero);
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

    // The HUD shows income per minute from this counter: only what a headquarters credited
    // counts — not the start money, not refunds, and spending never lowers it.
    [Test]
    public void Earned_CountsCreditsOnly()
    {
        var wallet = new Wallet(2);
        wallet.Add(Setups.Primary, 100);
        wallet.Credit(Setups.Primary, 5);
        wallet.Credit(Setups.Secondary, 2);
        Assert.That(wallet.TrySpend(Setups.Primary, 50), Is.True);

        Assert.That(wallet.Earned(Setups.Primary), Is.EqualTo(5));
        Assert.That(wallet.Earned(Setups.Secondary), Is.EqualTo(2));
        Assert.That(wallet.Get(Setups.Primary), Is.EqualTo(55));
    }

    [Test]
    public void Earned_ManualDelivery_CountedForHeadquartersOwner()
    {
        var map = new MapSetup(32, 16).Start(Setups.PlayerA, 2, 2).OreRect(10, 2, 2, 2, Resources.Primary);
        var sim = new Simulation(Setups.Coop(map));
        Unit worker = sim.State.UnitsOf(Setups.PlayerA)[0];
        sim.Order(new MineCommand(Setups.PlayerA, sim.State.Tick, new[] { worker.Id }, 10, 2));

        sim.RunUntil(s => s.State.GetPlayer(Setups.PlayerA).Wallet.Earned(Resources.Primary) > 0);
        Assert.That(sim.State.GetPlayer(Setups.PlayerA).Wallet.Earned(Resources.Primary), Is.EqualTo(sim.State.Rules.ManualCargo));
    }
}
