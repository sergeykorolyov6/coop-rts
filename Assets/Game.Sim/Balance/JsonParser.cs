using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.Sim;

// Minimal strict json reader for Assets/Data. Numbers must be whole int32:
// the converter (Tools/csv2json.py) already turned seconds and cells into ticks
// and milli-cells, so a fraction here means stale or hand-edited data.
internal sealed class JsonParser
{
    private readonly string _text;
    private int _pos;

    private JsonParser(string text)
    {
        _text = text;
    }

    public static JsonNode Parse(string text)
    {
        var parser = new JsonParser(text);
        JsonNode root = parser.ReadValue();
        parser.SkipWhitespace();
        if (parser._pos != text.Length)
            throw parser.Error("Unexpected text after the json value");
        return root;
    }

    private JsonNode ReadValue()
    {
        SkipWhitespace();
        if (_pos >= _text.Length)
            throw Error("Unexpected end of json");

        char c = _text[_pos];
        switch (c)
        {
            case '{': return ReadObject();
            case '[': return ReadArray();
            case '"': return JsonNode.String(ReadString());
            case 't': ReadLiteral("true"); return JsonNode.Bool(true);
            case 'f': ReadLiteral("false"); return JsonNode.Bool(false);
            case 'n': ReadLiteral("null"); return JsonNode.Null();
        }
        if (c == '-' || (c >= '0' && c <= '9'))
            return ReadInteger();
        throw Error($"Unexpected character '{c}'");
    }

    private JsonNode ReadObject()
    {
        _pos++; // {
        var fields = new List<KeyValuePair<string, JsonNode>>();
        SkipWhitespace();
        if (TryConsume('}'))
            return JsonNode.Object(fields);

        while (true)
        {
            SkipWhitespace();
            if (Peek() != '"')
                throw Error("Expected a field name");
            string key = ReadString();
            foreach (KeyValuePair<string, JsonNode> field in fields)
            {
                if (field.Key == key)
                    throw Error($"Duplicate field \"{key}\"");
            }
            SkipWhitespace();
            Consume(':');
            fields.Add(new KeyValuePair<string, JsonNode>(key, ReadValue()));
            SkipWhitespace();
            if (TryConsume('}'))
                return JsonNode.Object(fields);
            Consume(',');
        }
    }

    private JsonNode ReadArray()
    {
        _pos++; // [
        var items = new List<JsonNode>();
        SkipWhitespace();
        if (TryConsume(']'))
            return JsonNode.Array(items);

        while (true)
        {
            items.Add(ReadValue());
            SkipWhitespace();
            if (TryConsume(']'))
                return JsonNode.Array(items);
            Consume(',');
        }
    }

    private string ReadString()
    {
        _pos++; // opening quote
        var sb = new StringBuilder();
        while (true)
        {
            if (_pos >= _text.Length)
                throw Error("Unterminated string");
            char c = _text[_pos++];
            if (c == '"')
                return sb.ToString();
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (_pos >= _text.Length)
                throw Error("Unterminated escape");
            char e = _text[_pos++];
            switch (e)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (_pos + 4 > _text.Length
                        || !int.TryParse(_text.Substring(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                        throw Error("Bad \\u escape");
                    sb.Append((char)code);
                    _pos += 4;
                    break;
                default:
                    throw Error($"Unknown escape \\{e}");
            }
        }
    }

    private JsonNode ReadInteger()
    {
        int start = _pos;
        if (_text[_pos] == '-')
            _pos++;
        while (_pos < _text.Length && _text[_pos] >= '0' && _text[_pos] <= '9')
            _pos++;

        if (_pos < _text.Length && (_text[_pos] == '.' || _text[_pos] == 'e' || _text[_pos] == 'E'))
            throw Error("Only whole numbers are allowed in sim data; regenerate it with Tools/csv2json.py");

        string digits = _text.Substring(start, _pos - start);
        if (!int.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            throw Error($"Bad or out-of-range integer \"{digits}\"");
        return JsonNode.Int(value);
    }

    private void ReadLiteral(string literal)
    {
        if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
            throw Error($"Expected {literal}");
        _pos += literal.Length;
    }

    private void SkipWhitespace()
    {
        while (_pos < _text.Length && (_text[_pos] == ' ' || _text[_pos] == '\t' || _text[_pos] == '\n' || _text[_pos] == '\r' || _text[_pos] == '﻿'))
            _pos++;
    }

    private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

    private bool TryConsume(char c)
    {
        if (Peek() != c)
            return false;
        _pos++;
        return true;
    }

    private void Consume(char c)
    {
        if (!TryConsume(c))
            throw Error($"Expected '{c}'");
    }

    private FormatException Error(string message) => new FormatException($"{message} at position {_pos}.");
}
