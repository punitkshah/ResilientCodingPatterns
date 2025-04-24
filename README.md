# Resilient Coding Patterns for Azure Applications

This repository contains guidelines and code examples for developing resilient cloud applications on Azure. The guidelines focus on patterns and practices that help your applications gracefully handle transient errors, service outages, and other cloud-specific challenges.

## Why Resilience Matters in Cloud Applications

1. **Distributed Systems Complexity**: Cloud applications are inherently distributed, increasing the likelihood of partial failures.
2. **Service Dependencies**: Applications typically rely on multiple services that may experience outages or degraded performance.
3. **Network Unreliability**: Communication between services happens over networks that can be slow, interrupted, or fail.
4. **Cost Optimization**: Properly handling failures can prevent unnecessary resource consumption and cost overruns.
5. **User Experience**: Graceful degradation preserves critical functionality even when some components fail.

## Table of Contents

1. [Resilience Fundamentals](docs/01-resilience-fundamentals.md)
2. [Cache Aside Pattern](docs/02-cache-aside-pattern.md)
3. [Queue-Based Load Leveling](docs/03-queue-based-load-leveling.md)
4. [Strangler Fig Pattern](docs/04-strangler-fig-pattern.md)
5. [Ability to Scale to Different SKUs](docs/05-scaling-to-different-skus.md)
6. [Managing Connection Strings and Secrets](docs/06-managing-connection-strings-secrets.md)
7. [Timeout Handling](docs/07-timeout-handling.md)
8. [Graceful Error Logging & Propagation](docs/08-graceful-error-logging.md)
9. [Fallback Mechanism](docs/09-fallback-mechanism.md)

## Additional Resilience Patterns

10. [Bulkhead Pattern](docs/10-bulkhead-pattern.md)
11. [Retry Pattern](docs/11-retry-pattern.md)
12. [Circuit Breaker Pattern](docs/12-circuit-breaker-pattern.md)
13. [Saga Pattern](docs/13-saga-pattern.md)
14. [Graceful Degradation Pattern](docs/14-graceful-degradation-pattern.md)
15. [Chaos Engineering](docs/15-chaos-engineering.md)
16. [API Versioning](docs/16-api-versioning.md)
17. [Data Replication & Consistency](docs/17-data-replication-consistency.md)
18. [Sharding Pattern](docs/18-sharding-pattern.md)
19. [Event Sourcing](docs/19-event-sourcing.md)

## Code Examples

- [C# Examples](examples/csharp/README.md)
- [TypeScript Examples](examples/typescript/README.md)

## Contributing

Please feel free to contribute examples, improve documentation, or suggest additional patterns that should be covered.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
