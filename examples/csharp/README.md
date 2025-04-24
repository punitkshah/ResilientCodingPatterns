# C# Resilient Coding Pattern Examples

This directory contains C# code examples that demonstrate the implementation of various resilient coding patterns for Azure applications.

## Examples

1. [Resilience Fundamentals](./01-ResilienceFundamentals/)
2. [Retry Pattern](./02-RetryPattern/)
3. [Circuit Breaker Pattern](./03-CircuitBreakerPattern/)
4. [Timeout & Cancellation Patterns](./04-TimeoutCancellation/)
5. [Bulkhead Pattern](./05-BulkheadPattern/)
6. [Saga Pattern](./06-SagaPattern/)
7. [Graceful Degradation Pattern](./07-GracefulDegradationPattern/)
8. [Queue-Based Load Leveling](./08-QueueBasedLoadLeveling/)
9. [Chaos Engineering](./09-ChaosEngineering/)
10. [API Versioning](./10-ApiVersioning/)
11. [Data Replication & Consistency](./11-DataReplicationConsistency/)
12. [Sharding Pattern](./12-ShardingPattern/)
13. [Event Sourcing](./13-EventSourcing/)

## Setup

Each example is self-contained in its own directory. To run an example:

1. Navigate to the example directory
2. Restore dependencies: `dotnet restore`
3. Run the example: `dotnet run`

## Dependencies

These examples use various libraries to implement resilience patterns:

- [Polly](https://github.com/App-vNext/Polly) - For retry, circuit breaker, timeout, bulkhead, and fallback patterns
- [Microsoft.Extensions.Http.Resilience](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience) - For resilient HTTP clients
- [Microsoft.Extensions.Caching](https://www.nuget.org/packages/Microsoft.Extensions.Caching.Memory/) - For caching patterns
- [Azure SDK for .NET](https://github.com/Azure/azure-sdk-for-net) - For Azure-specific resilience examples
