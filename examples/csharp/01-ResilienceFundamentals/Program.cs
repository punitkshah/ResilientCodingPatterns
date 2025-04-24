using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ResilienceFundamentals
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Resilience Fundamentals Examples");
            Console.WriteLine("===============================\n");

            // Example 1: Basic error handling with try-catch
            await BasicErrorHandlingExample();

            // Example 2: Asynchronous error handling
            await AsyncErrorHandlingExample();

            // Example 3: Health monitoring
            await HealthMonitoringExample();

            // Example 4: Graceful degradation
            await GracefulDegradationExample();

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        static async Task BasicErrorHandlingExample()
        {
            Console.WriteLine("Example 1: Basic Error Handling");
            Console.WriteLine("------------------------------");

            try
            {
                // Simulate an operation that may fail
                SimulateOperation(shouldFail: true);
                Console.WriteLine("Operation completed successfully.");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Caught specific exception: {ex.Message}");
                // Handle specific exception type with appropriate recovery logic
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Caught general exception: {ex.Message}");
                // Generic fallback handling
            }
            finally
            {
                Console.WriteLine("Cleanup operations in finally block");
                // Resource cleanup, logging, etc.
            }
        }

        static void SimulateOperation(bool shouldFail)
        {
            Console.WriteLine("Executing operation...");
            
            if (shouldFail)
            {
                throw new InvalidOperationException("Operation failed due to business rule violation");
            }
            
            // Operation succeeds if we get here
        }

        static async Task AsyncErrorHandlingExample()
        {
            Console.WriteLine("\nExample 2: Asynchronous Error Handling");
            Console.WriteLine("-------------------------------------");

            try
            {
                // Use a cancellation token with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await SimulateAsyncOperation(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation was cancelled or timed out");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Async operation failed: {ex.Message}");
            }
        }

        static async Task SimulateAsyncOperation(CancellationToken cancellationToken)
        {
            Console.WriteLine("Starting asynchronous operation...");
            
            // Register cancellation callback
            cancellationToken.Register(() => 
                Console.WriteLine("Cancellation was requested"));
            
            for (int i = 0; i < 5; i++)
            {
                // Check for cancellation before each operation
                cancellationToken.ThrowIfCancellationRequested();
                
                Console.WriteLine($"Async operation step {i+1}");
                await Task.Delay(500, cancellationToken);
            }
            
            Console.WriteLine("Asynchronous operation completed");
        }

        static async Task HealthMonitoringExample()
        {
            Console.WriteLine("\nExample 3: Health Monitoring");
            Console.WriteLine("---------------------------");

            var healthMonitor = new HealthMonitor();
            
            // Register components to monitor
            healthMonitor.RegisterComponent("Database", () => CheckDatabaseHealth());
            healthMonitor.RegisterComponent("API Service", () => CheckApiHealth());
            healthMonitor.RegisterComponent("Cache", () => CheckCacheHealth());
            
            // Run health check
            await healthMonitor.CheckAllComponentsAsync();
        }

        static async Task<bool> CheckDatabaseHealth()
        {
            Console.WriteLine("Checking database connection...");
            await Task.Delay(300); // Simulate actual health check
            var isHealthy = new Random().Next(100) > 10; // 90% chance of being healthy
            Console.WriteLine($"Database health: {(isHealthy ? "Healthy" : "Unhealthy")}");
            return isHealthy;
        }

        static async Task<bool> CheckApiHealth()
        {
            Console.WriteLine("Checking API service...");
            await Task.Delay(200); // Simulate actual health check
            var isHealthy = new Random().Next(100) > 20; // 80% chance of being healthy
            Console.WriteLine($"API health: {(isHealthy ? "Healthy" : "Unhealthy")}");
            return isHealthy;
        }

        static async Task<bool> CheckCacheHealth()
        {
            Console.WriteLine("Checking cache service...");
            await Task.Delay(100); // Simulate actual health check
            var isHealthy = new Random().Next(100) > 5; // 95% chance of being healthy
            Console.WriteLine($"Cache health: {(isHealthy ? "Healthy" : "Unhealthy")}");
            return isHealthy;
        }

        static async Task GracefulDegradationExample()
        {
            Console.WriteLine("\nExample 4: Graceful Degradation");
            Console.WriteLine("-------------------------------");

            var service = new ServiceWithFallback();
            
            // Try to get data from primary source
            var result = await service.GetDataAsync();
            Console.WriteLine($"Final result: {result}");
        }
    }

    class HealthMonitor
    {
        private Dictionary<string, Func<Task<bool>>> components = new();

        public void RegisterComponent(string name, Func<Task<bool>> healthCheck)
        {
            components[name] = healthCheck;
        }

        public async Task CheckAllComponentsAsync()
        {
            Console.WriteLine("Running health checks for all components...");
            
            int healthyCount = 0;
            foreach (var component in components)
            {
                bool isHealthy = await component.Value();
                if (isHealthy) healthyCount++;
            }
            
            var overallHealth = (double)healthyCount / components.Count;
            Console.WriteLine($"Overall system health: {overallHealth:P0}");
            
            if (overallHealth < 0.5)
            {
                Console.WriteLine("ALERT: System is in critical state!");
            }
            else if (overallHealth < 0.8)
            {
                Console.WriteLine("WARNING: Some system components are unhealthy");
            }
            else
            {
                Console.WriteLine("System is healthy");
            }
        }
    }

    class ServiceWithFallback
    {
        public async Task<string> GetDataAsync()
        {
            try
            {
                Console.WriteLine("Attempting to retrieve data from primary source...");
                return await GetDataFromPrimarySourceAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Primary source failed: {ex.Message}");
                Console.WriteLine("Falling back to secondary source...");
                
                try
                {
                    return await GetDataFromSecondarySourceAsync();
                }
                catch (Exception secondaryEx)
                {
                    Console.WriteLine($"Secondary source failed: {secondaryEx.Message}");
                    Console.WriteLine("Using cached data as last resort...");
                    return GetCachedData();
                }
            }
        }

        private async Task<string> GetDataFromPrimarySourceAsync()
        {
            await Task.Delay(300); // Simulate network delay
            
            // Simulate 70% chance of failure
            if (new Random().NextDouble() < 0.7)
            {
                throw new HttpRequestException("Primary source connection timeout");
            }
            
            return "Data from primary source (most up-to-date)";
        }

        private async Task<string> GetDataFromSecondarySourceAsync()
        {
            await Task.Delay(200); // Simulate network delay
            
            // Simulate 30% chance of failure
            if (new Random().NextDouble() < 0.3)
            {
                throw new HttpRequestException("Secondary source unavailable");
            }
            
            return "Data from secondary source (slightly delayed)";
        }

        private string GetCachedData()
        {
            // Cache always works, but might be stale
            return "Cached data from last successful retrieval (may be stale)";
        }
    }
}
