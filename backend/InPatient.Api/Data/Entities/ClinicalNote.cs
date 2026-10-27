using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class ClinicalNote
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int AdmissionId { get; set; }

    public int AuthorUserId { get; set; }

    public string NoteType { get; set; } = null!;

    public string Text { get; set; } = null!;

    public string Source { get; set; } = null!;

    public int? AudioFileId { get; set; }

    public int? SignedOffBy { get; set; }

    public DateTime? SignedOffAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Admission Admission { get; set; } = null!;

    public virtual StoredFile? AudioFile { get; set; }

    public virtual AppUser AuthorUser { get; set; } = null!;

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual AppUser? SignedOffByNavigation { get; set; }
}
