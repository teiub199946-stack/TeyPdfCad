using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using TeyPdfCad.Core.Bridge;
using TeyPdfCad.Core.Recognition;

namespace TeyPdfCad.AutoCAD;

internal sealed class BridgeRuntimeSettings
{
    private BridgeRuntimeSettings(
        string pipelineVersion,
        string outputUnits,
        bool preserveSourceGeometry,
        string recognizerProfile,
        UnitsValue? targetUnits)
    {
        PipelineVersion = pipelineVersion;
        OutputUnits = outputUnits;
        PreserveSourceGeometry = preserveSourceGeometry;
        RecognizerProfile = recognizerProfile;
        TargetUnits = targetUnits;
    }

    public string PipelineVersion { get; }
    public string OutputUnits { get; }
    public bool PreserveSourceGeometry { get; }
    public string RecognizerProfile { get; }
    public UnitsValue? TargetUnits { get; }

    public DimensionRecognitionOptions RecognitionOptions => RecognizerProfile switch
    {
        "default" => new DimensionRecognitionOptions(),
        "strict" => new DimensionRecognitionOptions
        {
            MinConfidence = 0.85,
            MeasurementRelativeTolerance = 0.01
        },
        _ => throw new InvalidOperationException($"Unsupported recognizer profile '{RecognizerProfile}'.")
    };

    public static bool TryReadFromEnvironment(
        out BridgeRuntimeSettings? settings,
        out string error)
    {
        settings = null;
        error = string.Empty;

        var statusPath = Environment.GetEnvironmentVariable(BridgeEnvironmentVariables.StatusFile);
        if (string.IsNullOrWhiteSpace(statusPath))
            return true;

        var pipelineVersion = Normalize(Environment.GetEnvironmentVariable(BridgeEnvironmentVariables.PipelineVersion), "1");
        if (pipelineVersion is not "1" and not "v1")
        {
            error = $"Unsupported pipeline version '{pipelineVersion}'. Supported versions: 1, v1.";
            WriteStatus(statusPath, $"error|{error}");
            return false;
        }

        var outputUnits = Normalize(Environment.GetEnvironmentVariable(BridgeEnvironmentVariables.OutputUnits), "drawing");
        if (!TryMapUnits(outputUnits, out var targetUnits))
        {
            error = $"Unsupported output units '{outputUnits}'. Supported values: drawing, mm, cm, m, in, ft.";
            WriteStatus(statusPath, $"error|{error}");
            return false;
        }

        var recognizerProfile = Normalize(
            Environment.GetEnvironmentVariable(BridgeEnvironmentVariables.RecognizerProfile),
            "default");
        if (recognizerProfile is not "default" and not "strict")
        {
            error = $"Unsupported recognizer profile '{recognizerProfile}'. Supported profiles: default, strict.";
            WriteStatus(statusPath, $"error|{error}");
            return false;
        }

        var preserveValue = Normalize(
            Environment.GetEnvironmentVariable(BridgeEnvironmentVariables.PreserveSourceGeometry),
            "true");
        if (!bool.TryParse(preserveValue, out var preserveSourceGeometry))
        {
            error = $"Invalid preserveSourceGeometry value '{preserveValue}'. Expected true or false.";
            WriteStatus(statusPath, $"error|{error}");
            return false;
        }

        settings = new BridgeRuntimeSettings(
            pipelineVersion,
            outputUnits,
            preserveSourceGeometry,
            recognizerProfile,
            targetUnits);
        return true;
    }

    public void ApplyOutputUnits(Database database)
    {
        if (TargetUnits.HasValue)
            database.Insunits = TargetUnits.Value;
    }

    public void Complete()
    {
        var statusPath = Environment.GetEnvironmentVariable(BridgeEnvironmentVariables.StatusFile);
        if (!string.IsNullOrWhiteSpace(statusPath))
            WriteStatus(statusPath, "ok");
    }

    public void Fail(string message)
    {
        var statusPath = Environment.GetEnvironmentVariable(BridgeEnvironmentVariables.StatusFile);
        if (!string.IsNullOrWhiteSpace(statusPath))
            WriteStatus(statusPath, $"error|{message}");
    }

    private static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value!.Trim().ToLowerInvariant();
    }

    private static bool TryMapUnits(string value, out UnitsValue? targetUnits)
    {
        targetUnits = value switch
        {
            "drawing" => null,
            "mm" or "millimeter" or "millimeters" => UnitsValue.Millimeters,
            "cm" or "centimeter" or "centimeters" => UnitsValue.Centimeters,
            "m" or "meter" or "meters" => UnitsValue.Meters,
            "in" or "inch" or "inches" => UnitsValue.Inches,
            "ft" or "foot" or "feet" => UnitsValue.Feet,
            _ => null
        };

        return value == "drawing" || targetUnits.HasValue;
    }

    private static void WriteStatus(string path, string value)
    {
        try
        {
            File.WriteAllText(path, value);
        }
        catch (IOException)
        {
            // The bridge process reports a missing status file as a failed conversion.
        }
        catch (UnauthorizedAccessException)
        {
            // The bridge process reports a missing status file as a failed conversion.
        }
    }
}
