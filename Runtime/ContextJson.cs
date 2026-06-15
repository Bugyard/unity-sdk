using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BugyardSDK
{
    /// <summary>
    /// Serializes a free-form <c>context</c> bag — the app-internal state a game attaches to a
    /// report (inventory, quest flags, a save snapshot) — into a JSON object string. The shape
    /// matches the backend contract (bugyard-backend-docs/03-api-contracts.md): the top level is
    /// always an object, values may nest arbitrarily, and the result is stored verbatim in the
    /// report's metadata rather than flattened onto normalized columns.
    ///
    /// Supported value types: <c>null</c>, <c>string</c>, <c>char</c>, <c>bool</c>, every built-in
    /// integer/floating/decimal numeric, nested dictionaries (any <see cref="IDictionary"/>, keyed
    /// by the key's string form), and <see cref="IEnumerable"/> (arrays/lists). Anything else is
    /// written as its invariant-culture <c>ToString()</c>, so an unexpected value degrades to a
    /// readable string rather than throwing into the capture path. Non-finite floats/doubles
    /// (NaN/Infinity), which have no JSON literal, are emitted as <c>null</c>.
    /// </summary>
    public static class ContextJson
    {
        /// <summary>Serialize <paramref name="context"/> to a JSON object string, or null when it is null.</summary>
        public static string Serialize(IReadOnlyDictionary<string, object> context)
        {
            if (context == null) return null;
            var sb = new StringBuilder();
            WriteObject(sb, context);
            return sb.ToString();
        }

        /// <summary>
        /// Serialize an arbitrary value (primitive, dictionary, or array/list) to JSON using the
        /// same rules as <see cref="Serialize(IReadOnlyDictionary{string,object})"/>. Used for the
        /// breadcrumbs <c>events.json</c> payload, whose top level is a JSON array rather than an
        /// object. Mirrors the value handling so nested dictionaries/lists/primitives degrade the
        /// same way (unknown types become their invariant <c>ToString()</c>, non-finite floats null).
        /// </summary>
        public static string SerializeValue(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        static void WriteObject(StringBuilder sb, IReadOnlyDictionary<string, object> map)
        {
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> kv in map)
            {
                if (kv.Key == null) continue;
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, kv.Key);
                sb.Append(':');
                WriteValue(sb, kv.Value);
            }
            sb.Append('}');
        }

        static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case char c: WriteString(sb, c.ToString()); return;

                case sbyte n: sb.Append(((long)n).ToString(CultureInfo.InvariantCulture)); return;
                case byte n: sb.Append(((long)n).ToString(CultureInfo.InvariantCulture)); return;
                case short n: sb.Append(((long)n).ToString(CultureInfo.InvariantCulture)); return;
                case ushort n: sb.Append(((long)n).ToString(CultureInfo.InvariantCulture)); return;
                case int n: sb.Append(((long)n).ToString(CultureInfo.InvariantCulture)); return;
                case uint n: sb.Append(((long)n).ToString(CultureInfo.InvariantCulture)); return;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case ulong n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case float n: WriteDouble(sb, n); return;
                case double n: WriteDouble(sb, n); return;
                case decimal n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
            }

            // Check dictionaries before the general IEnumerable branch (a dictionary is also
            // enumerable, but should serialize as a JSON object, not an array of entries).
            if (value is IDictionary dict) { WriteDictionary(sb, dict); return; }
            if (value is IEnumerable en) { WriteArray(sb, en); return; }

            WriteString(sb, value.ToString());
        }

        static void WriteDictionary(StringBuilder sb, IDictionary map)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry e in map)
            {
                if (e.Key == null) continue;
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, e.Key.ToString());
                sb.Append(':');
                WriteValue(sb, e.Value);
            }
            sb.Append('}');
        }

        static void WriteArray(StringBuilder sb, IEnumerable items)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in items)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
        }

        static void WriteDouble(StringBuilder sb, double d)
        {
            // JSON has no NaN/Infinity literal; coerce non-finite to null to stay valid.
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        // Escapes a string per RFC 8259, mirroring MetadataJson's writer.
        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
