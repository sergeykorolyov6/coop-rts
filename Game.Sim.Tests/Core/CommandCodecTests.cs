using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class CommandCodecTests
{
    [Test]
    public void Log_RoundTrips_AllFields()
    {
        var codec = TestCommands.CreateCodec();
        var log = new List<ICommand>
        {
            new GrantCommand(1, 5, Setups.Secondary, 30),
            new RandomGrantCommand(2, 7, 10),
            new MarkCommand(9, 7, "привет"),
        };

        var restored = codec.ReadLog(codec.WriteLog(log));

        Assert.That(restored, Has.Count.EqualTo(3));
        var grant = (GrantCommand)restored[0];
        Assert.That((grant.PlayerId, grant.Tick, grant.ResourceType, grant.Amount), Is.EqualTo((1, 5, Setups.Secondary, 30)));
        var random = (RandomGrantCommand)restored[1];
        Assert.That((random.PlayerId, random.Tick, random.MaxAmount), Is.EqualTo((2, 7, 10)));
        var mark = (MarkCommand)restored[2];
        Assert.That((mark.PlayerId, mark.Tick, mark.Label), Is.EqualTo((9, 7, "привет")));
    }

    [Test]
    public void Write_IsByteStable()
    {
        // The log format is shared between machines and future replays.
        var bytes = TestCommands.CreateCodec().WriteLog(new ICommand[] { new GrantCommand(1, 2, 0, 3) });

        Assert.That(BitConverter.ToString(bytes), Is.EqualTo(
            "01-00-00-00-" +              // count
            "E9-03-00-00-" +              // type 1001
            "01-00-00-00-02-00-00-00-" +  // player 1, tick 2
            "08-00-00-00-" +              // payload length
            "00-00-00-00-03-00-00-00"));  // resource 0, amount 3
    }

    [Test]
    public void UnknownType_Throws()
    {
        var bytes = TestCommands.CreateCodec().WriteLog(new ICommand[] { new GrantCommand(1, 0, 0, 1) });

        Assert.Throws<InvalidDataException>(() => new CommandCodec().ReadLog(bytes));
    }

    [Test]
    public void DuplicateRegistration_Throws()
    {
        var codec = TestCommands.CreateCodec();

        Assert.Throws<ArgumentException>(() => codec.Register(GrantCommand.Id, GrantCommand.Read));
    }

    [Test]
    public void ReaderConsumingWrongLength_Throws()
    {
        var writer = TestCommands.CreateCodec();
        var bytes = writer.WriteLog(new ICommand[] { new GrantCommand(1, 0, 0, 1) });

        var reader = new CommandCodec();
        reader.Register(GrantCommand.Id, (player, tick, r) => new GrantCommand(player, tick, r.ReadInt32(), 0));

        Assert.Throws<InvalidDataException>(() => reader.ReadLog(bytes));
    }

    [Test]
    public void TruncatedLog_Throws()
    {
        var codec = TestCommands.CreateCodec();
        var bytes = codec.WriteLog(new ICommand[] { new GrantCommand(1, 0, 0, 1) });
        Array.Resize(ref bytes, bytes.Length - 1);

        Assert.That(() => codec.ReadLog(bytes), Throws.InstanceOf<InvalidDataException>().Or.InstanceOf<EndOfStreamException>());
    }
}
