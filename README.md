# BlobConfigurationProvider
A .NET Configuration Provider using JSON settings from an Azure Blob Storage with enhanced change detection strategies and comprehensive validation.

## 🛡️ Configuration Validation

All timing and size configuration values are validated using .NET DataAnnotations with descriptive error messages:

### Validation Ranges (Enforced via Range Attributes)
- **ReloadInterval**: 1-86400000 ms (1ms to 24h) - allows legacy test values
- **DebounceDelaySeconds**: 0-3600 seconds (0s to 1h) - 0 disables debouncing  
- **WatchingIntervalSeconds**: 1-86400 seconds (1s to 24h) - allows fast polling
- **ErrorRetryDelaySeconds**: 1-7200 seconds (1s to 2h) - allows quick retries
- **MaxFileContentHashSizeMb**: 1-1024 MB

### Single Source of Truth
Validation logic is centralized in `[Range]` attributes on the `BlobConfigurationOptions` properties and automatically enforced at runtime using `Validator.TryValidateObject()`.

### Example Validation Error
```csharp
// ❌ This will throw ArgumentException with clear error messages
builder.Configuration.AddJsonBlob(config => 
{
    config.DebounceDelaySeconds = -5;        // Invalid: negative value
    config.WatchingIntervalSeconds = 0;      // Invalid: must be >= 1  
    config.ErrorRetryDelaySeconds = 10000;   // Invalid: too large (>2h)
}, logger);

// Error: "Invalid BlobConfiguration values:
// DebounceDelaySeconds must be between 0 and 3600 seconds (1 hour). Use 0 to disable debouncing. Current value: -5
// WatchingIntervalSeconds must be between 1 second and 86400 seconds (24 hours). Current value: 0  
// ErrorRetryDelaySeconds must be between 1 second and 7200 seconds (2 hours). Current value: 10000"
```

### Valid Configuration Examples
```csharp
// ✅ Production configuration
builder.Configuration.AddJsonBlob(config => 
{
    config.DebounceDelaySeconds = 30;        // Recommended for production
    config.WatchingIntervalSeconds = 60;     // Balanced polling  
    config.ErrorRetryDelaySeconds = 120;     // Conservative retry
    config.MaxFileContentHashSizeMb = 5;     
}, logger);

// ✅ Test/development configuration
builder.Configuration.AddJsonBlob(config => 
{
    config.ReloadInterval = 1;               // Fast for tests
    config.DebounceDelaySeconds = 0;         // Disabled debouncing
    config.WatchingIntervalSeconds = 1;      // Fast polling
    config.ErrorRetryDelaySeconds = 1;       // Quick retry
}, logger);
```
