using System;
using System.Collections.Generic;
using UnityEngine;

namespace BugyardSDK
{
    public enum Severity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// Caller-supplied report content. Pass to <see cref="Bugyard.Capture"/>
    /// to send a report directly without the overlay UI.
    /// </summary>
    public class ReportInput
    {
        public string title;
        public string description;
        public string expectedResult;
        public Severity severity = Severity.Medium;

        /// <summary>Optional report category. Defaults to <see cref="BugyardConfig.defaultCategory"/> when empty.</summary>
        public string category;

        /// <summary>Optional reporter identity (tester id/name/email). Omitted from the payload when not set.</summary>
        public ReporterInfo reporter;

        /// <summary>Optional override for the player position. Defaults to the main camera position.</summary>
        public Vector3? playerPosition;

        /// <summary>
        /// Optional free-form key→value bag of app-internal state (inventory, quest flags, a save
        /// snapshot — whatever helps reproduce the bug). Serialized verbatim into the metadata
        /// <c>context</c> object and stored as-is by the backend. Values may nest (dictionaries,
        /// lists, primitives). Bounded to <see cref="BugyardConfig.maxContextBytes"/> serialized
        /// bytes; oversized context is dropped before upload rather than truncated.
        /// </summary>
        public Dictionary<string, object> context;

        /// <summary>
        /// Optional recent-gameplay-events JSON, uploaded as the <c>events.json</c> attachment
        /// (<c>application/json</c>). Capped at <see cref="BugyardConfig.maxEventsBytes"/>.
        /// </summary>
        public byte[] events;

        /// <summary>
        /// Optional engine save / game-state blob, uploaded as the <c>save_state</c> attachment.
        /// Sent as raw bytes (<c>application/octet-stream</c>) by default; set
        /// <see cref="saveStateIsJson"/> when the bytes are JSON. Capped at
        /// <see cref="BugyardConfig.maxSaveStateBytes"/>.
        /// </summary>
        public byte[] saveState;

        /// <summary>When true, <see cref="saveState"/> is uploaded as <c>application/json</c> (<c>save_state.json</c>) rather than raw bytes.</summary>
        public bool saveStateIsJson;

        /// <summary>
        /// Optional memory/arena dump, gzip-compressed, uploaded as the <c>memory_dump.gz</c>
        /// attachment (<c>application/gzip</c>). Capped at <see cref="BugyardConfig.maxMemoryDumpBytes"/>.
        /// </summary>
        public byte[] memoryDump;
    }

    /// <summary>
    /// The upload-ready artifacts that accompany a report's <see cref="ReportMetadata"/> in the
    /// multipart request: the screenshot, the captured logs, and the optional gameplay events /
    /// save-state / memory-dump blobs. Bundled so <see cref="BugyardClient"/> can pass them as one
    /// unit and clamp each to its configured cap before upload.
    /// </summary>
    public class ReportArtifacts
    {
        public byte[] screenshot;     // PNG -> screenshot.png (image/png)
        public string logs;           // text -> player.log (text/plain)
        public byte[] events;         // JSON -> events.json (application/json)
        public byte[] saveState;      // -> save_state.bin (octet-stream) or save_state.json
        public bool saveStateIsJson;  // selects the save_state MIME type and filename
        public byte[] memoryDump;     // gzip -> memory_dump.gz (application/gzip)
    }

    /// <summary>
    /// Outcome of a <see cref="BugyardClient.Send"/> call, surfaced to the overlay and to
    /// callers of <see cref="Bugyard.Capture(ReportInput, System.Action{SendResult})"/>.
    /// On success carries the backend <c>reportId</c>/<c>status</c>/<c>dashboardUrl</c>; on
    /// failure carries a friendly <see cref="message"/> (and the raw <see cref="errorCode"/>).
    /// </summary>
    public class SendResult
    {
        public bool success;

        /// <summary>HTTP status code of the final attempt, or 0 on a transport/network error.</summary>
        public long httpStatus;

        // Populated on success (from the response body).
        public string reportId;
        public string status;        // "created" | "already_exists"
        public string dashboardUrl;

        // Populated on failure.
        public string errorCode;     // backend { error } code, e.g. UNAUTHORIZED — may be empty
        public string message;       // human-friendly, safe to show in the overlay
        public string details;       // raw { details } JSON value from the body, if any — for logging/diagnostics

        /// <summary>True when a transient failure (offline / server error) was persisted to the
        /// offline queue and will be retried automatically on a later launch.</summary>
        public bool queuedForRetry;

        // Response/error envelope shapes parsed from the body (see backend doc 03-api-contracts).
        [Serializable]
        class Envelope
        {
            public string reportId;
            public string status;
            public string dashboardUrl;
            public string error;
            public string message;
        }

        public static SendResult Successful(long code, string body)
        {
            Envelope e = Parse(body);
            return new SendResult
            {
                success = true,
                httpStatus = code,
                reportId = e.reportId,
                status = e.status,
                dashboardUrl = e.dashboardUrl,
                message = string.Equals(e.status, "already_exists", StringComparison.OrdinalIgnoreCase)
                    ? "This report was already received earlier."
                    : "Report sent. Thanks!",
            };
        }

        public static SendResult Failed(long code, string body, string transportError)
        {
            Envelope e = Parse(body);
            return new SendResult
            {
                success = false,
                httpStatus = code,
                errorCode = e.error,
                // details is captured raw rather than via JsonUtility: its shape is unspecified
                // (string/object/array) and typing it on Envelope would risk failing the parse.
                details = BackendErrors.ExtractRawField(body, "details"),
                message = BackendErrors.FriendlyMessage(code, e.error, e.message, transportError),
            };
        }

        static Envelope Parse(string body)
        {
            if (string.IsNullOrEmpty(body)) return new Envelope();
            try { return JsonUtility.FromJson<Envelope>(body) ?? new Envelope(); }
            catch { return new Envelope(); }
        }
    }

    // --- Wire format (serialized by MetadataJson into the multipart "metadata" field) ---
    // Mirrors apps/backend metadata contract (bugyard-backend-docs/03-api-contracts.md).
    // JsonUtility is not used for serialization because it cannot omit empty optionals.

    [Serializable]
    public class ReportMetadata
    {
        public string clientReportId;
        public string environment;
        public string buildVersion;
        public string engine = "unity";
        public string engineVersion;
        public string sdkVersion;
        public string sceneName;
        public Vec3 playerPosition;
        public ReportBody report;
        public ReporterInfo reporter;
        public DeviceInfo device;
        public RuntimeInfo runtime;

        /// <summary>
        /// Pre-serialized free-form <c>context</c> object (see <see cref="ContextJson"/>), emitted
        /// verbatim as the metadata <c>context</c> field. Null when no context was supplied or it
        /// exceeded the size cap and was dropped.
        /// </summary>
        public string contextJson;
    }

    [Serializable]
    public class Vec3
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public class ReportBody
    {
        public string title;
        public string description;
        public string expectedResult;
        // Backend lowercase enum: low | medium | high | critical (Severity.ToString().ToLowerInvariant()).
        public string severity;
        // Always set by MetadataCollector from input.category or config.defaultCategory.
        public string category;
    }

    [Serializable]
    public class ReporterInfo
    {
        public string id;
        public string name;
        public string email;
    }

    [Serializable]
    public class DeviceInfo
    {
        public string os;
        public string cpu;
        public string gpu;
        public int ramMb;
        public string deviceModel;
    }

    [Serializable]
    public class RuntimeInfo
    {
        public int fps;
        public string locale;
        public string timezone;
    }
}
