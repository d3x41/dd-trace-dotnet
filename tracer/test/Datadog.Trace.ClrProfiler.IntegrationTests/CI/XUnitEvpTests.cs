// <copyright file="XUnitEvpTests.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Datadog.Trace.Ci.Ipc;
using Datadog.Trace.Ci.Ipc.Messages;
using Datadog.Trace.Ci.Tags;
using Datadog.Trace.Configuration;
using Datadog.Trace.ExtensionMethods;
using Datadog.Trace.Telemetry;
using Datadog.Trace.TestHelpers;
using Datadog.Trace.TestHelpers.Ci;
using Datadog.Trace.Vendors.Newtonsoft.Json;
using FluentAssertions;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

namespace Datadog.Trace.ClrProfiler.IntegrationTests.CI;

[UsesVerify]
public abstract class XUnitEvpTests : TestingFrameworkEvpTest
{
    private const string TestBundleName = "Samples.XUnitTests";
    private const string TestSuiteName = "Samples.XUnitTests.TestSuite";
    private const string UnSkippableSuiteName = "Samples.XUnitTests.UnSkippableSuite";
    private const int ExpectedTestCount = 16;

    public XUnitEvpTests(ITestOutputHelper output)
        : base("XUnitTests", output)
    {
        SetServiceName("xunit-tests-evp");
        SetServiceVersion("1.0.0");
    }

    public static IEnumerable<object[]> GetData()
    {
        foreach (var version in PackageVersions.XUnit)
        {
            // EVP version to remove, expects gzip
            yield return version.Concat("evp_proxy/v2", true);
            yield return version.Concat("evp_proxy/v4", false);
        }
    }

