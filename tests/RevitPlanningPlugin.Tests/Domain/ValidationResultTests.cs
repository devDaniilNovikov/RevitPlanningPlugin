using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Tests.Domain
{
    public class ValidationResultTests
    {
        [Fact]
        public void Success_IsValid_True_NoIssues()
        {
            var result = ValidationResult.Success();
            Assert.True(result.IsValid);
            Assert.False(result.HasWarnings);
            Assert.Empty(result.Issues);
        }

        [Fact]
        public void Fail_IsValid_False_HasError()
        {
            var result = ValidationResult.Fail("ошибка", "ERR_CODE");
            Assert.False(result.IsValid);
            Assert.Single(result.Issues);
            Assert.Equal(ValidationSeverity.Error, result.Issues[0].Severity);
            Assert.Equal("ошибка", result.Issues[0].Message);
            Assert.Equal("ERR_CODE", result.Issues[0].Code);
        }

        [Fact]
        public void AddWarning_IsValidStillTrue()
        {
            var result = new ValidationResult();
            result.AddWarning("предупреждение", "WARN_CODE");
            Assert.True(result.IsValid);
            Assert.True(result.HasWarnings);
            Assert.Equal(ValidationSeverity.Warning, result.Issues[0].Severity);
        }

        [Fact]
        public void AddError_MakesIsValidFalse()
        {
            var result = new ValidationResult();
            result.AddError("ошибка");
            Assert.False(result.IsValid);
        }

        [Fact]
        public void AddMultipleIssues_AllPreserved()
        {
            var result = new ValidationResult();
            result.AddWarning("предупреждение 1");
            result.AddWarning("предупреждение 2");
            result.AddError("ошибка 1");
            Assert.Equal(3, result.Issues.Count);
            Assert.False(result.IsValid);
            Assert.True(result.HasWarnings);
        }

        [Fact]
        public void HasWarnings_OnlyErrors_ReturnsFalse()
        {
            var result = new ValidationResult();
            result.AddError("ошибка");
            Assert.False(result.HasWarnings);
        }

        [Fact]
        public void CodeIsOptional_NullCodeAccepted()
        {
            var result = new ValidationResult();
            result.AddError("сообщение"); // no code
            Assert.Null(result.Issues[0].Code);
        }
    }
}
