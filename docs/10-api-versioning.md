# API Versioning and Backward Compatibility Pattern

## Overview

The API Versioning and Backward Compatibility pattern ensures that service interfaces can evolve over time without breaking existing clients. This pattern is essential for maintaining resilience during service evolution, especially in microservices architectures where different components may be updated independently.

## Problem Statement

As cloud applications evolve, their APIs need to change to support new features, fix issues, or improve performance. However, making changes to APIs can break clients that depend on the existing interface, leading to system failures or degraded functionality.

## Solution: API Versioning and Backward Compatibility

Implement a structured approach to evolving APIs while maintaining compatibility with existing clients:

1. **Version your APIs explicitly**: Use version numbers in URLs, headers, or parameters
2. **Support multiple API versions concurrently**: Maintain older versions alongside newer ones
3. **Implement graceful degradation for legacy clients**: Handle missing parameters or features
4. **Document breaking vs. non-breaking changes**: Clear communication about compatibility implications
5. **Use feature flags for safer transitions**: Gradually roll out new API features

## Benefits

- Enables continuous delivery without disrupting existing clients
- Provides a clear migration path for clients to newer API versions
- Reduces the risk of system-wide failures during upgrades
- Allows different parts of the system to evolve at different rates
- Improves overall system resilience during periods of change

## Implementation Considerations

- Consider the overhead of maintaining multiple API versions
- Define a clear deprecation policy for older API versions
- Balance flexibility (backward compatibility) with technical debt
- Test all supported API versions with automation
- Use consumer-driven contract testing to validate compatibility

## Code Example: API Versioning in C# (ASP.NET Core)

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace ResillientApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // Add API versioning services
            services.AddApiVersioning(options =>
            {
                // Default API version when not specified
                options.DefaultApiVersion = new ApiVersion(1, 0);
                
                // Assume that the caller wants the default version if they don't specify
                options.AssumeDefaultVersionWhenUnspecified = true;
                
                // Report available API versions in response headers
                options.ReportApiVersions = true;
                
                // Support versioning via: URL path segment, query string, and header
                options.ApiVersionReader = ApiVersionReader.Combine(
                    new UrlSegmentApiVersionReader(),
                    new QueryStringApiVersionReader("api-version"),
                    new HeaderApiVersionReader("X-API-Version"));
            });

            // Add API explorer for Swagger or other documentation tools
            services.AddVersionedApiExplorer(options =>
            {
                // Format version as 'v'major[.minor]
                options.GroupNameFormat = "'v'VVV";
                
                // Substitute the version in the controller route
                options.SubstituteApiVersionInUrl = true;
            });

            // Register services for different API versions
            services.AddScoped<V1.IUserService, V1.UserService>();
            services.AddScoped<V2.IUserService, V2.UserService>();

            services.AddControllers();

            // Add Swagger for API documentation
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { 
                    Title = "My API V1", 
                    Version = "v1",
                    Description = "Original API version"
                });
                
                c.SwaggerDoc("v2", new Microsoft.OpenApi.Models.OpenApiInfo { 
                    Title = "My API V2", 
                    Version = "v2",
                    Description = "Enhanced API with additional user fields"
                });
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Configure middleware pipeline
            
            app.UseRouting();
            app.UseAuthorization();
            
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            
            // Configure Swagger for API documentation
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
                c.SwaggerEndpoint("/swagger/v2/swagger.json", "My API V2");
            });
        }
    }
}

// V1 API Controller
namespace ResillientApi.V1.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/users")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        
        public UsersController(IUserService userService)
        {
            _userService = userService;
        }
        
        [HttpGet("{id}")]
        public IActionResult GetUser(int id)
        {
            var user = _userService.GetUser(id);
            
            if (user == null)
                return NotFound();
                
            return Ok(user);
        }
        
        [HttpPost]
        public IActionResult CreateUser(CreateUserRequestV1 request)
        {
            var userId = _userService.CreateUser(request);
            return CreatedAtAction(nameof(GetUser), new { id = userId, version = "1.0" }, null);
        }
    }
    
    public class CreateUserRequestV1
    {
        public string Username { get; set; }
        public string Email { get; set; }
    }
}

// V2 API Controller - Enhanced with more fields and functionality
namespace ResillientApi.V2.Controllers
{
    [ApiController]
    [ApiVersion("2.0")]
    [Route("api/v{version:apiVersion}/users")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        
        public UsersController(IUserService userService)
        {
            _userService = userService;
        }
        
        [HttpGet("{id}")]
        public IActionResult GetUser(int id)
        {
            var user = _userService.GetUser(id);
            
            if (user == null)
                return NotFound();
                
            return Ok(user);
        }
        
        [HttpPost]
        public IActionResult CreateUser(CreateUserRequestV2 request)
        {
            var userId = _userService.CreateUser(request);
            return CreatedAtAction(nameof(GetUser), new { id = userId, version = "2.0" }, null);
        }
        
        // New endpoint only available in V2
        [HttpGet("{id}/preferences")]
        public IActionResult GetUserPreferences(int id)
        {
            var preferences = _userService.GetUserPreferences(id);
            
            if (preferences == null)
                return NotFound();
                
            return Ok(preferences);
        }
    }
    
    public class CreateUserRequestV2
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public string PhoneNumber { get; set; }
        public UserPreferences Preferences { get; set; }
    }
    
    public class UserPreferences
    {
        public bool ReceiveNotifications { get; set; }
        public string Language { get; set; }
        public string Theme { get; set; }
    }
}
```

## Code Example: API Versioning in TypeScript (Express.js)

```typescript
import express, { Request, Response, NextFunction } from 'express';
import { body, validationResult } from 'express-validator';

