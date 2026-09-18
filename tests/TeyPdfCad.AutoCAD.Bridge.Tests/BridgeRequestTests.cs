using TeyPdfCad.AutoCAD.Bridge;
using Xunit;

namespace TeyPdfCad.AutoCAD.Bridge.Tests;

public sealed class BridgeRequestTests
{
    [Fact]
    public void Parses_the_supported_request_contract()
    {
        using var files = TestFiles.Create();

        var request = BridgeRequest.Parse(TestFiles.Arguments(files.Input, files.Output, "job_42"));

        Assert.Equal(files.Input, request.InputPath);
        Assert.Equal(files.Output, request.OutputPath);
        Assert.Equal("job_42", request.JobId);
        Assert.Equal("v1", request.PipelineVersion);
        Assert.Equal("drawing", request.OutputUnits);
        Assert.True(request.PreserveSourceGeometry);
        Assert.Equal("default", request.RecognizerProfile);
    }

    [Theory]
    [InlineData("2", "Unsupported bridge protocol version")]
    [InlineData("", "Required bridge argument is missing")]
    public void Rejects_unsupported_or_missing_protocol_version(string version, string expectedMessage)
    {
        using var files = TestFiles.Create();
        var args = TestFiles.Arguments(files.Input, files.Output, "job")
            .Select((value, index) => index == 3 ? version : value)
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() => BridgeRequest.Parse(args));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_unknown_and_duplicate_arguments()
    {
        using var files = TestFiles.Create();
        var unknown = TestFiles.Arguments(files.Input, files.Output, "job")
            .Append("--typo")
            .Append("value")
            .ToArray();
        var duplicate = TestFiles.Arguments(files.Input, files.Output, "job")
            .Append("--input")
            .Append(files.Input)
            .ToArray();

        Assert.Contains("Unsupported bridge argument", Assert.Throws<ArgumentException>(
            () => BridgeRequest.Parse(unknown)).Message, StringComparison.Ordinal);
        Assert.Contains("Duplicate bridge argument", Assert.Throws<ArgumentException>(
            () => BridgeRequest.Parse(duplicate)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_malformed_argument_pairs()
    {
        var exception = Assert.Throws<ArgumentException>(() => BridgeRequest.Parse(["--input"]));

        Assert.Contains("'--name value' pairs", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("drawing.txt", "Output file must use the .dwg extension")]
    [InlineData("input.txt", "Input file must use the .pdf extension")]
    public void Rejects_unsupported_file_extensions(string fileName, string expectedMessage)
    {
        using var files = TestFiles.Create();
        var input = fileName.StartsWith("input", StringComparison.Ordinal)
            ? files.DirectoryPath + "\\" + fileName
            : files.Input;
        var output = fileName.StartsWith("drawing", StringComparison.Ordinal)
            ? files.DirectoryPath + "\\" + fileName
            : files.Output;
        File.WriteAllText(input, "fixture");

        var exception = Assert.Throws<ArgumentException>(() => BridgeRequest.Parse(
            TestFiles.Arguments(input, output, "job")));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_same_input_and_output_path()
    {
        using var files = TestFiles.Create();

        var exception = Assert.Throws<ArgumentException>(() => BridgeRequest.Parse(
            TestFiles.Arguments(files.Input, files.Input, "job")));

        Assert.Contains("must differ", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-bool", "must be true or false")]
    [InlineData("", "Required bridge argument is missing")]
    public void Rejects_invalid_preserve_source_geometry(string value, string expectedMessage)
    {
        using var files = TestFiles.Create();
        var args = TestFiles.Arguments(files.Input, files.Output, "job")
            .Select((item, index) => index == 13 ? value : item)
            .ToArray();

        var exception = Assert.Throws<ArgumentException>(() => BridgeRequest.Parse(args));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_missing_input_file()
    {
        using var files = TestFiles.Create();
        var missingInput = Path.Combine(files.DirectoryPath, "missing.pdf");

        var exception = Assert.Throws<FileNotFoundException>(() => BridgeRequest.Parse(
            TestFiles.Arguments(missingInput, files.Output, "job")));

        Assert.Equal(missingInput, exception.FileName);
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("bad/id")]
    public void Rejects_unsupported_job_ids(string jobId)
    {
        using var files = TestFiles.Create();

        var exception = Assert.Throws<ArgumentException>(() => BridgeRequest.Parse(
            TestFiles.Arguments(files.Input, files.Output, jobId)));

        Assert.Contains("--job-id may contain only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_job_ids_longer_than_protocol_limit()
    {
        using var files = TestFiles.Create();

        var exception = Assert.Throws<ArgumentException>(() => BridgeRequest.Parse(
            TestFiles.Arguments(files.Input, files.Output, new string('a', 129))));

        Assert.Contains("at most 128", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TestFiles : IDisposable
    {
        private TestFiles(string directoryPath)
        {
            DirectoryPath = directoryPath;
            Directory.CreateDirectory(directoryPath);
            Input = Path.Combine(directoryPath, "input.pdf");
            Output = Path.Combine(directoryPath, "result.dwg");
            File.WriteAllBytes(Input, [0x25, 0x50, 0x44, 0x46]);
        }

        public string DirectoryPath { get; }
        public string Input { get; }
        public string Output { get; }

        public static TestFiles Create()
            => new(Path.Combine(Path.GetTempPath(), "TeyPdfCad.Bridge.Tests", Guid.NewGuid().ToString("N")));

        public static string[] Arguments(string input, string output, string jobId)
            =>
            [
                "--input", input,
                "--protocol-version", BridgeProtocol.SupportedProtocolVersion,
                "--output", output,
                "--job-id", jobId,
                "--pipeline-version", "v1",
                "--output-units", "drawing",
                "--preserve-source-geometry", "true",
                "--recognizer-profile", "default",
            ];

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
