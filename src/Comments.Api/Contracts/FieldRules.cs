namespace Threadline.Comments.Api.Contracts;

public sealed record FieldRules
{
    public bool Required { get; init; }

    public string? Pattern { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public string? Description { get; init; }
}
