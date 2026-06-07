using System.ComponentModel.DataAnnotations.Schema;
using EntityFrameworkCore.CreatedUpdatedDate.Contracts;

namespace InCleanHome.IamService.Domain.Model.Aggregates;

/// <summary>
///     A document uploaded by a worker during onboarding (background check / experience).
///     The file is stored as base64 — pluggable later to S3/Blob if needed.
/// </summary>
public class WorkerDocument : IEntityWithCreatedUpdatedDate
{
    public int Id { get; private set; }
    public int UserId { get; private set; }
    public string DocumentType { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string FileBase64 { get; private set; } = string.Empty;

    [Column("CreatedAt")] public DateTimeOffset? CreatedDate { get; set; }
    [Column("UpdatedAt")] public DateTimeOffset? UpdatedDate { get; set; }

    public WorkerDocument() { }

    public WorkerDocument(int userId, string documentType, string fileName, string fileBase64)
    {
        UserId       = userId;
        DocumentType = documentType;
        FileName     = fileName;
        FileBase64   = fileBase64;
    }
}
