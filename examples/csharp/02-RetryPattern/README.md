# Retry Pattern - C# Example

This example demonstrates how to implement the Retry Pattern in C# using the Polly library.

## Overview

The Retry Pattern enables an application to handle transient failures when connecting to a service or network resource by automatically retrying a failed operation. This is especially useful in cloud environments where momentary service unavailability is common.

## Features Demonstrated

- Basic retry with exponential backoff
- Retry with jitter to avoid thundering herd
- Retry with circuit breaker to prevent excessive retries
- Retry with timeout to prevent long-running operations

## How to Run

```bash
dotnet restore
dotnet run
```

## Implementation Details

The example shows how to configure retry policies for:

1. HTTP client operations
2. Database operations
3. Azure Storage operations

Each example demonstrates different retry strategies suitable for the specific operation type.
