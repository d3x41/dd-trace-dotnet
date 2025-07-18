// <copyright file="KafkaTests.cs" company="Datadog">
// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2017 Datadog, Inc.
// </copyright>

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Datadog.Trace.Configuration;
using Datadog.Trace.TestHelpers;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using Xunit.Abstractions;

namespace Datadog.Trace.ClrProfiler.IntegrationTests
{
    [Collection(nameof(KafkaTestsCollection))]
    [Trait("RequiresDockerDependency", "true")]
    public class KafkaTests : TracingIntegrationTest
    {
        private const int ExpectedSuccessProducerWithHandlerSpans = 20;
        private const int ExpectedSuccessProducerWithoutHandlerSpans = 10;
        private const int ExpectedSuccessProducerSpans = ExpectedSuccessProducerWithHandlerSpans + ExpectedSuccessProducerWithoutHandlerSpans;
        private const int ExpectedTombstoneProducerWithHandlerSpans = 20;
        private const int ExpectedTombstoneProducerWithoutHandlerSpans = 10;
        private const int ExpectedTombstoneProducerSpans = ExpectedTombstoneProducerWithHandlerSpans + ExpectedTombstoneProducerWithoutHandlerSpans;
        private const int ExpectedErrorProducerSpans = 2; // When no delivery handler, error can't be caught, so we don't test that case
        private const int ExpectedConsumerSpans = ExpectedSuccessProducerSpans + ExpectedTombstoneProducerSpans;
        private const int TotalExpectedSpanCount = ExpectedConsumerSpans
                                                 + ExpectedSuccessProducerSpans
                                                 + ExpectedTombstoneProducerSpans
                                                 + ExpectedErrorProducerSpans;

        private const string ErrorProducerResourceName = "Produce Topic INVALID-TOPIC";

        public KafkaTests(ITestOutputHelper output)
            : base("Kafka", output)
        {
            SetServiceVersion("1.0.0");
        }

        public static IEnumerable<object[]> GetEnabledConfig()
            => from packageVersionArray in PackageVersions.Kafka
               from metadataSchemaVersion in new[] { "v0", "v1" }
               select new[] { packageVersionArray[0], metadataSchemaVersion };

        public override Result ValidateIntegrationSpan(MockSpan span, string metadataSchemaVersion) =>
            span.Tags["span.kind"] switch
            {
                SpanKinds.Consumer => span.IsKafkaInbound(metadataSchemaVersion),
                SpanKinds.Producer => span.IsKafkaOutbound(metadataSchemaVersion),
                _ => throw new ArgumentException($"span.Tags[\"span.kind\"] is not a supported value for the Kafka integration: {span.Tags["span.kind"]}", nameof(span)),
            };

