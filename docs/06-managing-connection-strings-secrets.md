# Managing Connection Strings and Secrets in Azure Applications

## Overview

Proper management of connection strings and secrets is crucial for maintaining the security, maintainability, and resilience of cloud applications. This document outlines patterns and best practices for handling sensitive configuration data in Azure applications.

## Problem Statement

Applications need to securely store and access sensitive information such as:
- Database connection strings
- API keys and authentication tokens
- Storage account credentials
- Service principal secrets
- Certificate keys

Hardcoding these values or storing them in configuration files leads to security risks, complicates deployment across environments, and makes secret rotation challenging.

## Solution: Secure Connection String and Secret Management

1. **Externalize Secrets from Code**: Store secrets separate from application code
2. **Use Managed Secret Stores**: Leverage services designed for secret management
3. **Implement Access Controls**: Restrict access to secrets based on the principle of least privilege
4. **Enable Secret Rotation**: Design systems to handle regular secret rotation with minimal disruption
5. **Follow Environment-Specific Configurations**: Use different settings for development, testing, and production environments

## Implementation Strategies

### 1. Azure Key Vault Integration

**Why**: Azure Key Vault provides a secure, centralized repository for managing secrets, keys, and certificates.

**Guidelines**:
- Store all connection strings and sensitive configuration in Key Vault
- Use Managed Identities for authentication when possible
- Implement secret caching to reduce Key Vault access frequency
- Set up alerts for secret access and modification

**C# Example**:
```csharp
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Threading.Tasks;

public class SecretManager
{
    private readonly SecretClient _secretClient;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);
    private readonly ILogger<SecretManager> _logger;

    public SecretManager(
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<SecretManager> logger)
    {
        var keyVaultUrl = configuration["KeyVault:Url"];
        _secretClient = new SecretClient(
            new Uri(keyVaultUrl),
            new DefaultAzureCredential());
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> GetSecretAsync(string secretName)
    {
        string cacheKey = $"secret:{secretName}";
        
        // Try to get from cache first
        if (_cache.TryGetValue(cacheKey, out string cachedSecret))
        {
            _logger.LogDebug("Retrieved secret {SecretName} from cache", secretName);
            return cachedSecret;
        }

        try
        {
            // Get from Key Vault if not in cache
            _logger.LogDebug("Fetching secret {SecretName} from Key Vault", secretName);
            KeyVaultSecret secret = await _secretClient.GetSecretAsync(secretName);
            
            // Cache the secret
            _cache.Set(
                cacheKey, 
                secret.Value, 
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _cacheDuration
                });
            
            return secret.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret {SecretName} from Key Vault", secretName);
            throw;
        }
    }
}
```

**TypeScript Example**:
```typescript
import { DefaultAzureCredential } from '@azure/identity';
import { SecretClient } from '@azure/keyvault-secrets';

export class SecretManager {
  private secretClient: SecretClient;
  private cache: Map<string, { value: string, expiry: Date }> = new Map();
  private cacheDurationMs: number = 10 * 60 * 1000; // 10 minutes
  private logger: any;

  constructor(keyVaultUrl: string, logger: any) {
    this.secretClient = new SecretClient(
      keyVaultUrl,
      new DefaultAzureCredential()
    );
    this.logger = logger;
  }

  async getSecret(secretName: string): Promise<string> {
    const cacheKey = `secret:${secretName}`;
    const now = new Date();
    
    // Try to get from cache first
    const cachedItem = this.cache.get(cacheKey);
    if (cachedItem && cachedItem.expiry > now) {
      this.logger.debug(`Retrieved secret ${secretName} from cache`);
      return cachedItem.value;
    }

    try {
      // Get from Key Vault if not in cache
      this.logger.debug(`Fetching secret ${secretName} from Key Vault`);
      const secret = await this.secretClient.getSecret(secretName);
      
      // Cache the secret
      const expiry = new Date(now.getTime() + this.cacheDurationMs);
      this.cache.set(cacheKey, { 
        value: secret.value || '', 
        expiry 
      });
      
      return secret.value || '';
    } catch (error) {
      this.logger.error(`Error retrieving secret ${secretName} from Key Vault: ${error.message}`);
      throw error;
    }
  }
}
```

