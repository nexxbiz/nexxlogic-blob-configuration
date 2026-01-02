# BlobConfigurationProvider
A .NET Configuration Provider using JSON settings from an Azure Blob Storage with enhanced change detection strategies and comprehensive validation.

## 🛡️ Configuration Validation

All timing and size configuration values are validated at runtime with descriptive error messages:

### Validation Ranges
- **ReloadInterval**: 1000-86400000 ms (1s to 24h)
- **DebounceDelaySeconds**: 1-3600 seconds (1s to 1h)  
- **WatchingIntervalSeconds**: 5-86400 seconds (5s to 24h)
- **ErrorRetryDelaySeconds**: 5-7200 seconds (5s to 2h)
- **MaxFileContentHashSizeMb**: 1-1024 MB

### Example Validation Error
```csharp
// ❌ This will throw ArgumentException
builder.Configuration.AddJsonBlob(config => 
{
    config.DebounceDelaySeconds = -5;        // Invalid: negative value
    config.WatchingIntervalSeconds = 0;      // Invalid: too small
    config.ErrorRetryDelaySeconds = 10000;   // Invalid: too large (>2h)
}, logger);

// Error: "Invalid BlobConfiguration values:
// DebounceDelaySeconds (-5) must be between 1 and 3600 seconds (1 hour)
// WatchingIntervalSeconds (0) must be between 5 seconds and 86400 seconds (24 hours)  
// ErrorRetryDelaySeconds (10000) must be between 5 seconds and 7200 seconds (2 hours)"
```

### Valid Configuration Example
```csharp
builder.Configuration.AddJsonBlob(config => 
{
    config.ConnectionStringKey = "BlobStorage";
    config.ContainerName = "configuration";
    config.BlobName = "appsettings.json";
    config.ChangeDetectionStrategy = ChangeDetectionStrategy.ContentBased;
    config.DebounceDelaySeconds = 30;        // ✅ Valid: 1-3600 range
    config.WatchingIntervalSeconds = 60;     // ✅ Valid: 5-86400 range  
    config.ErrorRetryDelaySeconds = 120;     // ✅ Valid: 5-7200 range
    config.MaxFileContentHashSizeMb = 5;     // ✅ Valid: 1-1024 range
}, logger);
```
