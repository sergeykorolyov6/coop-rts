using System;
using System.Collections.Generic;

namespace Game.Sim;

// A parsed json value. Objects keep fields as an ordered list: lookups happen
// only while loading data, and the sim never iterates a hash map.
internal sealed class JsonNode
{
    private static readonly List<JsonNode> NoItems = new List<JsonNode>();
    private static readonly List<KeyValuePair<string, JsonNode>> NoFields = new List<KeyValuePair<string, JsonNode>>();

    private JsonNode(JsonKind kind, int integer, string text, List<JsonNode> items, List<KeyValuePair<string, JsonNode>> fields)
    {
        Kind = kind;
        Integer = integer;
        Text = text;
        Items = items;
        Fields = fields;
    }

    public JsonKind Kind { get; }
    public int Integer { get; }
    public string Text { get; }
    public IReadOnlyList<JsonNode> Items { get; }
    public IReadOnlyList<KeyValuePair<string, JsonNode>> Fields { get; }

    public static JsonNode Object(List<KeyValuePair<string, JsonNode>> fields) => new JsonNode(JsonKind.Object, 0, "", NoItems, fields);
    public static JsonNode Array(List<JsonNode> items) => new JsonNode(JsonKind.Array, 0, "", items, NoFields);
    public static JsonNode String(string text) => new JsonNode(JsonKind.String, 0, text, NoItems, NoFields);
    public static JsonNode Int(int value) => new JsonNode(JsonKind.Integer, value, "", NoItems, NoFields);
    public static JsonNode Bool(bool value) => new JsonNode(JsonKind.Bool, value ? 1 : 0, "", NoItems, NoFields);
    public static JsonNode Null() => new JsonNode(JsonKind.Null, 0, "", NoItems, NoFields);

    public JsonNode Get(string key)
    {
        Expect(JsonKind.Object, "object");
        foreach (KeyValuePair<string, JsonNode> field in Fields)
        {
            if (field.Key == key)
                return field.Value;
        }
        throw new FormatException($"Missing field \"{key}\".");
    }

    public int GetInt(string key) => Get(key).Expect(JsonKind.Integer, $"integer field \"{key}\"").Integer;

    public string GetString(string key) => Get(key).Expect(JsonKind.String, $"string field \"{key}\"").Text;

    public List<string> GetStringList(string key)
    {
        JsonNode list = Get(key).Expect(JsonKind.Array, $"list field \"{key}\"");
        var result = new List<string>(list.Items.Count);
        foreach (JsonNode item in list.Items)
            result.Add(item.Expect(JsonKind.String, $"string in \"{key}\"").Text);
        return result;
    }

    private JsonNode Expect(JsonKind kind, string what)
    {
        if (Kind != kind)
            throw new FormatException($"Expected {what}, got {Kind}.");
        return this;
    }
}
