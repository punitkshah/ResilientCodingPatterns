# TypeScript Resilient Coding Pattern Examples

This directory contains TypeScript code examples that demonstrate the implementation of various resilient coding patterns for Azure applications.

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
2. Install dependencies: `npm install`
3. Run the example: `npm start`

## Dependencies

These examples use various libraries to implement resilience patterns:

- [axios-retry](https://github.com/softonic/axios-retry) - For HTTP request retries
- [opossum](https://github.com/nodeshift/opossum) - For circuit breaker implementation
- [bottleneck](https://github.com/SGrondin/bottleneck) - For rate limiting and throttling
- [node-cache](https://github.com/node-cache/node-cache) - For caching patterns
- [@azure/storage-queue](https://www.npmjs.com/package/@azure/storage-queue) - For queue-based load leveling examples
- [p-timeout](https://github.com/sindresorhus/p-timeout) - For timeout patterns
