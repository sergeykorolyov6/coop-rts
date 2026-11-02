using System;
using NUnit.Framework;

namespace Game.Sim.Tests;

public class JsonParserTests
{
    [Test]
    public void Parses_ArrayOfObjects_WithIntsStringsAndLists()
    {
        JsonNode root = JsonParser.Parse(
            "[ {\"id\": \"drill\", \"hp\": 300, \"produces\": [\"a\", \"b\"], \"neg\": -7} ]");

        Assert.That(root.Kind, Is.EqualTo(JsonKind.Array));
        Assert.That(root.Items, Has.Count.EqualTo(1));
        JsonNode drill = root.Items[0];
        Assert.That(drill.GetString("id"), Is.EqualTo("drill"));
        Assert.That(drill.GetInt("hp"), Is.EqualTo(300));
        Assert.That(drill.GetInt("neg"), Is.EqualTo(-7));
        Assert.That(drill.GetStringList("produces"), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void Parses_EscapesAndCyrillic()
    {
        JsonNode node = JsonParser.Parse("{\"s\": \"Бур \\\"2\\u00d72\\\"\\n\\\\\"}");
        Assert.That(node.GetString("s"), Is.EqualTo("Бур \"2×2\"\n\\"));
    }

    // Fractions would bring non-integer math into the sim; the converter must have removed them.
    [TestCase("{\"a\": 1.5}")]
    [TestCase("{\"a\": 1e3}")]
    public void Rejects_NonIntegerNumbers(string json)
    {
        Assert.Throws<FormatException>(() => JsonParser.Parse(json));
    }

    [TestCase("{\"a\": 1} x")]
    [TestCase("{\"a\": }")]
    [TestCase("[1, 2")]
    [TestCase("{\"a\": 99999999999}")]
    [TestCase("{\"a\": 1, \"a\": 2}")]
    public void Rejects_MalformedJson(string json)
    {
        Assert.Throws<FormatException>(() => JsonParser.Parse(json));
    }

    [Test]
    public void MissingField_NamesTheField()
    {
        JsonNode node = JsonParser.Parse("{\"id\": \"x\"}");
        var ex = Assert.Throws<FormatException>(() => node.GetInt("hp"));
        Assert.That(ex!.Message, Does.Contain("hp"));
    }

    [Test]
    public void WrongType_IsAnError()
    {
        JsonNode node = JsonParser.Parse("{\"hp\": \"300\"}");
        Assert.Throws<FormatException>(() => node.GetInt("hp"));
    }
}
