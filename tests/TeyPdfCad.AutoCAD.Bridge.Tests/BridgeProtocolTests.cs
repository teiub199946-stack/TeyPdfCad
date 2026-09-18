using System.Text;
using TeyPdfCad.AutoCAD.Bridge;
using Xunit;

namespace TeyPdfCad.AutoCAD.Bridge.Tests;

public sealed class BridgeProtocolTests
{
    [Theory]
    [InlineData("AC1032", true)]
    [InlineData("AC1027", true)]
    [InlineData("AC1", false)]
    [InlineData("NOTDWG", false)]
    [InlineData("AC10x2", false)]
    public void Recognizes_only_the_six_byte_dwg_header_contract(string value, bool expected)
    {
        Assert.Equal(expected, BridgeProtocol.IsDwgHeader(Encoding.ASCII.GetBytes(value)));
    }

    [Fact]
    public void Accepts_ok_status_case_insensitively()
    {
        var path = WriteStatus(" OK \r\n");
        try
        {
            Assert.True(BridgeProtocol.TryReadSuccessStatus(path, out var error));
            Assert.Equal("AutoCAD bridge did not report a successful reconstruction.", error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("error|dimension_measurement_failed", "dimension_measurement_failed")]
    [InlineData("title block failed", "title block failed")]
    [InlineData("", "AutoCAD bridge did not report a successful reconstruction.")]
    public void Reports_non_success_statuses_without_claiming_success(string value, string expectedError)
    {
        var path = WriteStatus(value);
        try
        {
            Assert.False(BridgeProtocol.TryReadSuccessStatus(path, out var error));
            Assert.Equal(expectedError, error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_status_file_fails_closed()
    {
        var path = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Bridge.Tests", Guid.NewGuid().ToString("N"), "missing.status");

        Assert.False(BridgeProtocol.TryReadSuccessStatus(path, out var error));
        Assert.Contains("status file could not be read", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Exit_codes_are_stable_for_automation_contracts()
    {
        Assert.Equal(2, BridgeProtocol.InvalidRequestExitCode);
        Assert.Equal(21, BridgeProtocol.MissingHostDependencyExitCode);
        Assert.Equal(22, BridgeProtocol.ProcessFailureExitCode);
        Assert.Equal(23, BridgeProtocol.ReconstructionFailureExitCode);
        Assert.Equal(24, BridgeProtocol.CancelledExitCode);
    }

    private static string WriteStatus(string value)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TeyPdfCad.Bridge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "bridge-status.txt");
        File.WriteAllText(path, value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
