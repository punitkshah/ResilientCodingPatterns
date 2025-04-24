# Data Replication and Consistency Patterns

## Overview

Data Replication and Consistency patterns focus on maintaining data availability and consistency across distributed systems. These patterns are essential for cloud applications that need to ensure data resilience against failures while balancing consistency requirements with performance and availability needs.

## Problem Statement

In distributed systems, data often needs to be replicated across multiple nodes or services for improved availability, scalability, and fault tolerance. However, ensuring consistency across replicas becomes challenging, especially during network partitions or service failures.

## Solution: Data Replication and Consistency Patterns

Several patterns exist to manage the trade-offs between consistency, availability, and partition tolerance (CAP theorem):

1. **Strong Consistency**: Ensures all replicas are always in sync
2. **Eventual Consistency**: Allows temporary inconsistencies but guarantees convergence
3. **Read-After-Write Consistency**: Ensures users always see their own writes
4. **Causal Consistency**: Maintains order of causally related operations
5. **Leader-Follower Replication**: One node handles writes, others handle reads

## Benefits

- Improves data availability during partial system failures
- Enables geographic distribution of data for lower latency
- Provides scalability for read-heavy workloads
- Creates resilience against hardware failures or regional outages
- Offers flexibility in balancing between consistency and availability

## Implementation Considerations

- Choose appropriate consistency levels based on business requirements
- Implement conflict detection and resolution mechanisms
- Consider the impact of network latency on replication performance
- Design for recovery after replica failures
- Balance the number of replicas against operational complexity

## Code Example: Leader-Follower Replication in C#