### 2. Environment-Based Configuration

**Why**: Different environments (development, testing, production) require different configuration approaches.

**Guidelines**:
- Use environment-specific configuration sources
- Never use production credentials in development environments
- Use local emulators or contained environments for development
- Use Key Vault references in app configuration for production

**ASP.NET Core Example**:
```csharp
public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                var env = context.HostingEnvironment;
                
                // Base configuration from appsettings.json
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                      .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
                
                // Add environment variables
                config.AddEnvironmentVariables();
                
                // In production, use Key Vault
                if (env.IsProduction() || env.IsStaging())
                {
                    var builtConfig = config.Build();
                    var keyVaultUrl = builtConfig["KeyVault:Url"];
                    
                    config.AddAzureKeyVault(
                        new Uri(keyVaultUrl),
                        new DefaultAzureCredential());
                }
                
                // In development, use user secrets
                if (env.IsDevelopment())
                {
                    config.AddUserSecrets<Program>();
                }
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
}
```

### 3. Azure App Configuration

**Why**: Azure App Configuration provides a centralized service for managing application settings separate from code.

**Guidelines**:
- Use for feature flags and configuration management
- Store non-sensitive configuration centrally
- Leverage Key Vault references for sensitive values
- Implement configuration refresh for real-time updates

**C# Example**:
```csharp
public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                var env = context.HostingEnvironment;
                var settings = config.Build();
                
                // Connect to Azure App Configuration
                config.AddAzureAppConfiguration(options =>
                {
                    options.Connect(settings["AppConfig:ConnectionString"])
                           // Load all settings with no label
                           .Select("*", string.Empty)
                           // Load environment-specific settings
                           .Select("*", env.EnvironmentName)
                           // Enable Key Vault references
                           .ConfigureKeyVault(kv =>
                           {
                               kv.SetCredential(new DefaultAzureCredential());
                           })
                           // Configure to reload configuration if changed
                           .ConfigureRefresh(refresh =>
                           {
                               refresh.Register("SentinelKey", refreshAll: true)
                                      .SetCacheExpiration(TimeSpan.FromMinutes(5));
                           });
                });
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
}

// In Startup.cs
public void ConfigureServices(IServiceCollection services)
{
    // Add Azure App Configuration middleware for dynamic refresh
    services.AddAzureAppConfiguration();
    
    // Other service registrations
}

public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // Use Azure App Configuration middleware
    app.UseAzureAppConfiguration();
    
    // Other middleware
}
```

### 4. Managed Identities for Authentication

**Why**: Managed Identities eliminate the need to store and manage service credentials.

**Guidelines**:
- Use System-assigned identities when possible
- Grant appropriate RBAC permissions to the identity
- Use the DefaultAzureCredential to automatically use the Managed Identity

**Example**:
```csharp
// No secrets or connection strings required, just initialize clients with DefaultAzureCredential
public class StorageService
{
    private readonly BlobServiceClient _blobServiceClient;

    public StorageService(IConfiguration configuration)
    {
        var storageAccountUrl = configuration["Storage:AccountUrl"];
        
        // DefaultAzureCredential automatically uses Managed Identity in Azure
        // and falls back to other methods in development environments
        _blobServiceClient = new BlobServiceClient(
            new Uri(storageAccountUrl),
            new DefaultAzureCredential());
    }
    
    // Storage operations
}
```

### 5. Resilient Connection Management

**Why**: Connection failures are common, especially in distributed systems.

**Guidelines**:
- Implement connection retries with exponential backoff
- Utilize connection pooling where applicable
- Handle credential expiration and rotation gracefully
- Cache connections and credentials with appropriate TTLs

