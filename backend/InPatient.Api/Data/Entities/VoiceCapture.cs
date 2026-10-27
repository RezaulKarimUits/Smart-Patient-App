using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class VoiceCapture
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int AdmissionId { get; set; }

    public int UserId { get; set; }

    public string Purpose { get; set; } = null!;

    public int? AudioFileId { get; set; }

    public string? Transcript { get; set; }

    public string? DraftJson { get; set; }

    public string? FinalJson { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public virtual Admission Admission { get; set; } = null!;

    public virtual StoredFile? AudioFile { get; set; }

    public virtual Hospital Hospital { get; set; } = null!;

    public virtual AppUser User { get; set; } = null!;
}
