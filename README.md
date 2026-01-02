# BlobConfigurationProvider
A .NET Configuration Provider using JSON settings from an Azure Blob Storage with enhanced change detection strategies and comprehensive validation.

## 🛡️ Configuration Validation

All timing and size configuration values are validated at runtime with descriptive error messages:

### Validation Ranges (Backward Compatible)
- **ReloadInterval**: 1-86400000 ms (1ms to 24h) - allows legacy test values
- **DebounceDelaySeconds**: 0-3600 seconds (0s to 1h) - 0 disables debouncing  
- **WatchingIntervalSeconds**: 1-86400 seconds (1s to 24h) - allows fast polling
- **ErrorRetryDelaySeconds**: 1-7200 seconds (1s to 2h) - allows quick retries
- **MaxFileContentHashSizeMb**: 1-1024 MB

### Example Validation Error
```csharp
// ❌ This will throw ArgumentException for truly invalid values
builder.Configuration.AddJsonBlob(config => 
{
    config.DebounceDelaySeconds = -5;        // Invalid: negative value
    config.WatchingIntervalSeconds = 0;      // Invalid: must be >= 1  
    config.ErrorRetryDelaySeconds = 10000;   // Invalid: too large (>2h)
}, logger);

// Error: "Invalid BlobConfiguration values:
// DebounceDelaySeconds (-5) must be between 0 and 3600 seconds (1 hour)
// WatchingIntervalSeconds (0) must be between 1 second and 86400 seconds (24 hours)  
// ErrorRetryDelaySeconds (10000) must be between 1 second and 7200 seconds (2 hours)"
```

### Valid Configuration Examples
```csharp
// ✅ Production configuration
builder.Configuration.AddJsonBlob(config => 
{
    config.ChangeDetectionStrategy = ChangeDetectionStrategy.ContentBased;
    config.DebounceDelaySeconds = 30;        // Recommended for production
    config.WatchingIntervalSeconds = 60;     // Balanced polling  
    config.ErrorRetryDelaySeconds = 120;     // Conservative retry
    config.MaxFileContentHashSizeMb = 5;     
}, logger);

// ✅ Test/development configuration (backward compatible)
builder.Configuration.AddJsonBlob(config => 
{
    config.ReloadInterval = 1;               // Fast for tests
    config.DebounceDelaySeconds = 0;         // Disabled debouncing
    config.WatchingIntervalSeconds = 1;      // Fast polling
    config.ErrorRetryDelaySeconds = 1;       // Quick retry
}, logger);
```