        [SkippableTheory]
        [MemberData(nameof(GetEnabledConfig))]
        [Trait("Category", "EndToEnd")]
        [Trait("Category", "ArmUnsupported")]
        public async Task SubmitsTraces(string packageVersion, string metadataSchemaVersion)
        {
            var topic = $"sample-topic-{TestPrefix}-{packageVersion}".Replace('.', '-');

            SetEnvironmentVariable("DD_TRACE_SPAN_ATTRIBUTE_SCHEMA", metadataSchemaVersion);
            var isExternalSpan = metadataSchemaVersion == "v0";
            var clientSpanServiceName = isExternalSpan ? $"{EnvironmentHelper.FullSampleName}-kafka" : EnvironmentHelper.FullSampleName;

            using var telemetry = this.ConfigureTelemetry();
            using var agent = EnvironmentHelper.GetMockAgent();
            using var processResult = await RunSampleAndWaitForExit(agent, arguments: topic, packageVersion: packageVersion);

            var allSpans = await agent.WaitForSpansAsync(TotalExpectedSpanCount, timeoutInMilliseconds: 10_000);
            using var assertionScope = new AssertionScope();
            // We use HaveCountGreaterOrEqualTo because _both_ consumers may handle the message
            // Due to manual/autocommit behaviour
            allSpans.Should().HaveCountGreaterOrEqualTo(TotalExpectedSpanCount);
            ValidateIntegrationSpans(allSpans, metadataSchemaVersion, expectedServiceName: clientSpanServiceName, isExternalSpan);

            var outboundOperationName = metadataSchemaVersion == "v0" ? "kafka.produce" : "kafka.send";
            var inboundOperationName = metadataSchemaVersion == "v0" ? "kafka.consume" : "kafka.process";

            var allProducerSpans = allSpans.Where(x => x.Name == outboundOperationName).ToList();
            var successfulProducerSpans = allProducerSpans.Where(x => x.Error == 0).ToList();
            var errorProducerSpans = allProducerSpans.Where(x => x.Error > 0).ToList();

            var allConsumerSpans = allSpans.Where(x => x.Name == inboundOperationName).ToList();
            var successfulConsumerSpans = allConsumerSpans.Where(x => x.Error == 0).ToList();
            var errorConsumerSpans = allConsumerSpans.Where(x => x.Error > 0).ToList();

            VerifyProducerSpanProperties(successfulProducerSpans, serviceName: clientSpanServiceName, resourceName: GetSuccessfulResourceName("Produce", topic), ExpectedSuccessProducerSpans + ExpectedTombstoneProducerSpans);
            VerifyProducerSpanProperties(errorProducerSpans, serviceName: clientSpanServiceName, resourceName: ErrorProducerResourceName, ExpectedErrorProducerSpans);

            // Only successful spans with a delivery handler will have an offset
            successfulProducerSpans
               .Where(span => span.Tags.ContainsKey(Tags.KafkaOffset))
               .Select(span => span.Tags[Tags.KafkaOffset])
               .Should()
               .OnlyContain(tag => Regex.IsMatch(tag, @"^[0-9]+$"))
               .And.HaveCount(ExpectedSuccessProducerSpans + ExpectedTombstoneProducerSpans);

            // Only successful spans with a delivery handler will have a partition
            // Confirm partition is displayed correctly [0], [1]
            // https://github.com/confluentinc/confluent-kafka-dotnet/blob/master/src/Confluent.Kafka/Partition.cs#L217-L224
            successfulProducerSpans
               .Where(span => span.Tags.ContainsKey(Tags.KafkaPartition))
               .Select(span => span.Tags[Tags.KafkaPartition])
               .Should()
               .OnlyContain(tag => Regex.IsMatch(tag, @"^\[[0-9]+\]$"))
               .And.HaveCount(ExpectedSuccessProducerSpans + ExpectedTombstoneProducerSpans);

            successfulProducerSpans.Should().OnlyContain(span => span.Tags.ContainsKey(Tags.MessagingDestinationName) && span.Tags[Tags.MessagingDestinationName] == topic);

            allProducerSpans
               .Where(span => span.Tags.ContainsKey(Tags.KafkaTombstone))
               .Select(span => span.Tags[Tags.KafkaTombstone])
               .Should()
               .HaveCount(ExpectedTombstoneProducerSpans)
               .And.OnlyContain(tag => tag == "true");

            // verify have error
            errorProducerSpans.Should().OnlyContain(x => x.Tags.ContainsKey(Tags.ErrorType))
                              .And.ContainSingle(x => x.Tags[Tags.ErrorType] == "Confluent.Kafka.ProduceException`2[System.String,System.String]") // created by async handler
                              .And.ContainSingle(x => x.Tags[Tags.ErrorType] == "System.Exception"); // created by sync callback handler

            var producerSpanIds = successfulProducerSpans
                                 .Select(x => x.SpanId)
                                 .Should()
                                 .OnlyHaveUniqueItems()
                                 .And.Subject.ToImmutableHashSet();

            VerifyConsumerSpanProperties(successfulConsumerSpans, serviceName: clientSpanServiceName, resourceName: GetSuccessfulResourceName("Consume", topic), topic, ExpectedConsumerSpans);

            // every consumer span should be a child of a producer span.
            successfulConsumerSpans
               .Should()
               .OnlyContain(span => span.ParentId.HasValue)
               .And.OnlyContain(span => producerSpanIds.Contains(span.ParentId.Value));

            // HaveCountGreaterOrEqualTo because same message may be consumed by both
            successfulConsumerSpans
               .Where(span => span.Tags.ContainsKey(Tags.KafkaTombstone))
               .Select(span => span.Tags[Tags.KafkaTombstone])
               .Should()
               .HaveCountGreaterOrEqualTo(ExpectedTombstoneProducerSpans)
               .And.OnlyContain(tag => tag == "true");

            allConsumerSpans.Should().OnlyContain(x => x.Tags.ContainsKey(Tags.KafkaConsumerGroup));

            successfulConsumerSpans
               .Should()
               .OnlyContain(
                    x => x.Tags[Tags.KafkaConsumerGroup] == "Samples.Kafka.AutoCommitConsumer1"
                      || x.Tags[Tags.KafkaConsumerGroup] == "Samples.Kafka.ManualCommitConsumer2");

            // Error spans are created in 1.5.3 when the broker doesn't exist yet
            // Other package versions don't error, so won't create a span,
            // so no fixed number requirement
            if (errorConsumerSpans.Count > 0)
            {
                errorConsumerSpans.Should().OnlyContain(x => x.Tags[Tags.KafkaConsumerGroup] == "Samples.Kafka.FailingConsumer1");

                errorConsumerSpans
                   .Should()
                   .OnlyContain(x => x.Tags.ContainsKey(Tags.ErrorType))
                   .And.OnlyContain(x => x.Tags[Tags.ErrorMsg].Contains("Broker: Unknown topic or partition"))
                   .And.OnlyContain(x => x.Tags[Tags.ErrorType] == "Confluent.Kafka.ConsumeException");
            }

            await telemetry.AssertIntegrationEnabledAsync(IntegrationId.Kafka);
        }

