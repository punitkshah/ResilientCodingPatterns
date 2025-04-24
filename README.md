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
2. [Retry Pattern](docs/02-retry-pattern.md)
3. [Circuit Breaker Pattern](docs/03-circuit-breaker-pattern.md)
4. [Timeout & Cancellation Patterns](docs/04-timeout-cancellation.md)
5. [Bulkhead Pattern](docs/05-bulkhead-pattern.md)
6. [Fallback Pattern](docs/06-fallback-pattern.md)
7. [Cache-Aside Pattern](docs/07-cache-aside-pattern.md)
8. [Throttling & Rate Limiting](docs/08-throttling-rate-limiting.md)
9. [Health Monitoring & Logging](docs/09-health-monitoring-logging.md)
10. [Resiliency Testing](docs/10-resiliency-testing.md)

## Code Examples

- [C# Examples](examples/csharp/README.md)
- [TypeScript Examples](examples/typescript/README.md)

## Contributing

Please feel free to contribute examples, improve documentation, or suggest additional patterns that should be covered.

## License

This project is licensed under the MIT License - see the LICENSE file for details.
