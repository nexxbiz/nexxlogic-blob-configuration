using System.ComponentModel.DataAnnotations;

namespace NexxLogic.BlobConfiguration.AspNetCore.Options;

internal static class BlobConfigurationOptionsValidator
{
    public static ValidationResult? ValidateOptions(BlobConfigurationOptions options, ValidationContext validationContext)
    {
        var validationResults = new List<ValidationResult>();
        
        // Validate using DataAnnotations first
        if (!Validator.TryValidateObject(options, validationContext, validationResults, validateAllProperties: true))
        {
            var errorMessages = validationResults
                .Where(r => !string.IsNullOrWhiteSpace(r.ErrorMessage))
                .Select(r => r.ErrorMessage!);

            var combinedMessage = string.Join(" ", errorMessages);

            if (string.IsNullOrWhiteSpace(combinedMessage))
            {
                combinedMessage = "One or more validation errors occurred.";
            }

            return new ValidationResult(combinedMessage);
        }

        // Custom validation rules
        var customValidationErrors = new List<string>();

        // ReloadInterval validation when ReloadOnChange is enabled
        if (options is { ReloadOnChange: true, ReloadInterval: < 5000 })
        {
            customValidationErrors.Add("ReloadInterval must be at least 5000 milliseconds when ReloadOnChange is enabled.");
        }

        // Connection string and blob container URL mutual exclusion
        var hasConnectionString = !string.IsNullOrWhiteSpace(options.ConnectionString);
        var hasBlobContainerUrl = !string.IsNullOrWhiteSpace(options.BlobContainerUrl);

        switch (hasConnectionString)
        {
            case true when hasBlobContainerUrl:
                customValidationErrors.Add("Cannot specify both ConnectionString and BlobContainerUrl. Please choose one.");
                break;
            case false when !hasBlobContainerUrl:
                customValidationErrors.Add("Either ConnectionString or BlobContainerUrl must be specified.");
                break;
        }

        // Container name validation
        if (string.IsNullOrWhiteSpace(options.ContainerName))
        {
            customValidationErrors.Add("ContainerName is required.");
        }

        if (customValidationErrors.Count > 0)
        {
            var errorMessage = string.Join(" ", customValidationErrors);
            return new ValidationResult(errorMessage);
        }

        return ValidationResult.Success;
    }

    public static void ValidateAndThrow(BlobConfigurationOptions options)
    {
        var validationContext = new ValidationContext(options);
        var validationResult = ValidateOptions(options, validationContext);
        
        if (validationResult != ValidationResult.Success)
        {
            throw new ArgumentException($"Invalid BlobConfiguration values: {validationResult?.ErrorMessage}", nameof(options));
        }
    }
}