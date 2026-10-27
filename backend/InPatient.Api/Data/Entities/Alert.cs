using System;
using System.Collections.Generic;

namespace InPatient.Api.Data.Entities;

public partial class Alert
{
    public int Id { get; set; }

    public int HospitalId { get; set; }

    public int AdmissionId { get; set; }

    public string Severity { get; set; } = null!;

    public string AlertType { get; set; } = null!;

    public string RuleCode { get; set; } = null!;

    public string RuleDetail { get; set; } = null!;

    public string? AiExplanation { get; set; }

    public string Status { get; set; } = null!;

    public byte EscalationLevel { get; set; }

    public int? DoseTaskId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastEscalatedAt { get; set; }

    public int? AckBy { get; set; }

    public DateTime? AckAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public virtual AppUser? AckByNavigation { get; set; }

    public virtual Admission Admission { get; set; } = null!;

    public virtual DoseTask? DoseTask { get; set; }

    public virtual Hospital Hospital { get; set; } = null!;
}
