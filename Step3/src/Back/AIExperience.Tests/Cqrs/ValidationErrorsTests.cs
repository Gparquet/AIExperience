using AIExperience.Rag.Application.Common.Cqrs;
using FluentAssertions;

namespace AIExperience.Tests.Cqrs;

/// <summary>Tests TDD du collecteur d'erreurs de validation utilisé par les commandes CQRS maison.</summary>
public sealed class ValidationErrorsTests
{
    [Fact]
    public void HasErrors_NoRuleAdded_IsFalse()
    {
        var errors = new ValidationErrors();

        errors.HasErrors.Should().BeFalse();
        errors.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AddIf_ConditionTrue_AddsError()
    {
        var errors = new ValidationErrors();

        errors.AddIf(true, "FileName", "Le nom du fichier est obligatoire.");

        errors.HasErrors.Should().BeTrue();
        errors.Errors.Should().ContainSingle(e => e.Property == "FileName" && e.Message == "Le nom du fichier est obligatoire.");
    }

    [Fact]
    public void AddIf_ConditionFalse_DoesNotAddError()
    {
        var errors = new ValidationErrors();

        errors.AddIf(false, "FileName", "Le nom du fichier est obligatoire.");

        errors.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void NotEmpty_NullOrWhitespace_AddsError()
    {
        var errors = new ValidationErrors();

        errors.NotEmpty(null, "UserId", "L'identifiant utilisateur est obligatoire.");
        errors.NotEmpty("   ", "UserId", "L'identifiant utilisateur est obligatoire.");

        errors.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void NotEmpty_NonEmptyValue_DoesNotAddError()
    {
        var errors = new ValidationErrors();

        errors.NotEmpty("document.pdf", "FileName", "Le nom du fichier est obligatoire.");

        errors.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void MaxLength_ValueTooLong_AddsError()
    {
        var errors = new ValidationErrors();

        errors.MaxLength("abcdef", 3, "FileName", "Trop long.");

        errors.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void MaxLength_ValueWithinLimit_DoesNotAddError()
    {
        var errors = new ValidationErrors();

        errors.MaxLength("abc", 3, "FileName", "Trop long.");

        errors.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void MaxLength_NullValue_DoesNotAddError()
    {
        var errors = new ValidationErrors();

        errors.MaxLength(null, 3, "FileName", "Trop long.");

        errors.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void MultipleRules_PreserveInsertionOrder()
    {
        var errors = new ValidationErrors();

        errors.NotEmpty(null, "FileName", "Premier message.")
              .AddIf(true, "ContentType", "Second message.");

        errors.Errors.Select(e => e.Property).Should().ContainInOrder("FileName", "ContentType");
    }
}