```csharp
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ResillientDataPatterns
{
    // Database abstraction for the example
    public interface IDatabase
    {
        Task<T> ReadAsync<T>(string key);
        Task WriteAsync<T>(string key, T value);
        string ConnectionString { get; }
        bool IsLeader { get; }
    }

    // Data replication service implementing leader-follower pattern
    public class DataReplicationService<T> where T : class
    {
        private readonly IDatabase _leaderDb;
        private readonly List<IDatabase> _followerDbs;
        private readonly ILogger<DataReplicationService<T>> _logger;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _replicationTimeout;

        public DataReplicationService(
            IDatabase leaderDb,
            IEnumerable<IDatabase> followerDbs,
            ILogger<DataReplicationService<T>> logger,
            TimeSpan? replicationTimeout = null)
        {
            _leaderDb = leaderDb ?? throw new ArgumentNullException(nameof(leaderDb));
            _followerDbs = new List<IDatabase>(followerDbs ?? 
                throw new ArgumentNullException(nameof(followerDbs)));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _replicationTimeout = replicationTimeout ?? TimeSpan.FromSeconds(10);
            
            if (!_leaderDb.IsLeader)
            {
                throw new ArgumentException("The leader database must be configured as a leader", nameof(leaderDb));
            }
        }

        // Read operation - supports read from leader or follower based on consistency needs
        public async Task<T> ReadAsync(string key, ReadConsistency consistency = ReadConsistency.Eventual)
        {
            try
            {
                // If strong consistency is required, always read from leader
                if (consistency == ReadConsistency.Strong)
                {
                    _logger.LogDebug("Reading with strong consistency from leader: {Key}", key);
                    return await _leaderDb.ReadAsync<T>(key);
                }

                // For eventual consistency, try to read from a follower first
                if (_followerDbs.Count > 0)
                {
                    // Simple round-robin selection for followers
                    // In real-world, use more sophisticated load balancing
                    int followerIndex = Math.Abs(key.GetHashCode()) % _followerDbs.Count;
                    try
                    {
                        _logger.LogDebug("Reading with eventual consistency from follower {FollowerIndex}: {Key}", 
                            followerIndex, key);
                        return await _followerDbs[followerIndex].ReadAsync<T>(key);
                    }
                    catch (Exception ex)
                    {
                        // If follower read fails, fallback to leader
                        _logger.LogWarning(ex, 
                            "Failed to read from follower {FollowerIndex}, falling back to leader: {Key}", 
                            followerIndex, key);
                    }
                }

                // Fallback to leader if no followers or follower read failed
                return await _leaderDb.ReadAsync<T>(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading data: {Key}", key);
                throw;
            }
        }

        // Write operation - writes to leader and replicates to followers
        public async Task WriteAsync(string key, T value)
        {
            // Use a lock to ensure writes are processed one at a time
            // This simplifies replication logic for the example
            await _writeLock.WaitAsync();
            
            try
            {
                // Write to leader first
                _logger.LogDebug("Writing data to leader: {Key}", key);
                await _leaderDb.WriteAsync(key, value);

                // Then replicate to followers
                if (_followerDbs.Count > 0)
                {
                    var replicationTasks = new List<Task>();
                    foreach (var follower in _followerDbs)
                    {
                        replicationTasks.Add(ReplicateToFollowerAsync(follower, key, value));
                    }

                    // Wait for all replications with timeout
                    var completedTask = await Task.WhenAny(
                        Task.WhenAll(replicationTasks),
                        Task.Delay(_replicationTimeout)
                    );

                    if (completedTask != Task.WhenAll(replicationTasks))
                    {
                        _logger.LogWarning("Replication timed out after {Timeout}ms for key: {Key}",
                            _replicationTimeout.TotalMilliseconds, key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing data: {Key}", key);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // Helper method to replicate data to a follower
        private async Task ReplicateToFollowerAsync(IDatabase follower, string key, T value)
        {
            try
            {
                _logger.LogDebug("Replicating to follower {ConnectionString}: {Key}", 
                    follower.ConnectionString, key);
                await follower.WriteAsync(key, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replicate to follower {ConnectionString}: {Key}", 
                    follower.ConnectionString, key);
                
                // In a real implementation, we might:
                // 1. Queue failed replications for retry
                // 2. Mark the follower as unhealthy
                // 3. Trigger an alert for operations team
            }
        }
    }

    // Consistency levels for read operations
    public enum ReadConsistency
    {
        Strong,    // Read from leader only
        Eventual   // Can read from followers
    }

    // Example usage
    public class UserDataService
    {
        private readonly DataReplicationService<UserProfile> _replicationService;

        public UserDataService(DataReplicationService<UserProfile> replicationService)
        {
            _replicationService = replicationService;
        }

        public async Task<UserProfile> GetUserProfileAsync(string userId, bool requireFreshData = false)
        {
            // Choose consistency level based on requirements
            var consistency = requireFreshData 
                ? ReadConsistency.Strong   // User expects absolutely fresh data
                : ReadConsistency.Eventual; // Slightly stale data is acceptable
                
            return await _replicationService.ReadAsync(userId, consistency);
        }

        public async Task UpdateUserProfileAsync(string userId, UserProfile profile)
        {
            await _replicationService.WriteAsync(userId, profile);
        }
    }

    public class UserProfile
    {
        public string UserId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
```

## Code Example: Eventual Consistency in TypeScript

