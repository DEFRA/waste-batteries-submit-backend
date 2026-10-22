using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// POM (placed on market) submission: the quarterly return a battery compliance
// scheme files for one member. Tonnage entries are embedded.
[BsonIgnoreExtraElements]
public sealed record PomSubmissionModel : PomSubmissionBase
{
    // The shape this service writes. A new shape is a code change, so this is not configuration.
    public const int CurrentSchemaVersion = 1;

    private const string SubmittedStatus = "Submitted";

    // MongoDB deserialises through this constructor. The other constructor is for create.
    [BsonConstructor]
    [SetsRequiredMembers]
    public PomSubmissionModel()
    {
        Id = null!;
    }

    // : base(header) uses the compiler copy constructor, so shared header fields are not assigned again.
    [SetsRequiredMembers]
    internal PomSubmissionModel(
        PomSubmissionBase header,
        string id,
        DateTime createdAtUtc,
        List<TonnageEntry> tonnageEntries,
        string status,
        int version,
        string? supersedesSubmissionId = null)
        : base(header)
    {
        if (tonnageEntries is null || tonnageEntries.Count == 0)
        {
            throw new ArgumentException("A submission contains at least one tonnage entry.", nameof(tonnageEntries));
        }

        Id = id;
        CreatedAtUtc = createdAtUtc;
        TonnageEntries = tonnageEntries;
        SchemaVersion = CurrentSchemaVersion;
        Version = version;
        Status = status;
        SubmittedOnUtc = status.Equals(SubmittedStatus, StringComparison.OrdinalIgnoreCase)
            ? createdAtUtc
            : null;
        IsCurrent = true;
        SupersedesSubmissionId = supersedesSubmissionId;
        UpdatedAtUtc = null;
    }

    // Exposed as "id" in the API. Stored as ObjectId _id.
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; init; }

    // Returned on the API. A client uses schemaVersion to tell which vocabulary the stored
    // texts were written against. version is the optimistic concurrency token a later write
    // sends back. The submitter does not post either; the service sets them.
    public int SchemaVersion { get; init; }

    public int Version { get; init; }

    // Null on a first submission. An amendment sets this to the id of the submission it replaces.
    [BsonRepresentation(BsonType.ObjectId)]
    public string? SupersedesSubmissionId { get; init; }

    public bool IsCurrent { get; init; } = true;

    public string Status { get; init; } = string.Empty;

    // TODO: Embed Declaration (the appropriate person's signature: id, firstName, lastName,
    // position, declaredAt) when signing is added. It is omitted from this version.

    public List<TonnageEntry> TonnageEntries { get; init; } = [];

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? SubmittedOnUtc { get; init; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public required DateTime CreatedAtUtc { get; init; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? UpdatedAtUtc { get; init; }
}
