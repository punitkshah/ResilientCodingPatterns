# Retry Pattern - TypeScript Implementation

This example demonstrates the implementation of the Retry Pattern in TypeScript.

## Overview

The Retry Pattern enables an application to handle transient failures by transparently retrying a failed operation. This pattern is particularly useful when interacting with components that might experience occasional failures due to network issues, timeouts, or temporary service unavailability.

## Key Concepts

- **Retry Count**: Number of retry attempts before giving up
- **Retry Delay**: Time to wait between retry attempts
- **Exponential Backoff**: Progressively longer delays between retries
- **Jitter**: Small random variations in delay times to prevent thundering herd problems

## Implementation Details

This example demonstrates three approaches to implementing the Retry Pattern:

1. **Basic Retry**: Using the axios-retry library with Axios HTTP client
2. **Custom Retry**: A generic retry function with exponential backoff and jitter
3. **Retry with Circuit Breaker**: Combining retry with the circuit breaker pattern for enhanced resilience

## Running the Example

```bash
# Install dependencies
npm install

# Run the example
npm start
```

## Dependencies

- axios - For making HTTP requests
- axios-retry - Retry interceptor for Axios
- TypeScript and supporting devDependencies

## Additional Resources

- [Retry Pattern - Microsoft docs](https://docs.microsoft.com/en-us/azure/architecture/patterns/retry)
- [Axios Retry documentation](https://github.com/softonic/axios-retry)