```typescript
import { EventEmitter } from 'events';

// Interface for database operations
interface DatabaseClient {
  read<T>(key: string): Promise<T | null>;
  write<T>(key: string, value: T): Promise<void>;
  getName(): string;
  isLeader(): boolean;
}

// Simplified in-memory database implementation
class InMemoryDatabase implements DatabaseClient {
  private data: Map<string, any> = new Map();
  private readonly name: string;
  private readonly isLeaderNode: boolean;
  private readonly latency: number; // Simulated network latency in ms

  constructor(name: string, isLeader: boolean, latency: number = 0) {
    this.name = name;
    this.isLeaderNode = isLeader;
    this.latency = latency;
  }

  async read<T>(key: string): Promise<T | null> {
    // Simulate network latency
    if (this.latency > 0) {
      await new Promise(resolve => setTimeout(resolve, this.latency));
    }
    
    return (this.data.get(key) as T) || null;
  }

  async write<T>(key: string, value: T): Promise<void> {
    // Simulate network latency
    if (this.latency > 0) {
      await new Promise(resolve => setTimeout(resolve, this.latency));
    }
    
    this.data.set(key, value);
  }

  getName(): string {
    return this.name;
  }

  isLeader(): boolean {
    return this.isLeaderNode;
  }
}

// Enums and types
enum ConsistencyLevel {
  STRONG = 'strong',
  EVENTUAL = 'eventual',
  READ_AFTER_WRITE = 'read-after-write'
}

interface ReplicationOptions {
  consistencyLevel: ConsistencyLevel;
  replicationTimeout: number;
  minSuccessfulReplicas: number;
}

// Event bus for asynchronous replication
class ReplicationEventBus extends EventEmitter {
  publishReplicationEvent<T>(key: string, value: T): void {
    this.emit('replicate', { key, value, timestamp: Date.now() });
  }

  subscribeToReplication(callback: (data: any) => void): void {
    this.on('replicate', callback);
  }
}

// Main replication manager implementing eventual consistency
class DataReplicationManager {
  private leader: DatabaseClient;
  private followers: DatabaseClient[];
  private options: ReplicationOptions;
  private eventBus: ReplicationEventBus;
  private recentWrites: Map<string, number> = new Map(); // For read-after-write consistency

  constructor(
    leader: DatabaseClient,
    followers: DatabaseClient[],
    options?: Partial<ReplicationOptions>
  ) {
    if (!leader.isLeader()) {
      throw new Error('The leader database must be configured as a leader');
    }

    this.leader = leader;
    this.followers = followers;
    this.eventBus = new ReplicationEventBus();
    
    // Default options
    this.options = {
      consistencyLevel: ConsistencyLevel.EVENTUAL,
      replicationTimeout: 5000,
      minSuccessfulReplicas: Math.ceil(followers.length / 2), // Simple majority
      ...options
    };

    // Set up replication listeners for followers
    this.setupReplication();
  }

  private setupReplication(): void {
    // Each follower subscribes to replication events
    this.eventBus.subscribeToReplication(async (data) => {
      const { key, value } = data;
      
      // Replicate to all followers asynchronously
      const replicationPromises = this.followers.map(async (follower) => {
        try {
          await follower.write(key, value);
          console.log(`Replicated data to follower ${follower.getName()}: ${key}`);
          return true;
        } catch (error) {
          console.error(`Failed to replicate to follower ${follower.getName()}: ${key}`, error);
          return false;
        }
      });

      // Check for minimum successful replications
      const results = await Promise.allSettled(replicationPromises);
      const successCount = results.filter(r => r.status === 'fulfilled' && r.value).length;
      
      if (successCount < this.options.minSuccessfulReplicas) {
        console.warn(`Insufficient replication: only ${successCount}/${this.followers.length} successful for key ${key}`);
        // In real-world, you might trigger alerts or recovery processes here
      }
    });
  }

  async read<T>(key: string, options?: Partial<ReplicationOptions>): Promise<T | null> {
    const readOptions = { ...this.options, ...options };
    
    // Implement read-after-write consistency if needed
    if (readOptions.consistencyLevel === ConsistencyLevel.READ_AFTER_WRITE) {
      const recentWrite = this.recentWrites.get(key);
      
      if (recentWrite) {
        // If this client recently wrote this key, force read from leader
        console.log(`Read-after-write consistency enforced for key: ${key}`);
        return this.leader.read<T>(key);
      }
    }
    
    // For strong consistency, always read from leader
    if (readOptions.consistencyLevel === ConsistencyLevel.STRONG) {
      console.log(`Reading with strong consistency: ${key}`);
      return this.leader.read<T>(key);
    }
    
    // For eventual consistency, select a random follower
    if (this.followers.length > 0) {
      const followerIndex = Math.floor(Math.random() * this.followers.length);
      try {
        console.log(`Reading from follower ${this.followers[followerIndex].getName()}: ${key}`);
        return await this.followers[followerIndex].read<T>(key);
      } catch (error) {
        console.warn(`Failed to read from follower, falling back to leader: ${key}`, error);
      }
    }
    
    // Fallback to leader if needed
    return this.leader.read<T>(key);
  }

  async write<T>(key: string, value: T): Promise<void> {
    try {
      // Write to leader synchronously
      await this.leader.write<T>(key, value);
      console.log(`Wrote data to leader: ${key}`);
      
      // Track write for read-after-write consistency
      this.recentWrites.set(key, Date.now());
      
      // Clean up old entries from recent writes (after 1 minute)
      setTimeout(() => {
        this.recentWrites.delete(key);
      }, 60000);
      
      // Publish event for asynchronous replication to followers
      this.eventBus.publishReplicationEvent(key, value);
      
      return;
    } catch (error) {
      console.error(`Failed to write data: ${key}`, error);
      throw error;
    }
  }
}

// Example usage
async function demo() {
  // Create database nodes
  const leaderDb = new InMemoryDatabase('leader', true);
  const follower1 = new InMemoryDatabase('follower1', false, 50);  // 50ms latency
  const follower2 = new InMemoryDatabase('follower2', false, 100); // 100ms latency
  
  // Create replication manager
  const replicationManager = new DataReplicationManager(
    leaderDb, 
    [follower1, follower2],
    {
      consistencyLevel: ConsistencyLevel.EVENTUAL,
      replicationTimeout: 3000,
      minSuccessfulReplicas: 1
    }
  );
  
  // Example user data
  const userData = {
    id: 'user123',
    name: 'John Doe',
    email: 'john@example.com',
    lastUpdated: new Date().toISOString()
  };
  
  try {
    // Write data
    console.log('Writing user data...');
    await replicationManager.write('user:user123', userData);
    
    // Simulate some delay for replication
    await new Promise(resolve => setTimeout(resolve, 200));
    
    // Read with different consistency levels
    console.log('\nReading with eventual consistency:');
    const eventualData = await replicationManager.read('user:user123', {
      consistencyLevel: ConsistencyLevel.EVENTUAL
    });
    console.log('Data:', eventualData);
    
    console.log('\nReading with strong consistency:');
    const strongData = await replicationManager.read('user:user123', {
      consistencyLevel: ConsistencyLevel.STRONG
    });
    console.log('Data:', strongData);
    
    // Update the data
    console.log('\nUpdating user data...');
    await replicationManager.write('user:user123', {
      ...userData,
      name: 'John Updated',
      lastUpdated: new Date().toISOString()
    });
    
    // Read with read-after-write consistency
    console.log('\nReading with read-after-write consistency:');
    const rawData = await replicationManager.read('user:user123', {
      consistencyLevel: ConsistencyLevel.READ_AFTER_WRITE
    });
    console.log('Data:', rawData);
  } catch (error) {
    console.error('Error in demo:', error);
  }
}

// Run the demo
demo();
```

## When to Use

- For globally distributed applications requiring low latency
- When high availability is critical, even during partial failures
- In systems that need to scale read operations
- When the cost of downtime exceeds the complexity of replication
- For disaster recovery and business continuity planning

## When Not to Use

- When strong consistency is absolutely required and can't be compromised
- For simple applications with a single database instance
- When the added complexity of replication outweighs the benefits
- In systems where transactions frequently span multiple data entities

## Related Patterns

- [Saga Pattern](06-saga-pattern.md)
- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md)
- [Cache-Aside Pattern](07-cache-aside-pattern.md)
- [Sharding Pattern](12-sharding-pattern.md)
