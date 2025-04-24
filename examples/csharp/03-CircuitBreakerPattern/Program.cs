using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;

namespace CircuitBreakerPattern
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Circuit Breaker Pattern Examples");
            Console.WriteLine("================================\n");

            // Setup dependency injection with HttpClient factory 
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // Get configured HttpClient
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();

            // Basic circuit breaker demo
            await DemoCircuitBreakerAsync(httpClient);

            // Advanced circuit breaker demo with fallback
            await DemoAdvancedCircuitBreakerAsync();

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        static void ConfigureServices(IServiceCollection services)
        {
            // Configure HttpClient with circuit breaker policy
            services.AddHttpClient("CircuitBreakerClient", client =>
            {
                client.BaseAddress = new Uri("https://api.example.com/");
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddPolicyHandler(GetCircuitBreakerPolicy());

            // Register HttpClient as singleton
            services.AddSingleton(provider =>
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("CircuitBreakerClient"));
        }

        static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 3,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (outcome, breakDelay) =>
                    {
                        Console.WriteLine($"Circuit breaking for {breakDelay.TotalSeconds:N1}s due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString() ?? "unknown error"}");
                    },
                    onReset: () =>
                    {
                        Console.WriteLine("Circuit reset and is allowing requests");
                    },
                    onHalfOpen: () =>
                    {
                        Console.WriteLine("Circuit is half-open and is testing the service");
                    });
        }

        static async Task DemoCircuitBreakerAsync(HttpClient httpClient)
        {
            Console.WriteLine("Basic Circuit Breaker Demo");
            Console.WriteLine("-------------------------");

            for (int i = 1; i <= 5; i++)
            {
                try
                {
                    Console.WriteLine($"Request {i}: Sending HTTP request...");
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
                catch (BrokenCircuitException ex)
                {
                    Console.WriteLine($"Request {i}: Circuit is open! Error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Request {i}: Error: {ex.Message}");
                }

                await Task.Delay(1000); // Wait 1 second between requests
            }
        }

        static async Task DemoAdvancedCircuitBreakerAsync()
        {
            Console.WriteLine("\nAdvanced Circuit Breaker Demo with Fallback");
            Console.WriteLine("-------------------------------------------");

            // Define the circuit breaker policy
            var circuitBreaker = Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 2,
                    durationOfBreak: TimeSpan.FromSeconds(5),
                    onBreak: (ex, breakDelay) => 
                        Console.WriteLine($"Circuit broken for {breakDelay.TotalSeconds:N1}s due to: {ex.Message}"),
                    onReset: () => 
                        Console.WriteLine("Circuit reset and is allowing requests"),
                    onHalfOpen: () => 
                        Console.WriteLine("Circuit is half-open and is testing the service")
                );

            // Define a fallback policy
            var fallbackPolicy = Policy
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackAction: async ct => Console.WriteLine("Fallback: Using cached or default response"),
                    onFallbackAsync: async ex => Console.WriteLine($"Fallback triggered due to: {ex.Exception.Message}")
                );

            // Combine policies - the fallback policy wraps the circuit breaker
            var policyWrap = fallbackPolicy.WrapAsync(circuitBreaker);

            // Demo the advanced policy
            for (int i = 1; i <= 8; i++)
            {
                await policyWrap.ExecuteAsync(async () => 
                {
                    Console.WriteLine($"Request {i}: Executing operation...");
                    
                    // Simulate a service that fails for the first 4 requests
                    if (i <= 4)
                    {
                        throw new TimeoutException("Service timeout");
                    }
                    
                    Console.WriteLine("Operation executed successfully");
                });

                await Task.Delay(1000); // Wait 1 second between operations
            }
        }
    }
}
