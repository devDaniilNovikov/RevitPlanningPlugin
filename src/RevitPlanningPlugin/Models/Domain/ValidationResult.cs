using System.Collections.Generic;
using System.Linq;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Models.Domain
{
    public class ValidationIssue
    {
        public ValidationSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Code { get; set; }
    }

    public class ValidationResult
    {
        public List<ValidationIssue> Issues { get; set; } = new();
        public bool IsValid => !Issues.Any(i => i.Severity == ValidationSeverity.Error);
        public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);

        public static ValidationResult Success() => new();

        public static ValidationResult Fail(string message, string? code = null)
        {
            var result = new ValidationResult();
            result.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = message,
                Code = code
            });
            return result;
        }

        public void AddError(string message, string? code = null)
            => Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Error, Message = message, Code = code });

        public void AddWarning(string message, string? code = null)
            => Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Warning, Message = message, Code = code });

        public void AddInfo(string message, string? code = null)
            => Issues.Add(new ValidationIssue { Severity = ValidationSeverity.Info, Message = message, Code = code });
    }
}
