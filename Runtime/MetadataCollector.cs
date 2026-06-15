using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BugCaptureSDK
{
    /// <summary>Builds the <see cref="ReportMetadata"/> payload from the current runtime state.</summary>
    public static class MetadataCollector
    {
        public static ReportMetadata Build(BugCaptureConfig config, ReportInput input)
        {
            Vector3 pos = input.playerPosition
                ?? (Camera.main != null ? Camera.main.transform.position : Vector3.zero);

            return new ReportMetadata
            {
                clientReportId = Guid.NewGuid().ToString(),
                environment = config.environment,
                buildVersion = Application.version,
                engineVersion = Application.unityVersion,
                sdkVersion = BugCaptureVersion.Value,
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
            };
        }
    }
}
