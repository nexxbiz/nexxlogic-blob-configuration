# BlobConfigurationProvider
A .NET Configuration Provider using JSON settings from an Azure Blob Storage with enhanced change detection strategies and comprehensive validation.

## 🛡️ Configuration Validation

All timing and size configuration values are validated using .NET DataAnnotations with descriptive error messages:

### Validation Ranges (Enforced via Range Attributes)
- **ReloadInterval**: 1000-86400000 ms (1s to 24h)
- **DebounceDelay**: 0s-1h (TimeSpan) - 0 disables debouncing  
- **WatchingInterval**: 1s-24h (TimeSpan) - allows fast polling
- **ErrorRetryDelay**: 1s-2h (TimeSpan) - allows quick retries
- **MaxFileContentHashSizeMb**: 1-1024 MB

### Validation Sources
The primary validation ranges are expressed via `[Range]` attributes on the `BlobConfigurationOptions` properties and enforced at startup using `Validator.TryValidateObject()`. In addition, `BlobFileProvider.ValidateConfiguration` performs extra runtime guard checks (for example, on `TimeSpan` values) and may emit its own, more specific error messages when an invalid configuration slips through.

### Example Validation Error
```csharp
// ❌ This will throw ArgumentException with clear error messages
builder.Configuration.AddJsonBlob(config => 
{
    config.DebounceDelay = TimeSpan.FromSeconds(-5);        // Invalid: negative value
    config.WatchingInterval = TimeSpan.Zero;                // Invalid: must be >= 1s  
    config.ErrorRetryDelay = TimeSpan.FromHours(3);         // Invalid: too large (>2h)
}, logger);

// Error: "Invalid BlobConfiguration values:
// DebounceDelay must be between 0 seconds and 1 hour. Use 0 to disable debouncing.
// WatchingInterval must be between 1 second and 24 hours.  
// ErrorRetryDelay must be between 1 second and 2 hours."
```

### Valid Configuration Examples
```csharp
// ✅ Production configuration
builder.Configuration.AddJsonBlob(config => 
{
    config.DebounceDelay = TimeSpan.FromSeconds(30);        // Recommended for production
    config.WatchingInterval = TimeSpan.FromSeconds(60);     // Balanced polling  
    config.ErrorRetryDelay = TimeSpan.FromSeconds(120);     // Conservative retry
    config.MaxFileContentHashSizeMb = 5;     
}, logger);

// ✅ Test/development configuration
builder.Configuration.AddJsonBlob(config => 
{
    config.ReloadInterval = 1000;                           // Fast for tests (1 second)
    config.DebounceDelay = TimeSpan.Zero;                   // Disabled debouncing
    config.WatchingInterval = TimeSpan.FromSeconds(1);      // Fast polling
    config.ErrorRetryDelay = TimeSpan.FromSeconds(1);       // Quick retry
}, logger);
```