**C# Example with Polly**:
```csharp
public class ResilientDbConnection
{
    private readonly IAsyncPolicy<SqlConnection> _connectionPolicy;
    private readonly ILogger<ResilientDbConnection> _logger;
    private readonly string _connectionString;

    public ResilientDbConnection(
        string connectionString,
        ILogger<ResilientDbConnection> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
        
        _connectionPolicy = Policy<SqlConnection>
            .Handle<SqlException>(ex => IsTransientError(ex.Number))
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "SQL connection attempt {RetryCount} failed. Waiting {RetryTimespan} before next attempt.",
                        retryCount,
                        timespan);
                });
    }

    public async Task<SqlConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await _connectionPolicy.ExecuteAsync(async () =>
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        });
    }

    private bool IsTransientError(int errorNumber)
    {
        // Define SQL Server transient error numbers
        int[] transientErrors = { 4060, 10928, 10929, 40197, 40501, 40613, 49918, 49919, 49920 };
        return Array.IndexOf(transientErrors, errorNumber) >= 0;
    }
}
```

## Working with Azure App Services

For Azure App Services (Web Apps, Functions, etc.), consider these approaches:

### Application Settings

**Why**: App Settings provide a secure way to store configuration within the service.

**Guidelines**:
- Store connection strings in the "Connection Strings" section
- Use key vault references with format: @Microsoft.KeyVault(...)
- Use slots for swapping configurations with code
- Leverage configuration settings that auto-map to environment variables

**Example Connection String Setting**:
```
Name: SqlConnection
Value: @Microsoft.KeyVault(SecretUri=https://mykeyvault.vault.azure.net/secrets/SqlConnectionString/version)
Type: SQLAzure
```

### Azure Functions Environment Variables

**Why**: Functions have specific patterns for accessing connection settings.

**Guidelines**:
- Use app settings for simple values
- Use the built-in key vault integration for secrets
- Leverage DefaultAzureCredential for managed identity access

**Example**:
```csharp
// In Function
[FunctionName("ProcessData")]
public async Task Run(
    [ServiceBusTrigger("myqueue", Connection = "ServiceBusConnection")] string message,
    ILogger log)
{
    var secretManager = new SecretManager(
        Environment.GetEnvironmentVariable("KeyVaultUrl"),
        new DefaultAzureCredential());
        
    var dbConnectionString = await secretManager.GetSecretAsync("DbConnectionString");
    
    // Process with the connection string
}
```

## Common Pitfalls

1. **Hardcoding Secrets**: Embedding connections and secrets directly in code
   - Solution: Always externalize secrets to configuration or secret stores

2. **Insufficient Secret Rotation**: Not updating secrets regularly
   - Solution: Implement automated secret rotation with zero-downtime capabilities

3. **Insecure Local Development**: Using production secrets in development
   - Solution: Use user secrets or local emulators for development

4. **Over-Privileged Access**: Services with unnecessary access to secrets
   - Solution: Apply the principle of least privilege to all secret access

5. **Logging Secrets**: Accidental logging of connection strings
   - Solution: Implement secure logging practices and scrub sensitive data

## Azure Service-Specific Recommendations

### Azure SQL Database
- Use AAD authentication with Managed Identity when possible
- Store the managed connection string only with necessary access
- Keep server admin credentials in Key Vault with restricted access

### Azure Cosmos DB
- Use resource tokens for fine-grained access control
- Consider separate keys for read and write operations
- Rotate keys regularly using the alternate key for zero-downtime rotation

### Azure Storage
- Use SAS tokens with minimum required permissions
- Generate short-lived SAS tokens on demand
- Use Managed Identity with access to specific containers, not the whole account

### Azure Service Bus
- Use separate connection strings for send and receive operations
- Create topic-specific authorization rules
- Leverage Managed Identity authentication when available

## Next Steps

Continue to the [Graceful Error Logging & Propagation](docs/graceful-error-logging.md) pattern to learn how to effectively handle and communicate errors in your application.
