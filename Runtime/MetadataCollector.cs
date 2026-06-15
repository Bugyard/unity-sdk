using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BugyardSDK
{
    /// <summary>Builds the <see cref="ReportMetadata"/> payload from the current runtime state.</summary>
    public static class MetadataCollector
    {
        public static ReportMetadata Build(
            BugyardConfig config,
            ReportInput input,
            IReadOnlyDictionary<string, object> persistentContext = null)
        {
            Vector3 pos = input.playerPosition
                ?? (Camera.main != null ? Camera.main.transform.position : Vector3.zero);

            // The report's effective context is the SDK-wide persistent store (set via
            // Bugyard.SetContext) overlaid with anything the caller passed on this report; per-call
            // keys win on a conflict. Either side may be null/empty.
            IReadOnlyDictionary<string, object> context = MergeContext(persistentContext, input.context);

            return new ReportMetadata
            {
                clientReportId = Guid.NewGuid().ToString(),
                environment = config.environment,
                buildVersion = Application.version,
                engineVersion = Application.unityVersion,
                sdkVersion = BugyardVersion.Value,
                sceneName = SceneManager.GetActiveScene().name,
                playerPosition = new Vec3 { x = pos.x, y = pos.y, z = pos.z },
                report = new ReportBody
                {
                    title = string.IsNullOrWhiteSpace(input.title) ? "(no title)" : input.title,
                    description = input.description,
                    expectedResult = input.expectedResult,
                    severity = input.severity.ToString().ToLowerInvariant(),
                    category = string.IsNullOrWhiteSpace(input.category)
                        ? config.defaultCategory
                        : input.category,
                },
                reporter = input.reporter,
                device = new DeviceInfo
                {
                    os = SystemInfo.operatingSystem,
                    cpu = SystemInfo.processorType,
                    gpu = SystemInfo.graphicsDeviceName,
                    ramMb = SystemInfo.systemMemorySize,
                    deviceModel = SystemInfo.deviceModel,
                },
                runtime = new RuntimeInfo
                {
                    fps = Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)),
                    locale = CultureInfo.CurrentCulture.Name,
                    timezone = TimeZoneInfo.Local.Id,
                },
                // Serialize the free-form context bag here so the size cap is applied at the source;
                // oversized context is dropped (null) rather than truncated into invalid JSON.
                contextJson = PayloadLimits.ClampContext(context, config.maxContextBytes),
            };
        }

        // Overlay the per-report context onto a copy of the persistent store. Returns whichever
        // side is non-empty when the other is empty (no allocation in the common single-source
        // case), or null when both are empty so ClampContext emits no context object at all.
        static IReadOnlyDictionary<string, object> MergeContext(
            IReadOnlyDictionary<string, object> persistent,
            IReadOnlyDictionary<string, object> perReport)
        {
            bool hasPersistent = persistent != null && persistent.Count > 0;
            bool hasPerReport = perReport != null && perReport.Count > 0;

            if (!hasPersistent) return hasPerReport ? perReport : null;
            if (!hasPerReport) return persistent;

            var merged = new Dictionary<string, object>(persistent.Count + perReport.Count);
            foreach (KeyValuePair<string, object> kv in persistent) merged[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, object> kv in perReport) merged[kv.Key] = kv.Value; // per-report wins
            return merged;
        }
    }
}
