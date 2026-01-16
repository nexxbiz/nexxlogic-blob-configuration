using System.ComponentModel.DataAnnotations;

namespace NexxLogic.BlobConfiguration.AspNetCore.Options;

internal static class RequiredBlobNameBlobConfigurationOptionsValidator
{
    public static ValidationResult? ValidateOptions(BlobConfigurationOptions options, ValidationContext validationContext)
    {
        // First run the base validation
        var baseValidation = BlobConfigurationOptionsValidator.ValidateOptions(options, validationContext);
        if (baseValidation != ValidationResult.Success)
        {
            return baseValidation;
        }

        // Additional validation for BlobName
        if (string.IsNullOrWhiteSpace(options.BlobName))
        {
            return new ValidationResult("BlobName is required.");
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