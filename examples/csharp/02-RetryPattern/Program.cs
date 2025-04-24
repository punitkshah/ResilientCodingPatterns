using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Polly.Retry;
using Polly.Timeout;
using Polly.Wrap;

namespace RetryPattern
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Retry Pattern Examples");
            Console.WriteLine("======================\n");

            // Setup dependency injection with HttpClient factory 
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // Get configured HttpClient
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();

            // Example 1: Basic HTTP retry
            await BasicHttpRetryExample(httpClient);

            // Example 2: Advanced retry with policy options
            await AdvancedRetryExample();

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        static void ConfigureServices(IServiceCollection services)
        {
            // Configure HttpClient with retry policy
            services.AddHttpClient("RetryClient", client =>
            {
                client.BaseAddress = new Uri("https://api.example.com/");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddPolicyHandler(GetRetryPolicy());

            // Register HttpClient as singleton
            services.AddSingleton(provider =>
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("RetryClient"));
        }

        static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            // Define retry policy for HTTP requests
            return HttpPolicyExtensions
                .HandleTransientHttpError() // HttpRequestException, 5XX and 408 status codes
                .Or<TimeoutRejectedException>() // Handle timeouts
                .WaitAndRetryAsync(
                    retryCount: 3, // Number of retries
                    sleepDurationProvider: retryAttempt => 
                        // Exponential backoff with jitter
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + 
                        TimeSpan.FromMilliseconds(new Random().Next(0, 100)), 
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        // Log retry attempt
                        Console.WriteLine($"Retry {retryAttempt} after {timespan.TotalSeconds:N1}s due to {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString() ?? "unknown error"}");
                    }
                );
        }

        static async Task BasicHttpRetryExample(HttpClient httpClient)
        {
            Console.WriteLine("Example 1: Basic HTTP Retry");
            Console.WriteLine("---------------------------");

            try
            {
                // This will likely fail (using example.com as dummy endpoint)
                Console.WriteLine("Sending HTTP request to simulated endpoint...");
                var response = await httpClient.GetAsync("/api/products/1");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response: {content}");
                }
                else
                {
                    Console.WriteLine($"Request failed with status {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Request ultimately failed after retries: {ex.Message}");
            }
        }

        static async Task AdvancedRetryExample()
        {
            Console.WriteLine("\nExample 2: Advanced Retry");
            Console.WriteLine("-------------------------");

            // Create a retry policy for database operations
            var dbRetryPolicy = Policy
                .Handle<InvalidOperationException>() // Specific exception type
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    3, // Number of retries
                    retryAttempt => TimeSpan.FromSeconds(retryAttempt), // Linear backoff
                    (exception, timeSpan, retryCount, context) =>
                    {
                        Console.WriteLine($"Database operation failed with: {exception.Message}. Retry {retryCount} after {timeSpan.TotalSeconds}s");
                    }
                );

            // Create a timeout policy
            var timeoutPolicy = Policy
                .TimeoutAsync(TimeSpan.FromSeconds(1), TimeoutStrategy.Pessimistic, 
                    (context, timespan, task) =>
                    {
                        Console.WriteLine($"Operation timed out after {timespan.TotalSeconds}s");
                        return Task.CompletedTask;
                    });

            // Combine policies
            var policyWrap = Policy.WrapAsync(dbRetryPolicy, timeoutPolicy);

            // Execute operation with policy
            try
            {
                await policyWrap.ExecuteAsync(async () =>
                {
                    Console.WriteLine("Executing database operation simulation...");
                    
                    // Simulate a database operation that fails
                    await SimulateDatabaseOperation();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Operation ultimately failed after all retries: {ex.Message}");
            }
        }

        static async Task SimulateDatabaseOperation()
        {
            // Simulate database operation with random failures
            var random = new Random();
            var failureChance = random.Next(100);
            
            if (failureChance < 70) // 70% chance of failure
            {
                if (failureChance < 40)
                {
                    // Simulate timeout
                    Console.WriteLine("Simulating a slow operation...");
                    await Task.Delay(2000); // Wait 2 seconds, which is longer than our timeout policy
                }
                else
                {
                    // Simulate a database error
                    Console.WriteLine("Simulating a database error...");
                    throw new InvalidOperationException("Database connection error");
                }
            }
            
            Console.WriteLine("Database operation completed successfully");
        }
    }
}