        private void VerifyProducerSpanProperties(List<MockSpan> producerSpans, string serviceName, string resourceName, int expectedCount)
        {
            producerSpans.Should()
                         .HaveCount(expectedCount)
                         .And.OnlyContain(x => x.Service == serviceName)
                         .And.OnlyContain(x => x.Resource == resourceName)
                         .And.OnlyContain(x => x.Metrics.ContainsKey(Tags.Measured) && x.Metrics[Tags.Measured] == 1.0);
        }

        private void VerifyConsumerSpanProperties(List<MockSpan> consumerSpans, string serviceName, string resourceName, string topic, int expectedCount)
        {
            // HaveCountGreaterOrEqualTo because same message may be consumed by both
            consumerSpans.Should()
                         .HaveCountGreaterOrEqualTo(expectedCount)
                         .And.OnlyContain(x => x.Service == serviceName)
                         .And.OnlyContain(x => x.Resource == resourceName)
                         .And.OnlyContain(x => x.Metrics.ContainsKey(Tags.Measured) && x.Metrics[Tags.Measured] == 1.0)
                         .And.OnlyContain(x => x.Metrics.ContainsKey(Metrics.MessageQueueTimeMs))
                         .And.OnlyContain(x => x.Tags.ContainsKey(Tags.MessagingDestinationName) && x.Tags[Tags.MessagingDestinationName] == topic)
                         .And.OnlyContain(x => x.Tags.ContainsKey(Tags.KafkaOffset) && Regex.IsMatch(x.Tags[Tags.KafkaOffset], @"^[0-9]+$"))
                         .And.OnlyContain(x => x.Tags.ContainsKey(Tags.KafkaPartition) && Regex.IsMatch(x.Tags[Tags.KafkaPartition], @"^\[[0-9]+\]$"));
        }

        private string GetSuccessfulResourceName(string type, string topic)
        {
            return $"{type} Topic {topic}";
        }

        [CollectionDefinition(nameof(KafkaTestsCollection), DisableParallelization = true)]
        public class KafkaTestsCollection
        {
        }
    }
}
