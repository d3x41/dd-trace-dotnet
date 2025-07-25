// <copyright file="TransportTests.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Datadog.Trace.Agent;
using Datadog.Trace.Configuration;
using Datadog.Trace.Telemetry;
using Datadog.Trace.TestHelpers;
using FluentAssertions;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;

namespace Datadog.Trace.ClrProfiler.IntegrationTests
{
    [UsesVerify]
    public class TransportTests : TestHelper
    {
        private static readonly TestTransports[] Transports = new[]
        {
            TestTransports.Tcp,
            TestTransports.WindowsNamedPipe,
#if NETCOREAPP3_1_OR_GREATER
            TestTransports.Uds,
#endif
        };

        private readonly ITestOutputHelper _output;

        // Using Telemetry sample as it's simple
        public TransportTests(ITestOutputHelper output)
            : base("Telemetry", output)
        {
            _output = output;
        }

        public static IEnumerable<object[]> Data =>
            from transport in Transports
            from dataPipelineEnabled in new[] { false } // TODO: re-enable datapipeline tests - Currently it causes too much flake with duplicate spans
            select new object[] { transport, dataPipelineEnabled };

        [SkippableTheory]
        [MemberData(nameof(Data))]
        [Trait("Category", "EndToEnd")]
        [Trait("RunOnWindows", "True")]
        [Flaky("Named pipes is flaky", maxRetries: 3)]
        public async Task TransportsWorkCorrectly(TestTransports transport, bool dataPipelineEnabled)
        {
            var transportType = TracesTransportTypeFromTestTransport(transport);
            await RunTest(transportType, dataPipelineEnabled);
        }

        private TracesTransportType TracesTransportTypeFromTestTransport(TestTransports transport)
        {
            return transport switch
            {
                TestTransports.Tcp => TracesTransportType.Default,
                TestTransports.WindowsNamedPipe => TracesTransportType.WindowsNamedPipe,
                TestTransports.Uds => TracesTransportType.UnixDomainSocket,
                _ => throw new InvalidOperationException($"Unknown transport {transport}"),
            };
        }

        private async Task RunTest(TracesTransportType transportType, bool dataPipelineEnabled)
        {
            const int expectedSpanCount = 1;

            if (transportType == TracesTransportType.WindowsNamedPipe && !EnvironmentTools.IsWindows())
            {
                throw new SkipException("WindowsNamedPipe transport is only supported on Windows");
            }

            SetEnvironmentVariable(ConfigurationKeys.TraceDataPipelineEnabled, dataPipelineEnabled.ToString());
            var transportTypeGeneral = GetTransport(transportType);
            EnvironmentHelper.EnableTransport(transportTypeGeneral);

            using var telemetry = this.ConfigureTelemetry();
            var canUseDogStatsD = EnvironmentHelper.CanUseStatsD(transportTypeGeneral);
            using var agent = GetAgent(transportType, canUseDogStatsD);
            agent.Output = Output;

            int httpPort = TcpPortProvider.GetOpenPort();
            Output.WriteLine($"Assigning port {httpPort} for the httpPort.");
            using (var processResult = await RunSampleAndWaitForExit(agent, arguments: $"Port={httpPort}"))
            {
                ExitCodeException.ThrowIfNonZero(processResult.ExitCode, processResult.StandardError);

                var spans = await agent.WaitForSpansAsync(expectedSpanCount);

                await VerifyHelper.VerifySpans(spans, VerifyHelper.GetSpanVerifierSettings())
                                  .DisableRequireUniquePrefix()
                                  .UseFileName("TransportTests");
            }

            await telemetry.AssertConfigurationAsync(ConfigTelemetryData.AgentTraceTransport, transportType.ToString());
            MockTracerAgent GetAgent(TracesTransportType type, bool canUseDogStatsd)
                => type switch
                {
                    TracesTransportType.Default => MockTracerAgent.Create(Output, useStatsd: canUseDogStatsd),
                    TracesTransportType.WindowsNamedPipe => MockTracerAgent.Create(Output, new WindowsPipesConfig($"trace-{Guid.NewGuid()}", $"metrics-{Guid.NewGuid()}") { UseDogstatsD = canUseDogStatsd }),
#if NETCOREAPP3_1_OR_GREATER
                    TracesTransportType.UnixDomainSocket
                        => MockTracerAgent.Create(Output, new UnixDomainSocketConfig(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), canUseDogStatsd ? Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()) : null) { UseDogstatsD = canUseDogStatsd }),
#endif
                    _ => throw new InvalidOperationException("Unsupported transport type " + type),
                };

            TestTransports GetTransport(TracesTransportType type)
                => type switch
                {
                    TracesTransportType.Default => TestTransports.Tcp,
                    TracesTransportType.UnixDomainSocket => TestTransports.Uds,
                    TracesTransportType.WindowsNamedPipe => TestTransports.WindowsNamedPipe,
                    _ => throw new InvalidOperationException("Unsupported transport type " + type),
                };
        }
    }
}
