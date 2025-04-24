/**
 * Cache-Aside Pattern implementation in TypeScript
 * This example demonstrates how to implement the Cache-Aside (Lazy Loading) pattern
 * to improve performance and reduce load on backend data stores.
 */

import NodeCache from 'node-cache';

// Product entity
interface Product {
  id: string;
  name: string;
  description: string;
  price: number;
  stockQuantity: number;
  lastUpdated: Date;
}

// Repository interface for data access
interface ProductRepository {
  getProductById(productId: string): Promise<Product | null>;
  updateProduct(product: Product): Promise<void>;
}

// Simple console logger
class Logger {
  info(message: string): void {
    console.log(`[INFO] ${message}`);
  }
  
  warn(message: string): void {
    console.log(`[WARN] ${message}`);
  }
  
  error(message: string): void {
    console.log(`[ERROR] ${message}`);
  }
}

// Demo repository that simulates database access
class DemoProductRepository implements ProductRepository {
  private products: Map<string, Product> = new Map();
  private logger: Logger;
  
  constructor(logger: Logger) {
    this.logger = logger;
    
    // Initialize with sample data
    this.products.set('p-001', {
      id: 'p-001',
      name: 'Azure Cloud Services Handbook',
      description: 'Complete guide to Azure cloud services',
      price: 99.99,
      stockQuantity: 50,
      lastUpdated: new Date()
    });
    
    this.products.set('p-002', {
      id: 'p-002',
      name: 'Cloud Resilience Patterns',
      description: 'Implementation patterns for resilient cloud applications',
      price: 79.99,
      stockQuantity: 30,
      lastUpdated: new Date()
    });
  }
  
  async getProductById(productId: string): Promise<Product | null> {
    // Simulate database access latency
    await this.delay(Math.random() * 300 + 200);
    
    this.logger.info(`Repository: Fetching product ${productId} from database`);
    
    const product = this.products.get(productId);
    if (!product) {
      return null;
    }
    
    // Return a copy to avoid unintended modifications
    return { ...product };
  }
  
  async updateProduct(product: Product): Promise<void> {
    // Simulate database access latency
    await this.delay(Math.random() * 300 + 200);
    
    this.logger.info(`Repository: Updating product ${product.id} in database`);
    
    if (!this.products.has(product.id)) {
      throw new Error(`Product with ID ${product.id} not found`);
    }
    
    // Update the product with current timestamp
    product.lastUpdated = new Date();
    this.products.set(product.id, { ...product });
  }
  
  private delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}

// Service that implements the Cache-Aside pattern
class ProductService {
  private cache: NodeCache;
  private repository: ProductRepository;
  private logger: Logger;
  private defaultCacheDuration: number = 600; // 10 minutes in seconds
  
  constructor(
    cache: NodeCache,
    repository: ProductRepository,
    logger: Logger
  ) {
    this.cache = cache;
    this.repository = repository;
    this.logger = logger;
  }
  
  async getProductById(productId: string, cacheDuration?: number): Promise<Product | null> {
    // Step 1: Try to get the item from the cache
    const cacheKey = `product:${productId}`;
    const cachedProduct = this.cache.get<Product>(cacheKey);
    
    if (cachedProduct) {
      this.logger.info(`Cache hit for product ${productId}`);
      return cachedProduct;
    }
    
    this.logger.info(`Cache miss for product ${productId}`);
    
    // Step 2: On cache miss, get from the repository
    const product = await this.repository.getProductById(productId);
    
    if (!product) {
      this.logger.info(`Product ${productId} not found in repository`);
      return null;
    }
    
    // Step 3: Add to cache
    const ttl = cacheDuration || this.defaultCacheDuration;
    this.cache.set(cacheKey, product, ttl);
    this.logger.info(`Added product ${productId} to cache with TTL ${ttl} seconds`);
    
    return product;
  }
  
  async updateProduct(product: Product): Promise<void> {
    // Update in repository
    await this.repository.updateProduct(product);
    
    // Invalidate cache
    const cacheKey = `product:${product.id}`;
    this.cache.del(cacheKey);
    this.logger.info(`Invalidated cache for product ${product.id}`);
  }
}

// Function to display product information
function displayProduct(product: Product | null): void {
  if (!product) {
    console.log('Product is null');
    return;
  }
  
  console.log(`Product: ${product.id} - ${product.name}`);
  console.log(`  Price: $${product.price.toFixed(2)}, Stock: ${product.stockQuantity}`);
  console.log(`  Last Updated: ${product.lastUpdated}`);
}

// Main function to demonstrate the Cache-Aside pattern
async function main(): Promise<void> {
  const logger = new Logger();
  const repository = new DemoProductRepository(logger);
  
  // Configure cache with default TTL and checking period
  const cache = new NodeCache({
    stdTTL: 600,          // Default TTL in seconds
    checkperiod: 120,     // Check for expired keys every 120 seconds
    useClones: true       // Use deep clones for objects
  });
  
  const productService = new ProductService(cache, repository, logger);
  
  logger.info('Cache-Aside Pattern Demo\n');
  
  // First retrieval - Cache miss, fetches from repository
  logger.info('Fetching product first time (expect cache miss):');
  const product1 = await productService.getProductById('p-001');
  displayProduct(product1);
  
  // Second retrieval - Cache hit
  logger.info('\nFetching same product again (expect cache hit):');
  const product2 = await productService.getProductById('p-001');
  displayProduct(product2);
  
  // Update product - invalidates cache
  logger.info('\nUpdating product:');
  if (product2) {
    product2.price = 129.99;
    product2.stockQuantity = 42;
    await productService.updateProduct(product2);
  }
  
  // Fetch after update - Cache miss again
  logger.info('\nFetching product after update (expect cache miss):');
  const product3 = await productService.getProductById('p-001');
  displayProduct(product3);
  
  // Fetch another product that doesn't exist
  logger.info('\nFetching non-existent product:');
  const nonExistentProduct = await productService.getProductById('p-999');
  if (!nonExistentProduct) {
    logger.info('Product not found.');
  }
  
  // Demonstrate cache expiration
  logger.info('\nDemonstrating cache expiration:');
  logger.info('Fetching product with short TTL:');
  const shortTtlProduct = await productService.getProductById('p-002', 2); // 2 seconds TTL
  displayProduct(shortTtlProduct);
  
  logger.info('Waiting for cache to expire (3 seconds)...');
  await new Promise(resolve => setTimeout(resolve, 3000));
  
  logger.info('Fetching product again after TTL expired (expect cache miss):');
  const expiredCacheProduct = await productService.getProductById('p-002');
  displayProduct(expiredCacheProduct);
  
  logger.info('\nCache-Aside Pattern demo completed.');
}

// Run the demo
main().catch(error => {
  console.error('Error in Cache-Aside Pattern demo:', error);
});
