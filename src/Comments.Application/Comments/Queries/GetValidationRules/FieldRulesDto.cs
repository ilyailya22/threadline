namespace Threadline.Comments.Application.Comments.Queries.GetValidationRules;

/// <summary>Constraints on one form field, in a form the client can turn into validators.</summary>
public sealed record FieldRulesDto
{
    public bool Required { get; init; }

    public string? Pattern { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public string? Description { get; init; }
}