// Initialize Express app
const app = express();
app.use(express.json());

// Middleware to extract API version
const extractApiVersion = (req: Request, res: Response, next: NextFunction) => {
  // Get version from URL path, header, or query parameter
  const urlVersion = req.path.match(/\/v(\d+)\//);
  const headerVersion = req.header('X-API-Version');
  const queryVersion = req.query['api-version'];
  
  // Set version in request object (prioritizing URL, then header, then query param)
  if (urlVersion) {
    req.apiVersion = parseInt(urlVersion[1], 10);
  } else if (headerVersion) {
    req.apiVersion = parseInt(headerVersion, 10);
  } else if (queryVersion) {
    req.apiVersion = parseInt(queryVersion as string, 10);
  } else {
    // Default to version 1 if not specified
    req.apiVersion = 1;
  }
  
  next();
};

// Add custom property to Request type
declare global {
  namespace Express {
    interface Request {
      apiVersion: number;
    }
  }
}

// Apply version extraction middleware
app.use(extractApiVersion);

// Version-aware middleware
const versionCompatibility = (req: Request, res: Response, next: NextFunction) => {
  // Example: Transform request data for backward compatibility
  if (req.method === 'POST' && req.path.includes('/users')) {
    if (req.apiVersion === 1 && req.body.fullName && !req.body.username) {
      // V1 API expects username, so extract it from fullName for backward compatibility
      req.body.username = req.body.fullName.split(' ')[0].toLowerCase();
    }
  }
  next();
};

app.use(versionCompatibility);

// Add version information to responses
app.use((req: Request, res: Response, next: NextFunction) => {
  const originalJson = res.json;
  
  res.json = function(body: any) {
    // Only add version info to successful JSON responses
    if (res.statusCode >= 200 && res.statusCode < 300 && typeof body === 'object') {
      body.apiVersion = req.apiVersion;
    }
    return originalJson.call(this, body);
  };
  
  next();
});

// V1 User API Routes
app.post('/v1/users', [
  body('username').isString().notEmpty(),
  body('email').isEmail(),
], (req: Request, res: Response) => {
  const errors = validationResult(req);
  if (!errors.isEmpty()) {
    return res.status(400).json({ errors: errors.array() });
  }
  
  // V1 User creation logic
  const { username, email } = req.body;
  const userId = createUserV1({ username, email });
  
  res.status(201).json({ id: userId });
});

app.get('/v1/users/:id', (req: Request, res: Response) => {
  const user = getUserV1(parseInt(req.params.id, 10));
  
  if (!user) {
    return res.status(404).json({ error: 'User not found' });
  }
  
  res.json(user);
});

// V2 User API Routes - More fields and capabilities
app.post('/v2/users', [
  body('username').isString().notEmpty(),
  body('email').isEmail(),
  body('fullName').isString().notEmpty(),
  body('phoneNumber').optional().isMobilePhone('any'),
  body('preferences').optional().isObject(),
], (req: Request, res: Response) => {
  const errors = validationResult(req);
  if (!errors.isEmpty()) {
    return res.status(400).json({ errors: errors.array() });
  }
  
  // V2 User creation logic
  const userId = createUserV2(req.body);
  
  res.status(201).json({ id: userId });
});

app.get('/v2/users/:id', (req: Request, res: Response) => {
  const user = getUserV2(parseInt(req.params.id, 10));
  
  if (!user) {
    return res.status(404).json({ error: 'User not found' });
  }
  
  res.json(user);
});

// V2 only endpoint
app.get('/v2/users/:id/preferences', (req: Request, res: Response) => {
  const preferences = getUserPreferences(parseInt(req.params.id, 10));
  
  if (!preferences) {
    return res.status(404).json({ error: 'User or preferences not found' });
  }
  
  res.json(preferences);
});

// Fallback for unsupported API versions
app.use((req: Request, res: Response, next: NextFunction) => {
  if (req.apiVersion > 2) {
    return res.status(400).json({
      error: 'Unsupported API version',
      message: 'This API version is not supported. Please use version 1 or 2.',
      supportedVersions: [1, 2]
    });
  }
  next();
});

// Implementation details (simplified)
function createUserV1(userData: { username: string; email: string }): number {
  // Database operations...
  return 123; // Return new user ID
}

function createUserV2(userData: any): number {
  // Enhanced user creation with more fields...
  return 456; // Return new user ID
}

function getUserV1(id: number) {
  // Return basic user info
  return { id, username: 'user1', email: 'user1@example.com' };
}

function getUserV2(id: number) {
  // Return enhanced user info
  return {
    id,
    username: 'user1',
    email: 'user1@example.com',
    fullName: 'User One',
    phoneNumber: '+1234567890',
    registrationDate: '2023-04-01T12:00:00Z'
  };
}

function getUserPreferences(id: number) {
  return {
    receiveNotifications: true,
    language: 'en',
    theme: 'light'
  };
}

// Start server
const PORT = process.env.PORT || 3000;
app.listen(PORT, () => {
  console.log(`Server running on port ${PORT}`);
});
```

## When to Use

- When building long-lived APIs with evolving requirements
- In microservices architectures where services evolve independently
- When you have multiple client applications with different upgrade cycles
- For public APIs with external developers as consumers
- When implementing continuous delivery with zero downtime requirements

## When Not to Use

- For internal-only APIs with fully synchronized deployments
- When all clients can be updated simultaneously with the API
- For simple applications with limited API surface area
- When the overhead of maintaining multiple versions outweighs the benefits

## Related Patterns

- [Graceful Degradation Pattern](07-graceful-degradation-pattern.md)
- [Circuit Breaker Pattern](03-circuit-breaker-pattern.md)
- [Fallback Pattern](06-fallback-pattern.md)
