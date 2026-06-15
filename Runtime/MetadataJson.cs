using System.Globalization;
using System.Text;

namespace BugyardSDK
{
    /// <summary>
    /// Serializes <see cref="ReportMetadata"/> into the exact JSON shape the backend
    /// ingestion API expects (see bugyard-backend-docs/03-api-contracts.md).
    ///
    /// Unity's <c>JsonUtility</c> is deliberately not used here: it cannot omit fields,
    /// so a null or empty optional is emitted as <c>""</c>, which does not match the
    /// contract (optionals are absent or <c>null</c>, never empty strings). This writer
    /// emits required fields always and omits optionals that are null/empty, so the
    /// payload matches the schema field-for-field.
    /// </summary>
    public static class MetadataJson
    {
        public static string Serialize(ReportMetadata m)
        {
            string report = new JsonObject()
                .Str("title", m.report.title, required: true)
                .Str("description", m.report.description)
                .Str("expectedResult", m.report.expectedResult)
                .Str("severity", m.report.severity, required: true)
                .Str("category", m.report.category, required: true)
                .End();

            // Reporter is fully optional; omit the whole object when no field is set.
            string reporter = null;
            if (m.reporter != null && !(string.IsNullOrEmpty(m.reporter.id)
                                        && string.IsNullOrEmpty(m.reporter.name)
                                        && string.IsNullOrEmpty(m.reporter.email)))
            {
                reporter = new JsonObject()
                    .Str("id", m.reporter.id)
                    .Str("name", m.reporter.name)
                    .Str("email", m.reporter.email)
                    .End();
            }

            string playerPosition = new JsonObject()
                .Num("x", m.playerPosition.x)
                .Num("y", m.playerPosition.y)
                .Num("z", m.playerPosition.z)
                .End();

            string device = new JsonObject()
                .Str("os", m.device.os, required: true)
                .Str("cpu", m.device.cpu, required: true)
                .Str("gpu", m.device.gpu, required: true)
                .Num("ramMb", m.device.ramMb)
                .Str("deviceModel", m.device.deviceModel)
                .End();

            string runtime = new JsonObject()
                .Num("fps", m.runtime.fps)
                .Str("locale", m.runtime.locale, required: true)
                .Str("timezone", m.runtime.timezone, required: true)
                .End();

            return new JsonObject()
                .Str("clientReportId", m.clientReportId, required: true)
                .Str("environment", m.environment, required: true)
                .Str("buildVersion", m.buildVersion, required: true)
                .Str("engine", m.engine, required: true)
                .Str("engineVersion", m.engineVersion, required: true)
                .Str("sdkVersion", m.sdkVersion, required: true)
                .Str("sceneName", m.sceneName, required: true)
                .Raw("playerPosition", playerPosition)
                .Raw("report", report)
                .Raw("reporter", reporter)
                .Raw("device", device)
                .Raw("runtime", runtime)
                .End();
        }

        /// <summary>
        /// Minimal JSON object writer: emits required fields, omits empty optionals,
        /// and escapes strings per RFC 8259. Numbers use the invariant culture so the
        /// decimal separator is always '.' regardless of the player's locale.
        /// </summary>
        sealed class JsonObject
        {
            readonly StringBuilder _sb = new StringBuilder("{");
            bool _first = true;

            void Key(string key)
            {
                if (!_first) _sb.Append(',');
                _first = false;
                _sb.Append('"').Append(key).Append("\":");
            }

            public JsonObject Str(string key, string value, bool required = false)
            {
                if (string.IsNullOrEmpty(value))
                {
                    if (!required) return this;
                    Key(key);
                    _sb.Append("\"\"");
                    return this;
                }
                Key(key);
                AppendEscaped(value);
                return this;
            }

            public JsonObject Num(string key, int value)
            {
                Key(key);
                _sb.Append(value.ToString(CultureInfo.InvariantCulture));
                return this;
            }

            public JsonObject Num(string key, float value)
            {
                Key(key);
                // JSON has no NaN/Infinity literal; coerce non-finite to 0 to stay valid.
                float safe = float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
                _sb.Append(safe.ToString("R", CultureInfo.InvariantCulture));
                return this;
            }

            /// <summary>Append a pre-serialized nested JSON value. Omitted when null.</summary>
            public JsonObject Raw(string key, string json)
            {
                if (json == null) return this;
                Key(key);
                _sb.Append(json);
                return this;
            }

            public string End()
            {
                _sb.Append('}');
                return _sb.ToString();
            }

            void AppendEscaped(string s)
            {
                _sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': _sb.Append("\\\""); break;
                        case '\\': _sb.Append("\\\\"); break;
                        case '\b': _sb.Append("\\b"); break;
                        case '\f': _sb.Append("\\f"); break;
                        case '\n': _sb.Append("\\n"); break;
                        case '\r': _sb.Append("\\r"); break;
                        case '\t': _sb.Append("\\t"); break;
                        default:
                            if (c < 0x20)
                                _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else
                                _sb.Append(c);
                            break;
                    }
                }
                _sb.Append('"');
            }
        }
    }
}