    [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:Parameter should not span multiple lines", Justification = "readability")]
    public static IEnumerable<object[]> GetDataForEarlyFlakeDetection()
    {
        foreach (var row in GetData())
        {
            // settings json, efd tests json, expected spans, friendlyName

            // EFD for all tests
            yield return row.Concat(
                new MockData(
                    GetSettingsJson("true", "true", "false", "0"),
                    """
                    {
                        "data":{
                            "id":"lNemDTwOV8U",
                            "type":"ci_app_libraries_tests",
                            "attributes":{
                                "tests":{}
                            }
                        }
                    }
                    """,
                    string.Empty),
                1,
                124,
                "all_efd");

            // EFD with 1 test to bypass (TraitPassTest)
            yield return row.Concat(
                new MockData(
                    GetSettingsJson("true", "true", "false", "0"),
                    """
                    {
                        "data":{
                            "id":"lNemDTwOV8U",
                            "type":"ci_app_libraries_tests",
                            "attributes":{
                                "tests":{
                                    "Samples.XUnitTests":{
                                        "Samples.XUnitTests.TestSuite":["TraitPassTest"]
                                    }
                                }
                            }
                        }
                    }
                    """,
                    string.Empty),
                1,
                115,
                "efd_with_test_bypass");
        }
    }

    [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:Parameter should not span multiple lines", Justification = "readability")]
    public static IEnumerable<object[]> GetDataForQuarantinedTests()
    {
        foreach (var row in GetData())
        {
            yield return row.Concat(
                new MockData(
                    GetSettingsJson("false", "false", "true", "0"),
                    string.Empty,
                    """
                    {
                        "data": {
                            "id": "878448902e138d339eb9f26a778851f35582b5ea3622ae8ab446209d232399af",
                            "type": "ci_app_libraries_tests",
                            "attributes": {
                                "modules": {
                                    "Samples.XUnitTests": {
                                        "suites": {
                                            "Samples.XUnitTests.TestSuite": {
                                                "tests": {
                                                    "TraitErrorTest": {
                                                        "properties": {
                                                            "quarantined": true
                                                        }
                                                    },
                                                    "SimpleErrorTest": {
                                                        "properties": {
                                                            "quarantined": true
                                                        }
                                                    },
                                                    "SimpleErrorParameterizedTest": {
                                                        "properties": {
                                                            "quarantined": true
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    """),
                0,
                16,
                "quarantined_tests");
        }
    }

    [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:Parameter should not span multiple lines", Justification = "readability")]
    public static IEnumerable<object[]> GetDataForDisabledTests()
    {
        foreach (var row in GetData())
        {
            yield return row.Concat(
                new MockData(
                    GetSettingsJson("false", "false", "true", "0"),
                    string.Empty,
                    """
                    {
                        "data": {
                            "id": "878448902e138d339eb9f26a778851f35582b5ea3622ae8ab446209d232399af",
                            "type": "ci_app_libraries_tests",
                            "attributes": {
                                "modules": {
                                    "Samples.XUnitTests": {
                                        "suites": {
                                            "Samples.XUnitTests.TestSuite": {
                                                "tests": {
                                                    "TraitErrorTest": {
                                                        "properties": {
                                                            "disabled": true
                                                        }
                                                    },
                                                    "SimpleErrorTest": {
                                                        "properties": {
                                                            "disabled": true
                                                        }
                                                    },
                                                    "SimpleErrorParameterizedTest": {
                                                        "properties": {
                                                            "disabled": false
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    """),
                1,
                16,
                "disabled_tests");
        }
    }

    [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:Parameter should not span multiple lines", Justification = "readability")]
    public static IEnumerable<object[]> GetDataForAttemptToFixTests()
    {
        foreach (var row in GetData())
        {
            yield return row.Concat(
                new MockData(
                    GetSettingsJson("false", "false", "true", "10"),
                    string.Empty,
                    """
                    {
                        "data": {
                            "id": "878448902e138d339eb9f26a778851f35582b5ea3622ae8ab446209d232399af",
                            "type": "ci_app_libraries_tests",
                            "attributes": {
                                "modules": {
                                    "Samples.XUnitTests": {
                                        "suites": {
                                            "Samples.XUnitTests.TestSuite": {
                                                "tests": {
                                                    "TraitErrorTest": {
                                                        "properties": {
                                                            "quarantined": true,
                                                            "attempt_to_fix": true
                                                        }
                                                    },
                                                    "SimpleErrorTest": {
                                                        "properties": {
                                                            "disabled": true,
                                                            "attempt_to_fix": true
                                                        }
                                                    },
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    """),
                1,
                34,
                "quarantined_and_disabled");
        }
    }

    public virtual async Task SubmitTraces(string packageVersion, string evpVersionToRemove, bool expectedGzip)
    {
        var tests = new List<MockCIVisibilityTest>();
        var testsCopy = new List<MockCIVisibilityTest>();
        var testSuites = new List<MockCIVisibilityTestSuite>();
        var testModules = new List<MockCIVisibilityTestModule>();

        // Inject session
        InjectSession(
            out var sessionId,
            out var sessionCommand,
            out var sessionWorkingDirectory,
            out var gitRepositoryUrl,
            out var gitBranch,
            out var gitCommitSha);

        var codeCoverageReceived = new StrongBox<bool>(false);
        var name = $"session_{sessionId}";
        using var ipcServer = new IpcServer(name);
        ipcServer.SetMessageReceivedCallback(
            o =>
            {
                codeCoverageReceived.Value = codeCoverageReceived.Value || o is SessionCodeCoverageMessage;
            });

        string[] messages = null;

        using var logsIntake = new MockLogsIntakeForCiVisibility();
        EnableDirectLogSubmission(logsIntake.Port, nameof(IntegrationId.XUnit), nameof(XUnitTests));

        using var agent = EnvironmentHelper.GetMockAgent(useTelemetry: true, useStatsD: true);
        agent.Configuration.Endpoints = agent.Configuration.Endpoints.Where(e => !e.Contains(evpVersionToRemove)).ToArray();

        const string correlationId = "2e8a36bda770b683345957cc6c15baf9";
        agent.EventPlatformProxyPayloadReceived += (sender, e) =>
        {
            if (e.Value.PathAndQuery.EndsWith("api/v2/libraries/tests/services/setting"))
            {
                e.Value.Response = new MockTracerResponse("""{"data":{"id":"b5a855bffe6c0b2ae5d150fb6ad674363464c816","type":"ci_app_tracers_test_service_settings","attributes":{"code_coverage":false,"efd_enabled":false,"flaky_test_retries_enabled":false,"itr_enabled":true,"require_git":false,"tests_skipping":true}}} """, 200);
                return;
            }

            if (e.Value.PathAndQuery.EndsWith("api/v2/ci/tests/skippable"))
            {
                e.Value.Response = new MockTracerResponse($"{{\"data\":[],\"meta\":{{\"correlation_id\":\"{correlationId}\"}}}}", 200);
                return;
            }

            if (e.Value.PathAndQuery.EndsWith("api/v2/citestcycle"))
            {
                e.Value.Headers["Content-Encoding"].Should().Be(expectedGzip ? "gzip" : null);

                var payload = JsonConvert.DeserializeObject<MockCIVisibilityProtocol>(e.Value.BodyInJson);
                ValidateMetadata(payload.Metadata, sessionCommand);
                if (payload.Events?.Length > 0)
                {
                    foreach (var @event in payload.Events)
                    {
                        if (@event.Content.ToString() is { } eventContent)
                        {
                            if (@event.Type == SpanTypes.Test)
                            {
                                tests.Add(JsonConvert.DeserializeObject<MockCIVisibilityTest>(eventContent));
                                testsCopy.Add(JsonConvert.DeserializeObject<MockCIVisibilityTest>(eventContent));
                            }
                            else if (@event.Type == SpanTypes.TestSuite)
                            {
                                testSuites.Add(JsonConvert.DeserializeObject<MockCIVisibilityTestSuite>(eventContent));
                            }
                            else if (@event.Type == SpanTypes.TestModule)
                            {
                                testModules.Add(JsonConvert.DeserializeObject<MockCIVisibilityTestModule>(eventContent));
                            }
                        }
                    }
                }
            }
        };

        using var processResult = await RunDotnetTestSampleAndWaitForExit(
                                      agent,
                                      arguments: "--collect:\"XPlat Code Coverage\"",
                                      packageVersion: packageVersion,
                                      expectedExitCode: 1);

        // Check the tests, suites and modules count
        Assert.Equal(ExpectedTestCount, tests.Count);
        Assert.Equal(2, testSuites.Count);
        Assert.Single(testModules);

        var testSuite = testSuites.First(suite => suite.Resource == TestSuiteName);
        var unskippableTestSuite = testSuites.First(suite => suite.Resource == UnSkippableSuiteName);
        var testModule = testModules[0];

        // Check Suite
        Assert.True(tests.All(t => t.TestSuiteId == testSuite.TestSuiteId || t.TestSuiteId == unskippableTestSuite.TestSuiteId));
        testSuite.TestModuleId.Should().Be(testModule.TestModuleId);
        testSuite.Meta.Should().ContainKey(TestTags.SourceFile);
        testSuite.Meta.Should().ContainKey(TestTags.CodeOwners);
        unskippableTestSuite.TestModuleId.Should().Be(testModule.TestModuleId);

        // ITR tags inside the test suite
        testSuite.Metrics.Should().Contain(IntelligentTestRunnerTags.SkippingCount, 1);

        // Check Module
        Assert.True(tests.All(t => t.TestModuleId == testSuite.TestModuleId));

        // ITR tags inside the test module
        testModule.Metrics.Should().Contain(IntelligentTestRunnerTags.SkippingCount, 1);
        testModule.Meta.Should().Contain(IntelligentTestRunnerTags.SkippingType, IntelligentTestRunnerTags.SkippingTypeTest);
        testModule.Meta.Should().Contain(IntelligentTestRunnerTags.TestsSkipped, "true");

        // Check Session
        tests.Should().OnlyContain(t => t.TestSessionId == testSuite.TestSessionId);
        testSuite.TestSessionId.Should().Be(testModule.TestSessionId);
        unskippableTestSuite.TestSessionId.Should().Be(testModule.TestSessionId);
        testModule.TestSessionId.Should().Be(sessionId);

        // ***************************************************************************
        try
        {
            foreach (var targetTest in tests)
            {
                // Remove decision maker tag (not used by the backend for civisibility)
                targetTest.Meta.Remove(Tags.Propagated.DecisionMaker);

                // Remove EFD tags
                targetTest.Meta.Remove(TestTags.TestIsNew);
                targetTest.Meta.Remove(TestTags.TestIsRetry);

                // Remove capabilities
                targetTest.Meta.Remove(CapabilitiesTags.LibraryCapabilitiesAutoTestRetries);
                targetTest.Meta.Remove(CapabilitiesTags.LibraryCapabilitiesTestManagementQuarantine);
                targetTest.Meta.Remove(CapabilitiesTags.LibraryCapabilitiesEarlyFlakeDetection);
                targetTest.Meta.Remove(CapabilitiesTags.LibraryCapabilitiesTestImpactAnalysis);
                targetTest.Meta.Remove(CapabilitiesTags.LibraryCapabilitiesTestManagementDisable);
                targetTest.Meta.Remove(CapabilitiesTags.LibraryCapabilitiesTestManagementAttemptToFix);

                // Remove user provided service tag
                targetTest.Meta.Remove(CommonTags.UserProvidedTestServiceTag);

                // check the name
                Assert.Equal("xunit.test", targetTest.Name);

                // check correlationId
                Assert.Equal(correlationId, targetTest.CorrelationId);

                // check the CIEnvironmentValues decoration.
                CheckCIEnvironmentValuesDecoration(targetTest, gitRepositoryUrl, gitBranch, gitCommitSha);

                // check the runtime values
                CheckRuntimeValues(targetTest);

                // check the bundle name
                AssertTargetSpanEqual(targetTest, TestTags.Bundle, TestBundleName);
                AssertTargetSpanEqual(targetTest, TestTags.Module, TestBundleName);

                // check the suite name
                AssertTargetSpanAnyOf(targetTest, TestTags.Suite, TestSuiteName, UnSkippableSuiteName);

                // check the test type
                AssertTargetSpanEqual(targetTest, TestTags.Type, TestTags.TypeTest);

                // check the test framework
                AssertTargetSpanContains(targetTest, TestTags.Framework, "xUnit");
                Assert.True(targetTest.Meta.Remove(TestTags.FrameworkVersion));

                // check the version
                AssertTargetSpanEqual(targetTest, "version", "1.0.0");

                // checks the origin tag
                CheckOriginTag(targetTest);

                // checks the source tags
                AssertTargetSpanExists(targetTest, TestTags.SourceFile);

                // checks code owners
                AssertTargetSpanExists(targetTest, TestTags.CodeOwners);

                // Check the Environment
                AssertTargetSpanEqual(targetTest, Tags.Env, "integration_tests");

                // Language
                AssertTargetSpanEqual(targetTest, Tags.Language, TracerConstants.Language);

                // CI Library Language
                AssertTargetSpanEqual(targetTest, CommonTags.LibraryVersion, TracerConstants.AssemblyVersion);

                // Check Session data
                AssertTargetSpanEqual(targetTest, TestTags.Command, sessionCommand);
                AssertTargetSpanEqual(targetTest, TestTags.CommandWorkingDirectory, sessionWorkingDirectory);

                // Unskippable data
                if (targetTest.Meta[TestTags.Name] != "UnskippableTest")
                {
                    AssertTargetSpanEqual(targetTest, IntelligentTestRunnerTags.UnskippableTag, "false");
                    AssertTargetSpanEqual(targetTest, IntelligentTestRunnerTags.ForcedRunTag, "false");
                }

                // check specific test span
                switch (targetTest.Meta[TestTags.Name])
                {
                    case "SimplePassTest":
                        CheckSimpleTestSpan(targetTest);
                        break;

                    case "SimpleSkipFromAttributeTest":
                        CheckSimpleSkipFromAttributeTest(targetTest);
                        AssertTargetSpanEqual(targetTest, IntelligentTestRunnerTags.SkippedBy, "false");
                        break;

                    case "SkipByITRSimulation":
                        AssertTargetSpanEqual(targetTest, TestTags.Status, TestTags.StatusSkip);
                        AssertTargetSpanEqual(targetTest, TestTags.SkipReason, IntelligentTestRunnerTags.SkippedByReason);
                        AssertTargetSpanEqual(targetTest, IntelligentTestRunnerTags.SkippedBy, "true");
                        break;

                    case "SimpleErrorTest":
                        CheckSimpleErrorTest(targetTest);
                        break;

                    case "TraitPassTest":
                        CheckSimpleTestSpan(targetTest);
                        CheckTraitsValues(targetTest);
                        break;

                    case "TraitSkipFromAttributeTest":
                        CheckSimpleSkipFromAttributeTest(targetTest);
                        CheckTraitsValues(targetTest);
                        AssertTargetSpanEqual(targetTest, IntelligentTestRunnerTags.SkippedBy, "false");
                        break;

                    case "TraitErrorTest":
                        CheckSimpleErrorTest(targetTest);
                        CheckTraitsValues(targetTest);
                        break;

                    case "SimpleParameterizedTest":
                        CheckSimpleTestSpan(targetTest);
                        AssertTargetSpanAnyOf(
                            targetTest,
                            TestTags.Parameters,
                            "{\"metadata\":{\"test_name\":\"Samples.XUnitTests.TestSuite.SimpleParameterizedTest(xValue: 1, yValue: 1, expectedResult: 2)\"},\"arguments\":{\"xValue\":\"1\",\"yValue\":\"1\",\"expectedResult\":\"2\"}}",
                            "{\"metadata\":{\"test_name\":\"Samples.XUnitTests.TestSuite.SimpleParameterizedTest(xValue: 2, yValue: 2, expectedResult: 4)\"},\"arguments\":{\"xValue\":\"2\",\"yValue\":\"2\",\"expectedResult\":\"4\"}}",
                            "{\"metadata\":{\"test_name\":\"Samples.XUnitTests.TestSuite.SimpleParameterizedTest(xValue: 3, yValue: 3, expectedResult: 6)\"},\"arguments\":{\"xValue\":\"3\",\"yValue\":\"3\",\"expectedResult\":\"6\"}}",
                            "{\"metadata\":{\"test_name\":\"SimpleParameterizedTest(xValue: 1, yValue: 1, expectedResult: 2)\"},\"arguments\":{\"xValue\":\"1\",\"yValue\":\"1\",\"expectedResult\":\"2\"}}",
                            "{\"metadata\":{\"test_name\":\"SimpleParameterizedTest(xValue: 2, yValue: 2, expectedResult: 4)\"},\"arguments\":{\"xValue\":\"2\",\"yValue\":\"2\",\"expectedResult\":\"4\"}}",
                            "{\"metadata\":{\"test_name\":\"SimpleParameterizedTest(xValue: 3, yValue: 3, expectedResult: 6)\"},\"arguments\":{\"xValue\":\"3\",\"yValue\":\"3\",\"expectedResult\":\"6\"}}");
                        break;

                    case "SimpleSkipParameterizedTest":
                        CheckSimpleSkipFromAttributeTest(targetTest);
                        AssertTargetSpanEqual(targetTest, IntelligentTestRunnerTags.SkippedBy, "false");
                        break;

                    case "SimpleErrorParameterizedTest":
                        CheckSimpleErrorTest(targetTest);
                        AssertTargetSpanAnyOf(
                            targetTest,
                            TestTags.Parameters,
                            "{\"metadata\":{\"test_name\":\"Samples.XUnitTests.TestSuite.SimpleErrorParameterizedTest(xValue: 1, yValue: 0, expectedResult: 2)\"},\"arguments\":{\"xValue\":\"1\",\"yValue\":\"0\",\"expectedResult\":\"2\"}}",
                            "{\"metadata\":{\"test_name\":\"Samples.XUnitTests.TestSuite.SimpleErrorParameterizedTest(xValue: 2, yValue: 0, expectedResult: 4)\"},\"arguments\":{\"xValue\":\"2\",\"yValue\":\"0\",\"expectedResult\":\"4\"}}",
                            "{\"metadata\":{\"test_name\":\"Samples.XUnitTests.TestSuite.SimpleErrorParameterizedTest(xValue: 3, yValue: 0, expectedResult: 6)\"},\"arguments\":{\"xValue\":\"3\",\"yValue\":\"0\",\"expectedResult\":\"6\"}}",
                            "{\"metadata\":{\"test_name\":\"SimpleErrorParameterizedTest(xValue: 1, yValue: 0, expectedResult: 2)\"},\"arguments\":{\"xValue\":\"1\",\"yValue\":\"0\",\"expectedResult\":\"2\"}}",
                            "{\"metadata\":{\"test_name\":\"SimpleErrorParameterizedTest(xValue: 2, yValue: 0, expectedResult: 4)\"},\"arguments\":{\"xValue\":\"2\",\"yValue\":\"0\",\"expectedResult\":\"4\"}}",
                            "{\"metadata\":{\"test_name\":\"SimpleErrorParameterizedTest(xValue: 3, yValue: 0, expectedResult: 6)\"},\"arguments\":{\"xValue\":\"3\",\"yValue\":\"0\",\"expectedResult\":\"6\"}}");
                        break;

                    case "UnskippableTest":
                        AssertTargetSpanEqual(targetTest, IntelligentTestRunnerTags.UnskippableTag, "true");
                        AssertTargetSpanEqual(targetTest, IntelligentTestRunnerTags.ForcedRunTag, "false");
                        CheckSimpleTestSpan(targetTest);
                        break;
                }

                // check remaining tag (only the name)
                Assert.Single(targetTest.Meta);

                // check if we received code coverage information at session level
                codeCoverageReceived.Value.Should().BeTrue();
            }
        }
        catch
        {
            WriteSpans(tests);
            throw;
        }

        // Snapshots
        var settings = VerifyHelper.GetCIVisibilitySpanVerifierSettings();
        settings.UseTextForParameters("packageVersion=all");
        settings.DisableRequireUniquePrefix();
        settings.UseTypeName(nameof(XUnitEvpTests));
        await Verifier.Verify(testsCopy.OrderBy(s => s.Resource).ThenBy(s => s.Meta.GetValueOrDefault(TestTags.Parameters)), settings);

        // ***************************************************************************
        // Check logs
        messages = logsIntake.Logs.Select(i => i.Message).Where(m => m.StartsWith("Test:")).ToArray();

        Assert.Contains(messages, m => m.StartsWith("Test:SimplePassTest"));
        Assert.Contains(messages, m => m.StartsWith("Test:SimpleErrorTest"));
        Assert.Contains(messages, m => m.StartsWith("Test:TraitPassTest"));
        Assert.Contains(messages, m => m.StartsWith("Test:TraitErrorTest"));
        Assert.Contains(messages, m => m.StartsWith("Test:SimpleParameterizedTest"));
        Assert.Contains(messages, m => m.StartsWith("Test:SimpleErrorParameterizedTest"));

        // Smoke check telemetry
        await agent.WaitForLatestTelemetryAsync(x => ((TelemetryData)x).IsRequestType(TelemetryRequestTypes.AppClosing));
        var allData = agent.Telemetry.Cast<TelemetryData>().ToArray();

        // we will have multiple app closing events
        TelemetryHelper.GetMetricData(allData, "endpoint_payload.requests", "endpoint:test_cycle", singleAppClosing: false)
                       .Should()
                       .NotBeEmpty()
                       .And.OnlyContain(x => HasCorrectCompressionTag(x.Tags, expectedGzip));
    }

    public virtual async Task EarlyFlakeDetection(string packageVersion, string evpVersionToRemove, bool expectedGzip, MockData mockData, int expectedExitCode, int expectedSpans, string friendlyName)
    {
        // TODO: Fix alpine flakiness
        Skip.If(EnvironmentHelper.IsAlpine(), "This test is currently flaky in alpine, an issue has been opened to investigate the root cause. Meanwhile we are skipping it.");
        await ExecuteTestAsync(
                packageVersion,
                evpVersionToRemove,
                expectedGzip,
                new TestScenario(
                    nameof(XUnitEvpTests),
                    friendlyName,
                    mockData,
                    expectedExitCode,
                    expectedSpans,
                    true,
                    (in ExecutionData data) =>
                    {
                        // Check the tests, suites and modules count
                        Assert.Equal(2, data.TestSuites.Count);
                        Assert.Single(data.TestModules);
                    },
                    useDotnetExec: false))
           .ConfigureAwait(false);
    }

    public virtual async Task QuarantinedTests(string packageVersion, string evpVersionToRemove, bool expectedGzip, MockData mockData, int expectedExitCode, int expectedSpans, string friendlyName)
    {
        await ExecuteTestAsync(
                packageVersion,
                evpVersionToRemove,
                expectedGzip,
                new TestScenario(
                    nameof(XUnitEvpTests),
                    friendlyName,
                    mockData,
                    expectedExitCode,
                    expectedSpans,
                    true,
                    (in ExecutionData data) =>
                    {
                        // Check the tests, suites and modules count
                        Assert.Equal(2, data.TestSuites.Count);
                        Assert.Single(data.TestModules);
                    },
                    useDotnetExec: false))
           .ConfigureAwait(false);
    }

    public virtual async Task DisabledTests(string packageVersion, string evpVersionToRemove, bool expectedGzip, MockData mockData, int expectedExitCode, int expectedSpans, string friendlyName)
    {
        await ExecuteTestAsync(
                packageVersion,
                evpVersionToRemove,
                expectedGzip,
                new TestScenario(
                    nameof(XUnitEvpTests),
                    friendlyName,
                    mockData,
                    expectedExitCode,
                    expectedSpans,
                    true,
                    (in ExecutionData data) =>
                    {
                        // Check the tests, suites and modules count
                        Assert.Equal(2, data.TestSuites.Count);
                        Assert.Single(data.TestModules);
                    },
                    useDotnetExec: false))
           .ConfigureAwait(false);
    }

    public virtual async Task AttemptToFixTests(string packageVersion, string evpVersionToRemove, bool expectedGzip, MockData mockData, int expectedExitCode, int expectedSpans, string friendlyName)
    {
        await ExecuteTestAsync(
                packageVersion,
                evpVersionToRemove,
                expectedGzip,
                new TestScenario(
                    nameof(XUnitEvpTests),
                    friendlyName,
                    mockData,
                    expectedExitCode,
                    expectedSpans,
                    true,
                    (in ExecutionData data) =>
                    {
                        // Check the tests, suites and modules count
                        Assert.Equal(2, data.TestSuites.Count);
                        Assert.Single(data.TestModules);
                    },
                    useDotnetExec: false))
           .ConfigureAwait(false);
    }

    private static bool HasCorrectCompressionTag(string[] tags, bool isGzipped)
        => isGzipped ? tags.Contains("rq_compressed:true") : !tags.Contains("rq_compressed:true");
}
