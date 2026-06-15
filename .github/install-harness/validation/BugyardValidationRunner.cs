#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Bugyard.CI
{
    /// <summary>
    /// Batch-mode entry point that runs Unity's Package Validation Suite
    /// (com.unity.package-validation-suite) against the embedded Bugyard package and
    /// exits the editor non-zero on failure — so packaging mistakes the unit tests and
    /// the install-compile leg can't see (package.json correctness, meta coverage,
    /// naming conventions, sample layout, version/changelog consistency) fail the build.
    ///
    /// This file lives under .github/install-harness/validation/ and is copied into the
    /// harness project's Assets/Editor by the package-validation CI job — it is NOT part
    /// of the shipped package and is not compiled by the normal install-verification leg
    /// (which does not install the validation suite).
    ///
    /// Invoked via: Unity -batchmode -executeMethod Bugyard.CI.BugyardValidationRunner.Run
    ///
    /// The Validation Suite's public API has shifted across versions, so the call is made
    /// reflectively: we locate ValidationSuite.ValidatePackage and a sensible ValidationType
    /// and invoke whichever overload exists. A hard failure to even find the API is treated
    /// as a failure (exit 1) rather than a silent pass — a green run must mean validation
    /// actually ran.
    /// </summary>
    public static class BugyardValidationRunner
    {
        const string PackageName = "com.bugyard.sdk";

        public static void Run()
        {
            int exitCode;
            try
            {
                exitCode = Validate() ? 0 : 1;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[Bugyard.CI] Validation runner threw: {e}");
                exitCode = 1;
            }

            // Flush before exiting so the log is complete in CI artifacts.
            Console.Out.Flush();
            EditorApplication.Exit(exitCode);
        }

        static bool Validate()
        {
            PackageInfo pkg = PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == PackageName);
            if (pkg == null)
            {
                Console.Error.WriteLine(
                    $"[Bugyard.CI] Package '{PackageName}' is not registered in this project. " +
                    "Is the harness manifest pointing at it?");
                return false;
            }

            Console.WriteLine($"[Bugyard.CI] Validating {pkg.name}@{pkg.version} (resolved at {pkg.resolvedPath}).");

            Type suiteType = FindType("UnityEditor.PackageManager.ValidationSuite.ValidationSuite");
            if (suiteType == null)
            {
                Console.Error.WriteLine(
                    "[Bugyard.CI] Could not find UnityEditor.PackageManager.ValidationSuite.ValidationSuite. " +
                    "Is com.unity.package-validation-suite installed?");
                return false;
            }

            MethodInfo validate = suiteType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "ValidatePackage");
            if (validate == null)
            {
                Console.Error.WriteLine("[Bugyard.CI] ValidationSuite.ValidatePackage was not found.");
                return false;
            }

            object validationType = ResolveValidationType(suiteType.Assembly);
            string packageId = $"{pkg.name}@{pkg.version}";

            ParameterInfo[] ps = validate.GetParameters();
            object[] args;
            // Common overload: ValidatePackage(string packageId, ValidationType type).
            if (ps.Length == 2 && ps[0].ParameterType == typeof(string))
            {
                args = new object[] { packageId, validationType };
            }
            // Older overload: ValidatePackage(string packageId).
            else if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
            {
                args = new object[] { packageId };
            }
            else
            {
                Console.Error.WriteLine(
                    "[Bugyard.CI] Unrecognized ValidatePackage signature: " +
                    string.Join(", ", ps.Select(p => p.ParameterType.Name)));
                return false;
            }

            object result = validate.Invoke(null, args);
            bool passed = result is bool b && b;

            PrintReport(pkg.name);

            Console.WriteLine($"[Bugyard.CI] Validation {(passed ? "PASSED" : "FAILED")} for {packageId}.");
            return passed;
        }

        // Prefer the validation profile the in-editor "Validate" button uses (LocalDevelopment),
        // then fall back through progressively more generic options so this keeps working if the
        // enum changes.
        static object ResolveValidationType(Assembly suiteAssembly)
        {
            Type enumType = suiteAssembly.GetType("UnityEditor.PackageManager.ValidationSuite.ValidationType");
            if (enumType == null || !enumType.IsEnum)
                return null;

            foreach (string preferred in new[] { "LocalDevelopment", "Structure", "Publishing", "CI" })
            {
                if (Enum.GetNames(enumType).Contains(preferred))
                    return Enum.Parse(enumType, preferred);
            }

            // Last resort: the first declared value.
            return Enum.GetValues(enumType).GetValue(0);
        }

        // The suite writes a human-readable report to Library/ValidationSuiteResults/.
        // Echo it into the build log so CI failures are diagnosable from the run output.
        static void PrintReport(string packageName)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Library", "ValidationSuiteResults");
            if (!Directory.Exists(dir))
                return;

            foreach (string file in Directory.GetFiles(dir, $"{packageName}*.txt"))
            {
                Console.WriteLine($"[Bugyard.CI] ---- {Path.GetFileName(file)} ----");
                Console.WriteLine(File.ReadAllText(file));
            }
        }

        static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName);
                if (t != null)
                    return t;
            }
            return null;
        }
    }
}
#endif
